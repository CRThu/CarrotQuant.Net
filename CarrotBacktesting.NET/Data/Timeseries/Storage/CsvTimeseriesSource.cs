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
        public CsvTimeseriesSource(TableMetadata metadata, IStorageResolver resolver, DateTime? startDate = null, DateTime? endDate = null, string? timeZone = null)
            : base(metadata, resolver, startDate, endDate, timeZone)
        {
            // 1. 扫描可用股票代码，直通委托给调度器解析符合时间区间的物理文件
            var symbolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var csvFiles = _resolver.ResolvePhysicalFiles(_metadata, null, _startDate, _endDate);
            foreach (var file in csvFiles)
            {
                string sym = Path.GetFileNameWithoutExtension(file);
                if (!sym.Equals("data", StringComparison.OrdinalIgnoreCase))
                {
                    symbolSet.Add(sym);
                }
            }

            // 若文件名均为 data（即 partition: "none" 单文件多标的存储），则通过流式读取 symbol 列提取标的并集
            if (symbolSet.Count == 0 && csvFiles.Count > 0)
            {
                foreach (var file in csvFiles)
                {
                    if (!File.Exists(file)) continue;
                    using var reader = CsvDataReader.Create(file);
                    int symColIdx = reader.FindColumn(DefaultDataColumns.SymbolCandidates);
                    if (symColIdx >= 0)
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(symColIdx))
                            {
                                string sym = reader.GetString(symColIdx);
                                if (!string.IsNullOrWhiteSpace(sym))
                                {
                                    symbolSet.Add(sym);
                                }
                            }
                        }
                    }
                }
            }

            _symbols = symbolSet.OrderBy(s => s).ToList();

            // 2. 扫描所有交易日，取并集并排序（利用 Sylvan 高速解析）
            var dateSet = new HashSet<DateTime>();
            foreach (var file in csvFiles)
            {
                using var reader = CsvDataReader.Create(file);
                int dateColIdx = reader.FindColumn(DefaultDataColumns.TimeCandidates);
                if (dateColIdx == -1) continue;

                while (reader.Read())
                {
                    DateTime dt = reader.ParseDate(dateColIdx, _sourceTimeZone);
                    if (dt != DateTime.MinValue)
                    {
                        dateSet.Add(dt);
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
            var csvFiles = _resolver.ResolvePhysicalFiles(_metadata, symbol, minDate, maxDate);
            int count = 0;

            foreach (var filePath in csvFiles)
            {
                if (!File.Exists(filePath)) continue;

                using var reader = CsvDataReader.Create(filePath);

                int symColIdx = reader.FindColumn(DefaultDataColumns.SymbolCandidates);
                int dateColIdx = reader.FindColumn(DefaultDataColumns.TimeCandidates);
                if (dateColIdx == -1) continue;

                int fieldColIdx = reader.FindColumn(fieldName);
                if (fieldColIdx == -1) continue;

                while (reader.Read())
                {
                    if (symColIdx >= 0)
                    {
                        string rowSym = reader.GetString(symColIdx);
                        if (!rowSym.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    DateTime rowDate = reader.ParseDate(dateColIdx, _sourceTimeZone);
                    if (rowDate == DateTime.MinValue) continue;

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
