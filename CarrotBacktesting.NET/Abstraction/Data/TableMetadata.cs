using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 数据表不可变元数据聚合模型（单一事实来源 SSOT）。
    /// 统筹 Schema、物理格式、存储拓扑与空值约束契约。
    /// </summary>
    public sealed class TableMetadata
    {
        /// <summary>
        /// 数据表 ID。
        /// </summary>
        public string TableId { get; }

        /// <summary>
        /// 数据表所在物理目录。
        /// </summary>
        public string TableDir { get; }

        /// <summary>
        /// 元数据 metadata.json 物理路径（若存在）。
        /// </summary>
        public string? MetadataPath { get; }

        /// <summary>
        /// 物理存储格式枚举（Parquet 或 Csv）。
        /// </summary>
        public StorageFormat Format { get; }

        /// <summary>
        /// 物理存储布局枚举（Hive 分区或 Flat 平铺）。
        /// </summary>
        public StorageLayout Layout { get; }

        /// <summary>
        /// 字段名称只读列表。
        /// </summary>
        public IReadOnlyList<string> FieldNames { get; }

        /// <summary>
        /// 字段强类型映射字典（不区分大小写）。
        /// </summary>
        public IReadOnlyDictionary<string, Type> FieldTypes { get; }

        /// <summary>
        /// 数据分类枚举（Timeseries 或 Event）。
        /// </summary>
        public TableCategory Category { get; }

        /// <summary>
        /// 分区模式枚举（None 或 Symbol）。
        /// </summary>
        public PartitionMode Partition { get; }

        /// <summary>
        /// 允许为空值的字段白名单集合（不区分大小写）。
        /// </summary>
        public IReadOnlySet<string> NullableFields { get; }

        /// <summary>
        /// 格式文件扩展名（小写，不带点，如 "parquet"、"csv"）。
        /// </summary>
        public string FileExtension => Format == StorageFormat.Parquet ? "parquet" : "csv";

        /// <summary>
        /// 构造不可变元数据聚合模型。
        /// 作为全生命周期的单一事实来源（SSOT），所有集合与映射在构建期完成浅不可变固化，运行期禁止二次修改。
        /// </summary>
        public TableMetadata(
            string tableId,
            string tableDir,
            string? metadataPath,
            StorageFormat format,
            StorageLayout layout,
            IReadOnlyList<string> fieldNames,
            IReadOnlyDictionary<string, Type> fieldTypes,
            TableCategory category,
            PartitionMode partition,
            IReadOnlySet<string> nullableFields)
        {
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            TableDir = tableDir ?? throw new ArgumentNullException(nameof(tableDir));
            MetadataPath = metadataPath;
            Format = format;
            Layout = layout;
            FieldNames = fieldNames ?? throw new ArgumentNullException(nameof(fieldNames));
            FieldTypes = fieldTypes ?? throw new ArgumentNullException(nameof(fieldTypes));
            Category = category;
            Partition = partition;
            NullableFields = nullableFields ?? throw new ArgumentNullException(nameof(nullableFields));
        }

        /// <summary>
        /// 基于字段名获取对应的 C# 物理强类型。
        /// 采用防御性兜底：未在 Schema 中显式定义的未知字段安全降级为 string 类型，防止动态反射解析崩溃。
        /// </summary>
        public Type GetFieldType(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return typeof(string);
            return FieldTypes.TryGetValue(fieldName, out var type) ? type : typeof(string);
        }
    }
}
