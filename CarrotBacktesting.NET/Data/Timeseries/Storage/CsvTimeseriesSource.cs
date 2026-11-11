using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;
using Sylvan.Data.Csv;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// CSV 格式的纵向序列市场数据源实现（基于 Sylvan.Data.Csv 流式解析）。
    /// 继承自 <see cref="TimeseriesSourceBase"/>，仅关注底层 CSV 文本行的高性能无装箱反序列化。
    /// </summary>
    public class CsvTimeseriesSource : TimeseriesSourceBase
    {
        /// <summary>
        /// 便捷构造函数。
        /// </summary>
        public CsvTimeseriesSource(string storageRoot, IFieldRegistry registry, string tableId, DateTime? startDate = null, DateTime? endDate = null)
            : this(new StorageResolver(storageRoot), registry, tableId, startDate, endDate)
        {
        }

        /// <summary>
        /// 核心构造函数，接收外部依赖注入的路径解析器。
        /// </summary>
        public CsvTimeseriesSource(IStorageResolver resolver, IFieldRegistry registry, string tableId, DateTime? startDate = null, DateTime? endDate = null)
            : base(resolver, registry, tableId, startDate, endDate)
        {
            // 1. 扫描可用股票代码，委托给路径调度器解析符合时间区间的物理文件
            var symbolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var csvFiles = _resolver.ResolvePhysicalFiles(_tableId, null, _startDate, _endDate);
            foreach (var file in csvFiles)
            {
                string sym = Path.GetFileNameWithoutExtension(file);
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

        /// <inheritdoc />
        protected override int ReadRawSeriesFromFile<T>(
            string symbol,
            string fieldName,
            DateTime minDate,
            DateTime maxDate,
            ref DateTime[] rawDates,
            ref T[] rawValues)
        {
            var csvFiles = _resolver.ResolvePhysicalFiles(_tableId, symbol, minDate, maxDate);
            int count = 0;

            foreach (var filePath in csvFiles)
            {
                if (!File.Exists(filePath)) continue;

                using var reader = CsvDataReader.Create(filePath);

                int dateColIdx = -1;
                try { dateColIdx = reader.GetOrdinal("datetime"); } catch { }
                if (dateColIdx == -1)
                {
                    try { dateColIdx = reader.GetOrdinal("timestamp"); } catch { }
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

                    EnsureCapacity(ref rawDates, ref rawValues, count);

                    rawDates[count] = rowDate;
                    rawValues[count] = ValueConverter<T>.Read(reader, fieldColIdx);
                    count++;
                }
            }

            return count;
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
    }
}
