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
        /// 是否检测到物理数据按时间截面交错或单文件多标的混杂存放。
        /// 该模式下按标的抽取会多次扫描文本，性能受限。
        /// </summary>
        public bool IsInterleaved { get; private set; }

        /// <summary>
        /// 核心构造函数，接收不可变表元数据与统一调度器。
        /// </summary>
        public CsvTimeseriesSource(TableMetadata metadata, IStorageResolver resolver, DateTime? startDate = null, DateTime? endDate = null, string? timeZone = null)
            : base(metadata, resolver, startDate, endDate, timeZone)
        {
            var symbolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dateSet = new HashSet<DateTime>();
            var csvFiles = _resolver.ResolvePhysicalFiles(_metadata, null, _startDate, _endDate);
            bool hasInternalSymbols = false;

            foreach (var file in csvFiles)
            {
                if (!File.Exists(file)) continue;

                using var reader = CsvDataReader.Create(file);
                int symColIdx = reader.FindColumn(DefaultDataColumns.SymbolCandidates);
                int dateColIdx = reader.FindColumn(DefaultDataColumns.TimeCandidates);

                if (symColIdx >= 0)
                {
                    hasInternalSymbols = true;
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

                        if (dateColIdx >= 0)
                        {
                            DateTime dt = reader.ParseDate(dateColIdx, _sourceTimeZone);
                            if (dt != DateTime.MinValue)
                            {
                                dateSet.Add(dt);
                            }
                        }
                    }
                }
                else
                {
                    // 内部无 symbol 列：权威从文件名提取标的代码（如 sh.600000.csv -> sh.600000）
                    string sym = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrWhiteSpace(sym))
                    {
                        symbolSet.Add(sym);
                    }

                    if (dateColIdx >= 0)
                    {
                        while (reader.Read())
                        {
                            DateTime dt = reader.ParseDate(dateColIdx, _sourceTimeZone);
                            if (dt != DateTime.MinValue)
                            {
                                dateSet.Add(dt);
                            }
                        }
                    }
                }
            }

            _symbols = symbolSet.OrderBy(s => s).ToList();
            _tradeDates = dateSet.OrderBy(d => d).ToList();

            if (hasInternalSymbols && _symbols.Count > 1)
            {
                IsInterleaved = true;
                Console.WriteLine($"[WARN] CSV 时序数据表 '{_tableId}' 检测到单文件内混存多标的 (共 {_symbols.Count} 标的)。按标的抽取将反复全量扫描文本，存在 O(N*Rows) 性能开销。强烈建议上游拆分为 {{symbol}}.csv 或转换为 Parquet 格式。");
            }
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

                // 仅当明确检测到单文件多标的混杂交错时，才开启内部行级标的过滤；
                // 正常分标的文件 (IsInterleaved == false) 彻底跳过比对，零字符串分配与判定开销
                bool needSymbolFilter = IsInterleaved && symColIdx >= 0;

                while (reader.Read())
                {
                    if (needSymbolFilter)
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
