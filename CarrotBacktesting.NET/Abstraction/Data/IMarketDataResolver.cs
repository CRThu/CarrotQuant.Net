using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 市场数据物理路径与元数据解析契约，统一管理数据表的物理存储布局与结构类型定义。
    /// </summary>
    public interface IMarketDataResolver
    {
        /// <summary>
        /// 存储根目录 (例如 "storage_root/csv" 或 "storage_root/parquet")
        /// </summary>
        string StorageRoot { get; }

        /// <summary>
        /// 表 ID (例如 "ashare.kline.1d.raw.baostock")
        /// </summary>
        string TableId { get; }

        /// <summary>
        /// 表对应的物理文件夹完整路径
        /// </summary>
        string TableDir { get; }

        /// <summary>
        /// 物理 metadata.json 文件的完整路径
        /// </summary>
        string MetadataPath { get; }
        
        /// <summary>
        /// 从 metadata 中解析得出的物理存储格式 (如 "csv" 或 "parquet")
        /// </summary>
        string Format { get; }

        /// <summary>
        /// 所有已在 schema 注册的字段名称
        /// </summary>
        IReadOnlyList<string> FieldNames { get; }

        /// <summary>
        /// 获取所有可用的年份分区列表 (如 [2024, 2025])
        /// </summary>
        IReadOnlyList<int> GetAvailableYears();
        
        /// <summary>
        /// 获取某列字段名对应的 C# 强类型定义
        /// </summary>
        Type GetFieldType(string fieldName);

        /// <summary>
        /// 针对 CSV 格式：获取特定年份、特定股票的 CSV 物理文件路径
        /// </summary>
        string GetCsvFilePath(int year, string symbol);

        /// <summary>
        /// 针对 CSV 格式：递归扫描并枚举该表下所有个股的 CSV 物理文件路径列表
        /// </summary>
        IReadOnlyList<string> EnumerateCsvFiles();

        /// <summary>
        /// 针对 Parquet 格式：获取特定年份、特定月份的 Parquet 物理大表文件路径
        /// </summary>
        string GetParquetFilePath(int year, int month);

        /// <summary>
        /// 针对 Parquet 格式：递归扫描并枚举该表下所有按月分区的 Parquet 物理文件路径列表
        /// </summary>
        IReadOnlyList<string> EnumerateParquetFiles();
    }
}
