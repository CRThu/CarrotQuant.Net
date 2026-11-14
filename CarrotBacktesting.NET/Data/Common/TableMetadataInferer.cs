using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using Parquet;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 数据表元数据自描述推断工具（内部无状态纯函数）。
    /// 当数据目录缺失 metadata.json 时，通过流式嗅探 Parquet Footer 或 CSV 表头推断 Schema，用完即走，零实例状态。
    /// </summary>
    internal static class TableMetadataInferer
    {
        /// <summary>
        /// 为未显式配置 metadata.json 的数据目录流式嗅探物理文件元数据，并归一化构建强类型不可变实体。
        /// </summary>
        /// <param name="tableId">数据表唯一标识</param>
        /// <param name="tableDir">表物理存储绝对路径</param>
        /// <param name="metadataPath">可选的 metadata.json 预期路径</param>
        /// <param name="preLayout">可选显式预定义布局（若为 null 则自动由目录拓扑嗅探）</param>
        /// <param name="preFormat">可选显式预定义存储格式</param>
        /// <param name="prePartition">可选显式预定义分区模式</param>
        /// <param name="preCategory">可选显式预定义数据类别</param>
        /// <param name="preNullableFields">可选显式预定义允许空值白名单</param>
        /// <returns>推断构建出的全新不可变 TableMetadata 聚合实体</returns>
        public static TableMetadata Infer(string tableId, string tableDir, string? metadataPath, StorageLayout? preLayout = null, StorageFormat? preFormat = null, PartitionMode? prePartition = null, TableCategory? preCategory = null, IReadOnlySet<string>? preNullableFields = null)
        {
            // 1. 布局模式判定：存在 year=* 目录即为 Hive 分区，或由预定义指定
            StorageLayout layout = preLayout ?? (Directory.EnumerateDirectories(tableDir, "year=*").Any() ? StorageLayout.Hive : StorageLayout.Flat);

            // 2. 寻找首个样本物理文件
            bool isHiveDir = layout == StorageLayout.Hive || Directory.EnumerateDirectories(tableDir, "year=*").Any();
            Func<string, bool> matchesFormat = preFormat switch
            {
                StorageFormat.Parquet => f => f.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase),
                StorageFormat.Csv => f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase),
                _ => f => f.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            };

            string? sampleFile = isHiveDir
                ? Directory.EnumerateFiles(tableDir, "*.*", SearchOption.AllDirectories).FirstOrDefault(matchesFormat)
                : Directory.EnumerateFiles(tableDir, "*.*", SearchOption.TopDirectoryOnly).FirstOrDefault(matchesFormat);

            if (string.IsNullOrEmpty(sampleFile))
            {
                throw new FileNotFoundException($"No metadata.json or supported data files (*.parquet, *.csv) found in table directory: {tableDir}");
            }

            string ext = Path.GetExtension(sampleFile).TrimStart('.').ToLowerInvariant();
            StorageFormat format = preFormat ?? (ext switch
            {
                "parquet" => StorageFormat.Parquet,
                "csv" => StorageFormat.Csv,
                _ => throw new NotSupportedException($"Unsupported file extension '{ext}' in sample file: {sampleFile}")
            });

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sampleFile);

            PartitionMode partition = prePartition ?? (fileNameWithoutExt.Equals("data", StringComparison.OrdinalIgnoreCase)
                ? PartitionMode.None
                : PartitionMode.Symbol);

            var fieldNames = new List<string>();
            var fieldTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            if (format == StorageFormat.Parquet)
            {
                using var fs = File.OpenRead(sampleFile);
                var reader = ParquetReaderExtensions.OpenParquetReaderSync(fs);
                try
                {
                    foreach (var field in reader.Schema.GetDataFields())
                    {
                        fieldNames.Add(field.Name);
                        Type rawType = field.ClrType;
                        Type effectiveType = Nullable.GetUnderlyingType(rawType) ?? rawType;
                        if (effectiveType == typeof(ReadOnlyMemory<char>))
                        {
                            effectiveType = typeof(string);
                        }
                        fieldTypes[field.Name] = effectiveType;
                    }
                }
                finally
                {
                    reader.DisposeSync();
                }
            }
            else if (format == StorageFormat.Csv)
            {
                using var sr = new StreamReader(sampleFile);
                string? headerLine = sr.ReadLine();
                if (!string.IsNullOrWhiteSpace(headerLine))
                {
                    var cols = headerLine.Split(',').Select(c => c.Trim().Trim('"')).Where(c => !string.IsNullOrEmpty(c));
                    foreach (var col in cols)
                    {
                        fieldNames.Add(col);
                        string lower = col.ToLowerInvariant();
                        if (DefaultDataColumns.SymbolCandidates.Contains(lower)) fieldTypes[col] = typeof(string);
                        else if (lower == DefaultDataColumns.DateTime || lower == DefaultDataColumns.Date) fieldTypes[col] = typeof(DateTime);
                        else if (lower == DefaultDataColumns.Timestamp) fieldTypes[col] = typeof(long);
                        else fieldTypes[col] = typeof(double);
                    }
                }
            }

            TableCategory category = preCategory ?? TableCategory.Timeseries;
            IReadOnlySet<string> nullableFields = preNullableFields ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return new TableMetadata(tableId, tableDir, metadataPath, format, layout, fieldNames, fieldTypes, category, partition, nullableFields);
        }
    }
}
