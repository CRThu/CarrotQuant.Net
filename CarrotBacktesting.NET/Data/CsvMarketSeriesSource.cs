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
    /// CSV 格式的纵向序列市场数据源实现（实现 IMarketSeriesSource 契约）。
    /// 基于 Sylvan.Data.Csv 库进行高效的 CSV 文件列解析。
    /// 物理路径与元数据强类型全部委托给 IMarketDataResolver。
    /// </summary>
    public class CsvMarketSeriesSource : IMarketSeriesSource, IDisposable
    {
        private readonly IMarketDataResolver _resolver;
        private readonly List<DateTime> _tradeDates;
        private readonly List<string> _symbols;

        public IReadOnlyList<string> Symbols => _symbols;
        public IReadOnlyList<string> FieldNames => _resolver.FieldNames;
        public IReadOnlyList<DateTime> TradeDates => _tradeDates;

        /// <summary>
        /// 便捷构造函数。
        /// </summary>
        public CsvMarketSeriesSource(string storageRoot, string tableId)
            : this(new MarketDataResolver(storageRoot, tableId))
        {
        }

        /// <summary>
        /// 核心构造函数，接收外部依赖注入的路径解析器。
        /// </summary>
        public CsvMarketSeriesSource(IMarketDataResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

            // 1. 扫描可用股票代码
            var symbolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var csvFiles = _resolver.EnumerateCsvFiles();
            foreach (var file in csvFiles)
            {
                string sym = Path.GetFileNameWithoutExtension(file);
                symbolSet.Add(sym);
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
            return _resolver.GetFieldType(fieldName);
        }

        /// <summary>
        /// 批量读取特定股票的指标序列。
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

            // 初始化填充默认值
            destination.Fill(default);

            DateTime minDate = _tradeDates[startIndex];
            DateTime maxDate = _tradeDates[startIndex + length - 1];

            // 遍历所有可用年份的分区物理文件
            foreach (var year in _resolver.GetAvailableYears())
            {
                string filePath = _resolver.GetCsvFilePath(year, symbol);
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

                    if (rowDate > maxDate) break;
                    if (rowDate < minDate) continue;

                    int globalIdx = _tradeDates.BinarySearch(startIndex, length, rowDate, Comparer<DateTime>.Default);
                    if (globalIdx >= 0)
                    {
                        int offset = globalIdx - startIndex;
                        destination[offset] = GetSylvanValue<T>(reader, fieldColIdx);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T GetSylvanValue<T>(CsvDataReader reader, int colIdx) where T : unmanaged
        {
            if (reader.IsDBNull(colIdx))
            {
                return default;
            }

            if (typeof(T) == typeof(double))
            {
                double val = reader.GetDouble(colIdx);
                return Unsafe.As<double, T>(ref val);
            }
            if (typeof(T) == typeof(float))
            {
                float val = reader.GetFloat(colIdx);
                return Unsafe.As<float, T>(ref val);
            }
            if (typeof(T) == typeof(long))
            {
                long val = reader.GetInt64(colIdx);
                return Unsafe.As<long, T>(ref val);
            }
            if (typeof(T) == typeof(int))
            {
                int val = reader.GetInt32(colIdx);
                return Unsafe.As<int, T>(ref val);
            }
            if (typeof(T) == typeof(bool))
            {
                bool val = reader.GetBoolean(colIdx);
                return Unsafe.As<bool, T>(ref val);
            }
            if (typeof(T) == typeof(byte))
            {
                byte val = reader.GetByte(colIdx);
                return Unsafe.As<byte, T>(ref val);
            }

            string? valStr = reader.GetString(colIdx);
            return ParseValueFallback<T>(valStr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T ParseValueFallback<T>(string? valStr) where T : unmanaged
        {
            if (string.IsNullOrWhiteSpace(valStr))
            {
                return default;
            }
            try
            {
                object converted = Convert.ChangeType(valStr, typeof(T));
                return (T)converted;
            }
            catch
            {
                return default;
            }
        }

        public void Dispose()
        {
        }
    }
}
