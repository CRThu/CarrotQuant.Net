using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// Parquet 格式的纵向序列市场数据源实现（基于 Parquet.Net 列式读取与 YearCache 列缓存）。
    /// 继承自 <see cref="TimeseriesSourceBase"/>，仅关注底层列式文件的解压与内存块拷贝。
    /// </summary>
    public class ParquetTimeseriesSource : TimeseriesSourceBase
    {
        private readonly Dictionary<string, YearCache> _cachePool = new(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlySet<string> _nullableFields;

        /// <summary>
        /// 便捷构造函数。
        /// </summary>
        public ParquetTimeseriesSource(string storageRoot, string tableId, DateTime? startDate = null, DateTime? endDate = null, string? timeZone = null, IReadOnlySet<string>? nullableFields = null)
            : this(new StorageResolver(storageRoot), tableId, startDate, endDate, timeZone, nullableFields)
        {
        }

        /// <summary>
        /// 核心构造函数，接收外部依赖注入的路径解析器。
        /// </summary>
        public ParquetTimeseriesSource(IStorageResolver resolver, string tableId, DateTime? startDate = null, DateTime? endDate = null, string? timeZone = null, IReadOnlySet<string>? nullableFields = null)
            : base(resolver, tableId, startDate, endDate, timeZone)
        {
            var resolverFields = resolver.GetNullableFields(tableId);
            if (nullableFields == null || nullableFields.Count == 0)
            {
                _nullableFields = resolverFields;
            }
            else if (resolverFields == null || resolverFields.Count == 0)
            {
                _nullableFields = nullableFields;
            }
            else
            {
                var merged = new HashSet<string>(resolverFields, StringComparer.OrdinalIgnoreCase);
                foreach (var f in nullableFields) merged.Add(f);
                _nullableFields = merged;
            }
            var symbolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dateSet = new HashSet<DateTime>();

            // 使用 resolver 解析出的所有物理文件列表（已支持年份剪枝）
            var parquetFiles = _resolver.ResolvePhysicalFiles(_tableId, null, _startDate, _endDate);
            foreach (var file in parquetFiles)
            {
                using var fs = File.OpenRead(file);
                var reader = ParquetReaderExtensions.OpenParquetReaderSync(fs);
                try
                {
                    var symField = reader.Schema.FindDataFieldIgnoreCase(DefaultDataColumns.SymbolCandidates);
                    var dtField = reader.Schema.FindDataFieldIgnoreCase(DefaultDataColumns.TimeCandidates);

                    if (symField != null)
                    {
                        for (int i = 0; i < reader.RowGroupCount; i++)
                        {
                            using var groupReader = reader.OpenRowGroupReader(i);
                            var symCol = ReadColumnFromGroup(groupReader, symField, _nullableFields);
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
                            var dtCol = ReadColumnFromGroup(groupReader, dtField, _nullableFields);
                            var dates = ConvertToDateTimes(dtCol, _sourceTimeZone);
                            foreach (var d in dates)
                            {
                                dateSet.Add(d);
                            }
                        }
                    }
                }
                finally
                {
                    reader.DisposeSync();
                }
            }

            _symbols = symbolSet.OrderBy(s => s).ToList();
            _tradeDates = dateSet.OrderBy(d => d).ToList();
        }

        /// <inheritdoc />
        protected override int ReadRawSeriesFromFile<T>(
            string symbol,
            string fieldName,
            DateTime minDate,
            DateTime maxDate,
            ref DateTime[] rawDates,
            ref T[] rawValues)
        {
            var parquetFiles = _resolver.ResolvePhysicalFiles(_tableId, symbol, minDate, maxDate);
            int count = 0;

            foreach (var filePath in parquetFiles)
            {
                if (!_cachePool.TryGetValue(filePath, out var cache))
                {
                    if (File.Exists(filePath))
                    {
                        cache = new YearCache(filePath, _sourceTimeZone, _nullableFields);
                        _cachePool[filePath] = cache;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (!cache.StockRanges.TryGetValue(symbol, out var range))
                {
                    continue; // 无个股数据或停牌
                }

                Array colArray = cache.GetColumnData(fieldName);
                int endRow = range.StartRow + range.RowCount;

                for (int r = range.StartRow; r < endRow; r++)
                {
                    DateTime rowDate = cache.Dates[r];

                    EnsureCapacity(ref rawDates, ref rawValues, count);

                    rawDates[count] = rowDate;
                    rawValues[count] = ConvertArrayValue<T>(colArray, r);
                    count++;
                }
            }

            return count;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var cache in _cachePool.Values)
                {
                    cache.Dispose();
                }
                _cachePool.Clear();
            }
            base.Dispose(disposing);
        }

        #region Parquet Low-Level Helpers

        private static Array ReadColumnFromGroup(ParquetRowGroupReader groupReader, DataField dataField, IReadOnlySet<string>? nullableFields = null)
        {
            int rowCount = (int)groupReader.RowCount;
            Type readType = dataField.ClrType;
            Array buffer = Array.CreateInstance(readType, rowCount);
            bool isAllowedNull = nullableFields?.Contains(dataField.Name) ?? false;

            if (readType == typeof(ReadOnlyMemory<char>))
            {
                int[] defLevels = new int[rowCount];
                var memBuf = (ReadOnlyMemory<char>[])buffer;
                if (!dataField.IsNullable)
                {
                    groupReader.ReadRawSync(dataField, memBuf, defLevels);
                }
                else
                {
                    var packed = new ReadOnlyMemory<char>[rowCount];
                    groupReader.ReadRawSync(dataField, packed, defLevels);
                    int valIdx = 0;
                    int nullCount = 0;
                    List<int>? badIndices = null;
                    for (int i = 0; i < rowCount; i++)
                    {
                        if (defLevels[i] > 0)
                        {
                            memBuf[i] = packed[valIdx++];
                        }
                        else
                        {
                            nullCount++;
                            if (badIndices == null) badIndices = new List<int>(5);
                            if (badIndices.Count < 5) badIndices.Add(i);
                            memBuf[i] = ReadOnlyMemory<char>.Empty;
                        }
                    }

                    if (nullCount > 0 && !isAllowedNull)
                    {
                        string sampleStr = string.Join(", ", badIndices!);
                        string moreSuffix = nullCount > badIndices!.Count ? " ..." : "";
                        throw new InvalidDataException(
                            $"Parquet 列 '{dataField.Name}' 检测到缺失值 (NULL)：共 {nullCount}/{rowCount} 行无效 (占比 {(double)nullCount / rowCount:P2})。损坏样本行号: [{sampleStr}{moreSuffix}]。该字段未显式配置允许空值，严禁缺失。");
                    }
                }
            }
            else
            {
                int[] defLevels = new int[rowCount];
                if (readType == typeof(double)) ReadColumnRawTyped<double>(groupReader, dataField, (double[])buffer, defLevels, double.NaN, isAllowedNull);
                else if (readType == typeof(float)) ReadColumnRawTyped<float>(groupReader, dataField, (float[])buffer, defLevels, float.NaN, isAllowedNull);
                else if (readType == typeof(long)) ReadColumnRawTyped<long>(groupReader, dataField, (long[])buffer, defLevels, 0, isAllowedNull);
                else if (readType == typeof(int)) ReadColumnRawTyped<int>(groupReader, dataField, (int[])buffer, defLevels, 0, isAllowedNull);
                else if (readType == typeof(bool)) ReadColumnRawTyped<bool>(groupReader, dataField, (bool[])buffer, defLevels, false, isAllowedNull);
                else if (readType == typeof(DateTime))
                {
                    var dtBuf = (DateTime[])buffer;
                    if (!dataField.IsNullable)
                    {
                        groupReader.ReadRawSync(dataField, dtBuf, defLevels);
                    }
                    else
                    {
                        var packed = new DateTime[rowCount];
                        groupReader.ReadRawSync(dataField, packed, defLevels);
                        int valIdx = 0;
                        int nullCount = 0;
                        List<int>? badIndices = null;
                        for (int i = 0; i < rowCount; i++)
                        {
                            if (defLevels[i] > 0)
                            {
                                dtBuf[i] = packed[valIdx++];
                            }
                            else
                            {
                                nullCount++;
                                if (badIndices == null) badIndices = new List<int>(5);
                                if (badIndices.Count < 5) badIndices.Add(i);
                                dtBuf[i] = DateTime.MinValue;
                            }
                        }

                        if (nullCount > 0 && !isAllowedNull)
                        {
                            string sampleStr = string.Join(", ", badIndices!);
                            string moreSuffix = nullCount > badIndices!.Count ? " ..." : "";
                            throw new InvalidDataException(
                                $"Parquet 列 '{dataField.Name}' 检测到缺失值 (NULL)：共 {nullCount}/{rowCount} 行无效 (占比 {(double)nullCount / rowCount:P2})。损坏样本行号: [{sampleStr}{moreSuffix}]。该字段未显式配置允许空值，严禁缺失。");
                        }
                    }
                }
                else if (readType == typeof(DateTimeOffset))
                {
                    var dtoBuf = (DateTimeOffset[])buffer;
                    if (!dataField.IsNullable)
                    {
                        groupReader.ReadRawSync(dataField, dtoBuf, defLevels);
                    }
                    else
                    {
                        var packed = new DateTimeOffset[rowCount];
                        groupReader.ReadRawSync(dataField, packed, defLevels);
                        int valIdx = 0;
                        int nullCount = 0;
                        List<int>? badIndices = null;
                        for (int i = 0; i < rowCount; i++)
                        {
                            if (defLevels[i] > 0)
                            {
                                dtoBuf[i] = packed[valIdx++];
                            }
                            else
                            {
                                nullCount++;
                                if (badIndices == null) badIndices = new List<int>(5);
                                if (badIndices.Count < 5) badIndices.Add(i);
                                dtoBuf[i] = DateTimeOffset.MinValue;
                            }
                        }

                        if (nullCount > 0 && !isAllowedNull)
                        {
                            string sampleStr = string.Join(", ", badIndices!);
                            string moreSuffix = nullCount > badIndices!.Count ? " ..." : "";
                            throw new InvalidDataException(
                                $"Parquet 列 '{dataField.Name}' 检测到缺失值 (NULL)：共 {nullCount}/{rowCount} 行无效 (占比 {(double)nullCount / rowCount:P2})。损坏样本行号: [{sampleStr}{moreSuffix}]。该字段未显式配置允许空值，严禁缺失。");
                        }
                    }
                }
                else
                {
                    throw new NotSupportedException($"Type {readType} is not supported by the optimized Parquet loader.");
                }
            }

            return buffer;
        }

        private static void ReadColumnRawTyped<T>(
            ParquetRowGroupReader groupReader,
            DataField field,
            T[] buffer,
            int[] defLevels,
            T defaultValue,
            bool isAllowedNull)
            where T : unmanaged
        {
            if (!field.IsNullable)
            {
                groupReader.ReadRawSync(field, buffer, defLevels);
                return;
            }

            T[] packed = ArrayPool<T>.Shared.Rent(buffer.Length);
            try
            {
                groupReader.ReadRawSync(field, packed, defLevels);
                int valIdx = 0;
                int nullCount = 0;
                List<int>? badIndices = null;
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (defLevels[i] > 0)
                    {
                        buffer[i] = packed[valIdx++];
                    }
                    else
                    {
                        nullCount++;
                        if (badIndices == null) badIndices = new List<int>(5);
                        if (badIndices.Count < 5) badIndices.Add(i);
                        buffer[i] = defaultValue;
                    }
                }

                if (nullCount > 0 && !isAllowedNull)
                {
                    string sampleStr = string.Join(", ", badIndices!);
                    string moreSuffix = nullCount > badIndices!.Count ? " ..." : "";
                    throw new InvalidDataException(
                        $"Parquet 列 '{field.Name}' 检测到缺失值 (NULL)：共 {nullCount}/{buffer.Length} 行无效 (占比 {(double)nullCount / buffer.Length:P2})。损坏样本行号: [{sampleStr}{moreSuffix}]。该字段未显式配置允许空值，严禁缺失。");
                }
            }
            finally
            {
                ArrayPool<T>.Shared.Return(packed);
            }
        }

        private static List<DateTime> ConvertToDateTimes(object columnData, TimeZoneInfo? sourceTimeZone)
        {
            var dates = new List<DateTime>();
            if (columnData is long[] tsArr)
            {
                foreach (var ts in tsArr)
                {
                    dates.Add(sourceTimeZone != null
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime
                        : DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime.Date);
                }
            }
            else if (columnData is DateTime[] dtArr)
            {
                foreach (var dt in dtArr)
                {
                    dates.Add(NormalizeDateTime(dt, sourceTimeZone));
                }
            }
            else if (columnData is DateTimeOffset[] dtoArr)
            {
                foreach (var dto in dtoArr)
                {
                    dates.Add(sourceTimeZone != null
                        ? dto.UtcDateTime
                        : dto.DateTime.Date);
                }
            }
            else if (columnData is ReadOnlyMemory<char>[] romArr)
            {
                foreach (var rom in romArr)
                {
                    if (DateTime.TryParse(rom.Span, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt) ||
                        DateTime.TryParse(rom.Span, out dt))
                    {
                        dates.Add(NormalizeDateTime(dt, sourceTimeZone));
                    }
                }
            }
            return dates;
        }

        private static DateTime NormalizeDateTime(DateTime dt, TimeZoneInfo? sourceTimeZone)
        {
            if (sourceTimeZone != null)
            {
                if (dt.Kind == DateTimeKind.Unspecified)
                {
                    return TimeZoneInfo.ConvertTimeToUtc(dt, sourceTimeZone);
                }
                if (dt.Kind == DateTimeKind.Local)
                {
                    return dt.ToUniversalTime();
                }
                return dt;
            }
            return dt.Date;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T ConvertArrayValue<T>(Array arr, int index) where T : unmanaged
        {
            if (arr is T[] directArr)
            {
                return directArr[index];
            }

            if (arr is double[] dArr)
            {
                double d = dArr[index];
                if (typeof(T) == typeof(double)) return Unsafe.As<double, T>(ref d);
                if (typeof(T) == typeof(float)) { float f = (float)d; return Unsafe.As<float, T>(ref f); }
                if (typeof(T) == typeof(long)) { long l = (long)d; return Unsafe.As<long, T>(ref l); }
                if (typeof(T) == typeof(int)) { int i = (int)d; return Unsafe.As<int, T>(ref i); }
            }
            else if (arr is float[] fArr)
            {
                float f = fArr[index];
                if (typeof(T) == typeof(float)) return Unsafe.As<float, T>(ref f);
                if (typeof(T) == typeof(double)) { double d = f; return Unsafe.As<double, T>(ref d); }
                if (typeof(T) == typeof(long)) { long l = (long)f; return Unsafe.As<long, T>(ref l); }
                if (typeof(T) == typeof(int)) { int i = (int)f; return Unsafe.As<int, T>(ref i); }
            }
            else if (arr is long[] lArr)
            {
                long l = lArr[index];
                if (typeof(T) == typeof(long)) return Unsafe.As<long, T>(ref l);
                if (typeof(T) == typeof(double)) { double d = l; return Unsafe.As<double, T>(ref d); }
                if (typeof(T) == typeof(float)) { float f = l; return Unsafe.As<float, T>(ref f); }
                if (typeof(T) == typeof(int)) { int i = (int)l; return Unsafe.As<int, T>(ref i); }
            }
            else if (arr is int[] iArr)
            {
                int i = iArr[index];
                if (typeof(T) == typeof(int)) return Unsafe.As<int, T>(ref i);
                if (typeof(T) == typeof(double)) { double d = i; return Unsafe.As<double, T>(ref d); }
                if (typeof(T) == typeof(float)) { float f = i; return Unsafe.As<float, T>(ref f); }
                if (typeof(T) == typeof(long)) { long l = i; return Unsafe.As<long, T>(ref l); }
            }

            return default;
        }

        #endregion

        #region YearCache Nested Class

        private sealed class YearCache : IDisposable
        {
            public string FilePath { get; }
            public DateTime[] Dates { get; private set; } = Array.Empty<DateTime>();
            public Dictionary<string, (int StartRow, int RowCount)> StockRanges { get; } = new(StringComparer.OrdinalIgnoreCase);

            private readonly Dictionary<string, Array> _columns = new(StringComparer.OrdinalIgnoreCase);
            private readonly ParquetReader _parquetReader;
            private readonly FileStream _fs;
            private readonly DataField[] _dataFields;
            private readonly TimeZoneInfo? _sourceTimeZone;
            private readonly IReadOnlySet<string>? _nullableFields;

            public YearCache(string filePath, TimeZoneInfo? sourceTimeZone = null, IReadOnlySet<string>? nullableFields = null)
            {
                FilePath = filePath;
                _sourceTimeZone = sourceTimeZone;
                _nullableFields = nullableFields;
                _fs = File.OpenRead(filePath);
                _parquetReader = ParquetReaderExtensions.OpenParquetReaderSync(_fs);
                _dataFields = _parquetReader.Schema.GetDataFields();

                BuildIndex();
            }

            private void BuildIndex()
            {
                var symField = _parquetReader.Schema.FindDataFieldIgnoreCase(DefaultDataColumns.SymbolCandidates)
                    ?? throw new InvalidOperationException($"Required symbol column not found in {FilePath}");

                var dtField = _parquetReader.Schema.FindDataFieldIgnoreCase(DefaultDataColumns.TimeCandidates)
                    ?? throw new InvalidOperationException($"Required datetime column not found in {FilePath}");

                var symList = new List<string>();
                var dateList = new List<DateTime>();

                for (int i = 0; i < _parquetReader.RowGroupCount; i++)
                {
                    using var groupReader = _parquetReader.OpenRowGroupReader(i);
                    var symData = ReadColumnFromGroup(groupReader, symField, _nullableFields);
                    if (symData is ReadOnlyMemory<char>[] romArr)
                    {
                        foreach (var m in romArr)
                        {
                            symList.Add(m.ToString());
                        }
                    }

                    var dtData = ReadColumnFromGroup(groupReader, dtField, _nullableFields);
                    dateList.AddRange(ConvertToDateTimes(dtData, _sourceTimeZone));
                }

                Dates = dateList.ToArray();

                StockRanges.Clear();
                if (symList.Count > 0)
                {
                    string currentSym = symList[0];
                    int startIdx = 0;
                    for (int i = 1; i < symList.Count; i++)
                    {
                        if (!symList[i].Equals(currentSym, StringComparison.OrdinalIgnoreCase))
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
                    Array chunkData = ReadColumnFromGroup(groupReader, targetField, _nullableFields);

                    Array.Copy(chunkData, 0, mergedArray, currentOffset, chunkData.Length);
                    currentOffset += chunkData.Length;
                }

                _columns[fieldName] = mergedArray;
                return mergedArray;
            }

            public void Dispose()
            {
                _columns.Clear();
                _parquetReader.DisposeSync();
                _fs.Dispose();
            }
        }

        #endregion
    }
}
