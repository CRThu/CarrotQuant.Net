using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;
using Sylvan.Data.Csv;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// CSV 格式的纵向序列市场数据源实现（实现 IMarketSeriesSource 契约）。
    /// 基于 Sylvan.Data.Csv 库进行高效的 CSV 文件列解析。
    /// 物理路径与元数据强类型全部委托给 IStorageResolver。
    /// </summary>
    public class CsvMarketSeriesSource : IMarketSeriesSource, IDisposable
    {
        private readonly IStorageResolver _resolver;
        private readonly string _tableId;
        private readonly DateTime? _startDate;
        private readonly DateTime? _endDate;
        private readonly List<DateTime> _tradeDates;
        private readonly List<string> _symbols;

        public IReadOnlyList<string> Symbols => _symbols;
        public IReadOnlyList<string> FieldNames => _resolver.GetFieldNames(_tableId);
        public IReadOnlyList<DateTime> TradeDates => _tradeDates;

        /// <summary>
        /// 便捷构造函数。
        /// </summary>
        public CsvMarketSeriesSource(string storageRoot, IFieldRegistry registry, string tableId, DateTime? startDate = null, DateTime? endDate = null)
            : this(new StorageResolver(storageRoot), registry, tableId, startDate, endDate)
        {
        }

        /// <summary>
        /// 核心构造函数，接收外部依赖注入的路径解析器。
        /// </summary>
        public CsvMarketSeriesSource(IStorageResolver resolver, IFieldRegistry registry, string tableId, DateTime? startDate = null, DateTime? endDate = null)
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

            // 1. 扫描可用股票代码，委托给路径调度器解析符合时间区间的物理文件
            var symbolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var csvFiles = _resolver.ResolvePhysicalFiles(_tableId, null, _startDate, _endDate);
            foreach (var file in csvFiles)
            {
                string sym = Path.GetFileNameWithoutExtension(file);
                // 排除可能生成的非个股公共数据 CSV
                if (!sym.Equals("data", StringComparison.OrdinalIgnoreCase))
                {
                    symbolSet.Add(sym);
                }
            }
            _symbols = symbolSet.OrderBy(s => s).ToList();

            // 2. 扫描所有交易日，取并集并排序（利用 Sylvan 高速解析）
            var dateSet = new HashSet<DateTime>();
            foreach (var file in csvFiles)
            {
                using var reader = CsvDataReader.Create(file);
                int dateColIdx = -1;
                try { dateColIdx = reader.GetOrdinal("datetime"); } catch {}
                if (dateColIdx == -1)
                {
                    try { dateColIdx = reader.GetOrdinal("timestamp"); } catch {}
                }
                if (dateColIdx == -1) continue;

                while (reader.Read())
                {
                    if (reader.GetFieldType(dateColIdx) == typeof(DateTime))
                    {
                        dateSet.Add(reader.GetDateTime(dateColIdx).Date);
                    }
                    else
                    {
                        string dtStr = reader.GetString(dateColIdx);
                        if (long.TryParse(dtStr, out long ts))
                        {
                            dateSet.Add(DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime.Date);
                        }
                        else if (DateTime.TryParse(dtStr, out var dt))
                        {
                            dateSet.Add(dt.Date);
                        }
                    }
                }
            }

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
        /// 批量读取特定股票的指标序列。
        /// 仅解析目标窗口 [minDate, maxDate] 内的物理文件，零跨文件向左回溯，缺失单元格由对齐器天然置 NaN。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            // 获取指定时间区间内的物理数据文件列表（精确剪枝，杜绝向左全量历史检索放大）
            var csvFiles = _resolver.ResolvePhysicalFiles(_tableId, symbol, minDate, maxDate);
            
            // 依据目标窗口长度自适应租借池化缓冲区，杜绝硬编码与高频/日线维度的内存重叠开销
            int capacity = Math.Max(length, 16);
            DateTime[] rawDates = ArrayPool<DateTime>.Shared.Rent(capacity);
            T[] rawValues = ArrayPool<T>.Shared.Rent(capacity);
            int count = 0;

            try
            {
                foreach (var filePath in csvFiles)
                {
                    if (!File.Exists(filePath))
                    {
                        continue;
                    }

                    using var reader = CsvDataReader.Create(filePath);
                    
                    int dateColIdx = -1;
                    try { dateColIdx = reader.GetOrdinal("datetime"); } catch {}
                    if (dateColIdx == -1)
                    {
                        try { dateColIdx = reader.GetOrdinal("timestamp"); } catch {}
                    }
                    
                    int fieldColIdx = -1;
                    try
                    {
                        fieldColIdx = reader.GetOrdinal(fieldName);
                    }
                    catch
                    {
                        continue;
                    }

                    if (dateColIdx == -1) continue;

                    while (reader.Read())
                    {
                        DateTime rowDate = DateTime.MinValue;
                        if (reader.GetFieldType(dateColIdx) == typeof(DateTime))
                        {
                            rowDate = reader.GetDateTime(dateColIdx).Date;
                        }
                        else
                        {
                            string dtStr = reader.GetString(dateColIdx);
                            if (long.TryParse(dtStr, out long ts))
                            {
                                rowDate = DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime.Date;
                            }
                            else if (DateTime.TryParse(dtStr, out var dt))
                            {
                                rowDate = dt.Date;
                            }
                        }

                        if (rowDate == DateTime.MinValue) continue;

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
                        rawValues[count] = ValueConverter<T>.Read(reader, fieldColIdx);
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

        private static class ValueConverter<T> where T : unmanaged
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static T Read(CsvDataReader reader, int colIdx)
            {
                if (reader.IsDBNull(colIdx)) return default;

                if (typeof(T) == typeof(double)) { double val = reader.GetDouble(colIdx); return Unsafe.As<double, T>(ref val); }
                if (typeof(T) == typeof(float)) { float val = reader.GetFloat(colIdx); return Unsafe.As<float, T>(ref val); }
                if (typeof(T) == typeof(long)) { long val = reader.GetInt64(colIdx); return Unsafe.As<long, T>(ref val); }
                if (typeof(T) == typeof(int)) { int val = reader.GetInt32(colIdx); return Unsafe.As<int, T>(ref val); }
                if (typeof(T) == typeof(bool)) { bool val = reader.GetBoolean(colIdx); return Unsafe.As<bool, T>(ref val); }
                if (typeof(T) == typeof(byte)) { byte val = reader.GetByte(colIdx); return Unsafe.As<byte, T>(ref val); }

                return ParseValueFallback<T>(reader.GetString(colIdx));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T ParseValueFallback<T>(string? valStr) where T : unmanaged
        {
            if (string.IsNullOrWhiteSpace(valStr)) return default;
            try
            {
                object converted = Convert.ChangeType(valStr, typeof(T));
                return (T)converted;
            }
            catch { return default; }
        }

        public void Dispose()
        {
        }
    }
}
