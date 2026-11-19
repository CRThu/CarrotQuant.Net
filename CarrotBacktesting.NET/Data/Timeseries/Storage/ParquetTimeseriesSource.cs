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
        /// 是否检测到物理数据按时间截面交错存放（存在非连续标的交替）。
        /// </summary>
        public bool IsInterleaved => _cachePool.Values.Any(c => c.IsInterleaved);

        /// <summary>
        /// 核心构造函数，接收不可变表元数据与统一调度器。
        /// </summary>
        public ParquetTimeseriesSource(TableMetadata metadata, IStorageResolver resolver, DateTime? startDate = null, DateTime? endDate = null, string? timeZone = null, IReadOnlySet<string>? nullableFields = null)
            : base(metadata, resolver, startDate, endDate, timeZone)
        {
            var resolverFields = metadata.NullableFields;
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

            try
            {
                // 使用直通重载解析出的所有物理文件列表（已支持年份剪枝）
                var parquetFiles = _resolver.ResolvePhysicalFiles(_metadata, null, _startDate, _endDate);
                foreach (var file in parquetFiles)
                {
                    if (!File.Exists(file)) continue;

                    var cache = new YearCache(file, _sourceTimeZone, _nullableFields);
                    _cachePool[file] = cache;

                    foreach (var sym in cache.StockRanges.Keys)
                    {
                        symbolSet.Add(sym);
                    }
                    foreach (var dt in cache.Dates)
                    {
                        dateSet.Add(dt);
                    }
                }
            }
            catch
            {
                foreach (var cache in _cachePool.Values)
                {
                    cache.Dispose();
                }
                _cachePool.Clear();
                throw;
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
            var parquetFiles = _resolver.ResolvePhysicalFiles(_metadata, symbol, minDate, maxDate);
            int count = 0;

            foreach (var filePath in parquetFiles)
            {
                if (!_cachePool.TryGetValue(filePath, out var cache))
                {
                    continue;
                }

                if (!cache.StockRanges.TryGetValue(symbol, out var ranges))
                {
                    continue; // 无个股数据或停牌
                }

                Array colArray = cache.GetColumnData(fieldName);
                for (int i = 0; i < ranges.Count; i++)
                {
                    var range = ranges[i];
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
                    DateTime dt = sourceTimeZone != null
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime
                        : DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime;
                    dates.Add(dt);
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
                    DateTime dt = sourceTimeZone != null
                        ? dto.UtcDateTime
                        : dto.DateTime;
                    dates.Add(dt);
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
            return dt;
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
            public Dictionary<string, List<(int StartRow, int RowCount)>> StockRanges { get; } = new(StringComparer.OrdinalIgnoreCase);
            public bool IsInterleaved { get; private set; }

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
                try
                {
                    _parquetReader = ParquetReaderExtensions.OpenParquetReaderSync(_fs);
                    _dataFields = _parquetReader.Schema.GetDataFields();
                    BuildIndex();
                }
                catch
                {
                    _parquetReader?.DisposeSync();
                    _fs.Dispose();
                    throw;
                }
            }

            private void BuildIndex()
            {
                var symField = _parquetReader.Schema.FindDataFieldIgnoreCase(DefaultDataColumns.SymbolCandidates)
                    ?? throw new InvalidOperationException($"Required symbol column not found in {FilePath}");

                var dtField = _parquetReader.Schema.FindDataFieldIgnoreCase(DefaultDataColumns.TimeCandidates)
                    ?? throw new InvalidOperationException($"Required datetime column not found in {FilePath}");

                StockRanges.Clear();
                var dateList = new List<DateTime>();
                var symPool = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var symLookup = symPool.GetAlternateLookup<ReadOnlySpan<char>>();

                string? currentSym = null;
                ReadOnlyMemory<char> currentSymMem = ReadOnlyMemory<char>.Empty;
                int currentBlockStart = 0;
                int currentRow = 0;
                bool isInterleaved = false;

                for (int i = 0; i < _parquetReader.RowGroupCount; i++)
                {
                    using var groupReader = _parquetReader.OpenRowGroupReader(i);
                    var symData = ReadColumnFromGroup(groupReader, symField, _nullableFields);
                    if (symData is ReadOnlyMemory<char>[] romArr)
                    {
                        for (int r = 0; r < romArr.Length; r++)
                        {
                            var m = romArr[r];
                            if (currentSym != null && m.Span.SequenceEqual(currentSymMem.Span))
                            {
                                // 连续相同标的，延续当前块计数
                            }
                            else
                            {
                                if (currentSym != null)
                                {
                                    AddStockRange(currentSym, currentBlockStart, currentRow - currentBlockStart);
                                }

                                if (!isInterleaved && symLookup.Contains(m.Span))
                                {
                                    isInterleaved = true;
                                    Console.WriteLine($"[WARN] 时序数据表 '{FilePath}' 检测到数据存在非连续交错存储 (如标的 '{m}')。时序读取为非连续跨步访问，加载性能会受到硬件 Cache Miss 限制。建议上游 ETL 按 (symbol, datetime) 聚簇排序，或开启本地二进制分块缓存 (.cache)。");
                                }

                                if (!symLookup.TryGetValue(m.Span, out currentSym))
                                {
                                    currentSym = m.ToString();
                                    symLookup.Add(currentSym);
                                }
                                currentSymMem = m;
                                currentBlockStart = currentRow;
                            }
                            currentRow++;
                        }
                    }

                    var dtData = ReadColumnFromGroup(groupReader, dtField, _nullableFields);
                    dateList.AddRange(ConvertToDateTimes(dtData, _sourceTimeZone));
                }

                if (currentSym != null && currentRow > currentBlockStart)
                {
                    AddStockRange(currentSym, currentBlockStart, currentRow - currentBlockStart);
                }

                Dates = dateList.ToArray();
                IsInterleaved = isInterleaved;

                void AddStockRange(string sym, int start, int count)
                {
                    if (!StockRanges.TryGetValue(sym, out var list))
                    {
                        list = new List<(int StartRow, int RowCount)>(1);
                        StockRanges[sym] = list;
                    }
                    list.Add((start, count));
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
