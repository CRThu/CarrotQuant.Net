using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CarrotBacktesting.NET.Abstraction.Data;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// Parquet 格式的纵向序列市场数据源实现（实现 IMarketSeriesSource 契约）。
    /// 所有的物理路径定位与 schema 类型定义均由 IStorageResolver 提供。
    /// 内部维护一个惰性滑动的月份数据列解压缓存。
    /// </summary>
    public class ParquetMarketSeriesSource : IMarketSeriesSource, IDisposable
    {
        private readonly IStorageResolver _resolver;
        private readonly string _tableId;
        private readonly DateTime? _startDate;
        private readonly DateTime? _endDate;
        private readonly List<DateTime> _tradeDates;
        private readonly List<string> _symbols;

        private readonly Dictionary<string, YearCache> _cachePool = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed = false;

        public IReadOnlyList<string> Symbols => _symbols;
        public IReadOnlyList<string> FieldNames => _resolver.GetFieldNames(_tableId);
        public IReadOnlyList<DateTime> TradeDates => _tradeDates;

        /// <summary>
        /// 便捷构造函数。
        /// </summary>
        public ParquetMarketSeriesSource(string storageRoot, IFieldRegistry registry, string tableId, DateTime? startDate = null, DateTime? endDate = null)
            : this(new StorageResolver(storageRoot), registry, tableId, startDate, endDate)
        {
        }

        /// <summary>
        /// 核心构造函数，接收外部依赖注入的路径解析器。
        /// </summary>
        public ParquetMarketSeriesSource(IStorageResolver resolver, IFieldRegistry registry, string tableId, DateTime? startDate = null, DateTime? endDate = null)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _tableId = tableId;
            _startDate = startDate;
            _endDate = endDate;

            // 自动注册 Schema
            foreach (var fieldName in _resolver.GetFieldNames(_tableId))
            {
                registry.RegisterField(fieldName, _resolver.GetFieldType(_tableId, fieldName));
            }

            var symbolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dateSet = new HashSet<DateTime>();

            // 使用 resolver 解析出的所有物理文件列表（已支持年份剪枝）
            var parquetFiles = _resolver.ResolvePhysicalFiles(_tableId, null, _startDate, _endDate);
            foreach (var file in parquetFiles)
            {
                using var fs = File.OpenRead(file);
                var reader = ParquetReader.CreateAsync(fs).GetAwaiter().GetResult();
                try
                {
                    var fields = reader.Schema.GetDataFields();
                    var symField = fields.FirstOrDefault(f => f.Name.Equals("symbol", StringComparison.OrdinalIgnoreCase));
                    var dtField = fields.FirstOrDefault(f => f.Name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
                               ?? fields.FirstOrDefault(f => f.Name.Equals("timestamp", StringComparison.OrdinalIgnoreCase));

                    if (symField != null)
                    {
                        for (int i = 0; i < reader.RowGroupCount; i++)
                        {
                            using var groupReader = reader.OpenRowGroupReader(i);
                            var symCol = ReadColumnFromGroup(groupReader, symField);
                            if (symCol is ReadOnlyMemory<char>[] romArr)
                            {
                                foreach (var m in romArr)
                                {
                                    if (!m.IsEmpty)
                                        symbolSet.Add(m.ToString());
                                }
                            }
                        }
                    }

                    if (dtField != null)
                    {
                        for (int i = 0; i < reader.RowGroupCount; i++)
                        {
                            using var groupReader = reader.OpenRowGroupReader(i);
                            var dtCol = ReadColumnFromGroup(groupReader, dtField);
                            var dates = ConvertToDateTimes(dtCol);
                            foreach (var d in dates)
                            {
                                dateSet.Add(d);
                            }
                        }
                    }
                }
                finally
                {
                    reader.DisposeAsync().GetAwaiter().GetResult();
                }
            }

            _symbols = symbolSet.OrderBy(s => s).ToList();
            _tradeDates = dateSet.OrderBy(d => d).ToList();
        }

        public Type GetFieldType(string fieldName)
        {
            return _resolver.GetFieldType(_tableId, fieldName);
        }

        public int GetSymbolIndex(string symbol)
        {
            return _symbols.IndexOf(symbol);
        }

        public int GetDateIndex(DateTime date)
        {
            return _tradeDates.BinarySearch(date);
        }

        /// <summary>
        /// 批量读取指定股票在交易日区间内的字段行情。
        /// 仅解析目标窗口 [minDate, maxDate] 内的物理文件，零跨文件向左回溯，缺失单元格由对齐器天然置 NaN。
        /// </summary>
        public void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged
        {
            if (startIndex < 0 || startIndex + length > _tradeDates.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Requested range is out of global TradeDates boundaries.");
            }

            if (destination.Length != length)
            {
                throw new ArgumentException($"Destination span length ({destination.Length}) must match requested length ({length}).");
            }

            DateTime minDate = _tradeDates[startIndex];
            DateTime maxDate = _tradeDates[startIndex + length - 1];

            // 获取指定时间区间内的物理数据文件列表（精确剪枝）
            var parquetFiles = _resolver.ResolvePhysicalFiles(_tableId, symbol, minDate, maxDate);

            // 依据目标窗口长度自适应租借池化缓冲区，杜绝硬编码与多余内存放大
            int capacity = Math.Max(length, 16);
            DateTime[] rawDates = ArrayPool<DateTime>.Shared.Rent(capacity);
            T[] rawValues = ArrayPool<T>.Shared.Rent(capacity);
            int count = 0;

            try
            {
                // 依次填充当前窗口内的每个文件数据
                foreach (var filePath in parquetFiles)
                {
                    // 从缓存池中获取，没有则创建
                    if (!_cachePool.TryGetValue(filePath, out var cache))
                    {
                        if (File.Exists(filePath))
                        {
                            cache = new YearCache(filePath);
                            _cachePool[filePath] = cache;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    // 提取个股在该年度的起始物理行偏移量
                    if (!cache.StockRanges.TryGetValue(symbol, out var range))
                    {
                        continue; // 无个股数据或停牌
                    }

                    // 提取字段对应的全列数组缓存
                    Array colArray = cache.GetColumnData(fieldName);

                    int endRow = range.StartRow + range.RowCount;
                    for (int r = range.StartRow; r < endRow; r++)
                    {
                        DateTime rowDate = cache.Dates[r];

                        if (count >= rawDates.Length)
                        {
                            int newCap = rawDates.Length * 2;
                            var newDates = ArrayPool<DateTime>.Shared.Rent(newCap);
                            var newVals = ArrayPool<T>.Shared.Rent(newCap);
                            rawDates.AsSpan(0, count).CopyTo(newDates);
                            rawValues.AsSpan(0, count).CopyTo(newVals);
                            ArrayPool<DateTime>.Shared.Return(rawDates);
                            ArrayPool<T>.Shared.Return(rawValues);
                            rawDates = newDates;
                            rawValues = newVals;
                        }

                        rawDates[count] = rowDate;
                        rawValues[count] = ConvertArrayValue<T>(colArray, r);
                        count++;
                    }
                }

                if (count > 1 && rawDates[0] > rawDates[count - 1])
                {
                    Array.Sort(rawDates, rawValues, 0, count);
                }

                // 通过 MarketDataAligner 完成全局日历投影，缺失位置天然保留 NaN
                MarketDataAligner.AlignAndFill(
                    _tradeDates,
                    startIndex,
                    length,
                    rawDates.AsSpan(0, count),
                    rawValues.AsSpan(0, count),
                    destination);
            }
            finally
            {
                ArrayPool<DateTime>.Shared.Return(rawDates);
                ArrayPool<T>.Shared.Return(rawValues);
            }
        }

        private static Array ReadColumnFromGroup(ParquetRowGroupReader groupReader, DataField dataField)
        {
            int rowCount = (int)groupReader.RowCount;
            Type readType = dataField.ClrType;

            Array buffer = Array.CreateInstance(readType, rowCount);

            if (readType == typeof(ReadOnlyMemory<char>))
            {
                int[] defLevels = new int[rowCount];
                var memBuf = (ReadOnlyMemory<char>[])buffer;
                groupReader.ReadRawAsync<ReadOnlyMemory<char>>(dataField, memBuf, defLevels, null, default).GetAwaiter().GetResult();
                for (int i = 0; i < rowCount; i++)
                {
                    if (defLevels[i] == 0) memBuf[i] = ReadOnlyMemory<char>.Empty;
                }
            }
            else
            {
                int[] defLevels = new int[rowCount];

                if (readType == typeof(double)) ReadColumnRawTyped<double>(groupReader, dataField, (double[])buffer, defLevels, double.NaN);
                else if (readType == typeof(float)) ReadColumnRawTyped<float>(groupReader, dataField, (float[])buffer, defLevels, float.NaN);
                else if (readType == typeof(long)) ReadColumnRawTyped<long>(groupReader, dataField, (long[])buffer, defLevels, 0);
                else if (readType == typeof(int)) ReadColumnRawTyped<int>(groupReader, dataField, (int[])buffer, defLevels, 0);
                else if (readType == typeof(bool)) ReadColumnRawTyped<bool>(groupReader, dataField, (bool[])buffer, defLevels, false);
                else if (readType == typeof(DateTimeOffset))
                {
                    groupReader.ReadRawAsync<DateTimeOffset>(dataField, (DateTimeOffset[])buffer, defLevels, null, default).GetAwaiter().GetResult();
                }
                else
                {
                    throw new NotSupportedException($"Type {readType} is not supported by the optimized Parquet loader.");
                }
            }

            return buffer;
        }

        private static void ReadColumnRawTyped<T>(ParquetRowGroupReader groupReader, DataField field, T[] buffer, int[] defLevels, T defaultValue)
            where T : unmanaged
        {
            groupReader.ReadRawAsync<T>(field, buffer, defLevels, null, default).GetAwaiter().GetResult();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (defLevels[i] == 0) buffer[i] = defaultValue;
            }
        }

        private static List<DateTime> ConvertToDateTimes(object columnData)
        {
            var dates = new List<DateTime>();
            if (columnData is long[] tsArr)
            {
                foreach (var ts in tsArr)
                {
                    dates.Add(DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime.Date);
                }
            }
            else if (columnData is DateTime[] dtArr)
            {
                foreach (var dt in dtArr)
                {
                    dates.Add(dt.Date);
                }
            }
            else if (columnData is ReadOnlyMemory<char>[] romArr)
            {
                foreach (var rom in romArr)
                {
                    if (DateTime.TryParse(rom.Span, out var dt))
                    {
                        dates.Add(dt.Date);
                    }
                }
            }
            return dates;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T ConvertArrayValue<T>(Array arr, int index) where T : unmanaged
        {
            if (arr is double[] dArr)
            {
                if (typeof(T) == typeof(double))
                {
                    double val = dArr[index];
                    return Unsafe.As<double, T>(ref val);
                }
                if (typeof(T) == typeof(float))
                {
                    float val = (float)dArr[index];
                    return Unsafe.As<float, T>(ref val);
                }
            }
            if (arr is float[] fArr)
            {
                if (typeof(T) == typeof(float))
                {
                    float val = fArr[index];
                    return Unsafe.As<float, T>(ref val);
                }
                if (typeof(T) == typeof(double))
                {
                    double val = fArr[index];
                    return Unsafe.As<double, T>(ref val);
                }
            }
            if (arr is long[] lArr)
            {
                if (typeof(T) == typeof(long))
                {
                    long val = lArr[index];
                    return Unsafe.As<long, T>(ref val);
                }
                if (typeof(T) == typeof(double))
                {
                    double val = lArr[index];
                    return Unsafe.As<double, T>(ref val);
                }
            }
            if (arr is int[] iArr)
            {
                if (typeof(T) == typeof(int))
                {
                    int val = iArr[index];
                    return Unsafe.As<int, T>(ref val);
                }
                if (typeof(T) == typeof(double))
                {
                    double val = iArr[index];
                    return Unsafe.As<double, T>(ref val);
                }
            }
            if (arr is bool[] bArr)
            {
                if (typeof(T) == typeof(bool))
                {
                    bool val = bArr[index];
                    return Unsafe.As<bool, T>(ref val);
                }
            }

            object valObj = arr.GetValue(index)!;
            return (T)Convert.ChangeType(valObj, typeof(T));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var cache in _cachePool.Values)
                {
                    cache.Dispose();
                }
                _cachePool.Clear();
                _disposed = true;
            }
        }

        /// <summary>
        /// 维护单年 Parquet 数据文件的按需列解压缓存与证券索引偏移映射。
        /// </summary>
        private class YearCache : IDisposable
        {
            public string FilePath { get; }

            public Dictionary<string, (int StartRow, int RowCount)> StockRanges { get; }
            public DateTime[] Dates { get; }

            private readonly FileStream _fs;
            private readonly ParquetReader _parquetReader;
            private readonly Dictionary<string, Array> _columns;
            private readonly DataField[] _dataFields;

            public YearCache(string filePath)
            {
                FilePath = filePath;
                _columns = new Dictionary<string, Array>(StringComparer.OrdinalIgnoreCase);

                _fs = File.OpenRead(filePath);
                _parquetReader = ParquetReader.CreateAsync(_fs).GetAwaiter().GetResult();
                _dataFields = _parquetReader.Schema.GetDataFields();

                var symField = _dataFields.FirstOrDefault(f => f.Name.Equals("symbol", StringComparison.OrdinalIgnoreCase));
                var dtField = _dataFields.FirstOrDefault(f => f.Name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
                           ?? _dataFields.FirstOrDefault(f => f.Name.Equals("timestamp", StringComparison.OrdinalIgnoreCase));

                if (symField == null || dtField == null)
                {
                    throw new InvalidDataException($"Required 'symbol' or date column is missing in: {filePath}");
                }

                var symList = new List<string>();
                var dateList = new List<DateTime>();

                for (int i = 0; i < _parquetReader.RowGroupCount; i++)
                {
                    using var groupReader = _parquetReader.OpenRowGroupReader(i);
                    var symData = ReadColumnFromGroup(groupReader, symField);
                    if (symData is ReadOnlyMemory<char>[] romArr)
                    {
                        foreach (var m in romArr)
                        {
                            symList.Add(m.ToString());
                        }
                    }

                    var dtData = ReadColumnFromGroup(groupReader, dtField);
                    dateList.AddRange(ConvertToDateTimes(dtData));
                }

                Dates = dateList.ToArray();

                StockRanges = new Dictionary<string, (int StartRow, int RowCount)>(StringComparer.OrdinalIgnoreCase);
                if (symList.Count > 0)
                {
                    string currentSym = symList[0];
                    int startIdx = 0;
                    for (int i = 1; i < symList.Count; i++)
                    {
                        if (symList[i] != currentSym)
                        {
                            StockRanges[currentSym] = (startIdx, i - startIdx);
                            currentSym = symList[i];
                            startIdx = i;
                        }
                    }
                    StockRanges[currentSym] = (startIdx, symList.Count - startIdx);
                }
            }

            public Array GetColumnData(string fieldName)
            {
                if (_columns.TryGetValue(fieldName, out var cachedArray))
                {
                    return cachedArray;
                }

                var targetField = _dataFields.FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                if (targetField == null)
                {
                    throw new ArgumentException($"Field '{fieldName}' not found in the Parquet schema.");
                }

                int totalRows = Dates.Length;
                Array mergedArray = Array.CreateInstance(targetField.ClrType, totalRows);

                int currentOffset = 0;
                for (int i = 0; i < _parquetReader.RowGroupCount; i++)
                {
                    using var groupReader = _parquetReader.OpenRowGroupReader(i);
                    Array chunkData = (Array)ReadColumnFromGroup(groupReader, targetField);
                    
                    Array.Copy(chunkData, 0, mergedArray, currentOffset, chunkData.Length);
                    currentOffset += chunkData.Length;
                }

                _columns[fieldName] = mergedArray;
                return mergedArray;
            }

            public void Dispose()
            {
                _columns.Clear();
                _parquetReader.DisposeAsync().GetAwaiter().GetResult();
                _fs.Dispose();
            }
        }
    }
}
