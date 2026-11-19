using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        /// 核心构造函数，接收不可变表元数据与统一调度器。
        /// </summary>
        public CsvTimeseriesSource(TableMetadata metadata, IStorageResolver resolver, MarketTime? startDate = null, MarketTime? endDate = null, string? timeZone = null)
            : base(metadata, resolver, startDate, endDate, timeZone)
        {
            if (_metadata.Partition != PartitionMode.Symbol)
            {
                throw new NotSupportedException(
                    $"时序 CSV 数据表 '{_tableId}' 不支持 partition: '{_metadata.Partition}'。" +
                    $"时序 CSV 必须按标的独立拆分存储 (partition: 'symbol'，物理命名为 {{symbol}}.csv)。" +
                    $"全市场单文件聚合存储请使用 Parquet 格式 (format: 'parquet', partition: 'none')。");
            }

            var symbolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dateSet = new HashSet<MarketTime>();
            var csvFiles = _resolver.ResolvePhysicalFiles(_metadata, null, _startDate, _endDate);

            foreach (var file in csvFiles)
            {
                if (!File.Exists(file)) continue;

                string sym = Path.GetFileNameWithoutExtension(file);
                symbolSet.Add(sym);

                using var reader = CsvDataReader.Create(file);
                int dateColIdx = reader.FindColumn(DefaultDataColumns.TimeCandidates);

                if (dateColIdx == -1)
                {
                    throw new InvalidOperationException($"Required datetime column not found in {file}");
                }

                while (reader.Read())
                {
                    MarketTime dt = reader.ParseTime(dateColIdx, _sourceTimeZone);
                    if (dt != MarketTime.MinValue)
                    {
                        dateSet.Add(dt);
                    }
                }
            }

            _symbols = symbolSet.OrderBy(s => s).ToList();
            _tradeDates = dateSet.OrderBy(d => d).ToList();
        }

        /// <inheritdoc />
        protected override int ReadRawSeriesFromFile<T>(
            string symbol,
            string fieldName,
            MarketTime minDate,
            MarketTime maxDate,
            ref MarketTime[] rawDates,
            ref T[] rawValues)
        {
            var csvFiles = _resolver.ResolvePhysicalFiles(_metadata, symbol, minDate, maxDate);
            int count = 0;

            foreach (var filePath in csvFiles)
            {
                if (!File.Exists(filePath)) continue;

                using var reader = CsvDataReader.Create(filePath);

                int dateColIdx = reader.FindColumn(DefaultDataColumns.TimeCandidates);
                if (dateColIdx == -1) continue;

                int fieldColIdx = reader.FindColumn(fieldName);
                if (fieldColIdx == -1) continue;

                while (reader.Read())
                {
                    MarketTime rowDate = reader.ParseTime(dateColIdx, _sourceTimeZone);
                    if (rowDate == MarketTime.MinValue) continue;

                    EnsureCapacity(ref rawDates, ref rawValues, count);

                    rawDates[count] = rowDate;
                    rawValues[count] = reader.ReadValue<T>(fieldColIdx);
                    count++;
                }
            }

            return count;
        }
    }
}
