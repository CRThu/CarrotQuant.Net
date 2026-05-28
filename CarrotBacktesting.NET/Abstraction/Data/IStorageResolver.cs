using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 数据表的物理存储布局模式。
    /// </summary>
    public enum StorageLayout
    {
        /// <summary>
        /// Hive 分区模式 (含有 year=YYYY/ 子目录)
        /// </summary>
        Hive,

        /// <summary>
        /// Flat 平铺模式 (所有数据文件直接平铺在表目录下)
        /// </summary>
        Flat
    }

    /// <summary>
    /// 统一数据区多表路径调度解析契约。
    /// 不再绑定单表状态，作为全局数据区的“物理调度管理器”统一调度多表元数据及物理文件定位。
    /// </summary>
    public interface IStorageResolver
    {
        /// <summary>
        /// 数据区存储根目录 (例如 "D:/QuantData")
        /// </summary>
        string StorageRoot { get; }

        /// <summary>
        /// 检查数据区内是否存在指定表 ID 的数据目录与元数据。
        /// </summary>
        /// <param name="tableId">数据表 ID (例如 "ashare.kline.1d.raw.baostock")</param>
        /// <returns>若存在返回 true，否则返回 false</returns>
        bool HasTable(string tableId);

        /// <summary>
        /// 获取指定表的元数据 metadata.json 物理路径。
        /// </summary>
        /// <param name="tableId">数据表 ID</param>
        /// <returns>metadata.json 的完整物理路径</returns>
        string GetMetadataPath(string tableId);

        /// <summary>
        /// 获取指定表的物理存储格式 (例如 "csv" 或 "parquet")。
        /// </summary>
        /// <param name="tableId">数据表 ID</param>
        /// <returns>存储格式字符串 (全小写)</returns>
        string GetFormat(string tableId);

        /// <summary>
        /// 获取指定表的数据物理存储布局模式 (Hive 分区或 Flat 平铺)。
        /// </summary>
        /// <param name="tableId">数据表 ID</param>
        /// <returns>StorageLayout 布局类型</returns>
        StorageLayout GetLayout(string tableId);

        /// <summary>
        /// 获取指定表已在 Schema 中注册的所有字段名称列表。
        /// </summary>
        /// <param name="tableId">数据表 ID</param>
        /// <returns>字段名只读列表</returns>
        IReadOnlyList<string> GetFieldNames(string tableId);

        /// <summary>
        /// 获取指定表中某列字段名对应的 C# 强类型定义。
        /// </summary>
        /// <param name="tableId">数据表 ID</param>
        /// <param name="fieldName">字段名称</param>
        /// <returns>字段对应的 System.Type</returns>
        Type GetFieldType(string tableId, string fieldName);

        /// <summary>
        /// 获取指定表的数据分类 (例如 "timeseries" 或 "events")。
        /// </summary>
        /// <param name="tableId">数据表 ID</param>
        /// <returns>分类字符串 ("timeseries" 或 "events")</returns>
        string GetCategory(string tableId);

        /// <summary>
        /// 获取指定表的分区模式 (例如 "symbol"、"date" 或 "none")。
        /// </summary>
        /// <param name="tableId">数据表 ID</param>
        /// <returns>分区模式字符串 ("symbol"、"date" 或 "none")</returns>
        string GetPartition(string tableId);

        /// <summary>
        /// 终极物理路径调度与分区剪枝 API。
        /// 支持根据起止日期进行可选的年份分区剪枝，并支持按 symbol 智能路由（若为 null 则返回整表所有分区文件），返回解析并排序后的物理数据文件列表。
        /// 向上层 Reader 屏蔽所有目录探测、分区查找及路径拼接物理细节。
        /// </summary>
        /// <param name="tableId">数据表 ID</param>
        /// <param name="symbol">可选股票代码（若提供，将精确定位该股数据文件；若为 null，返回整表所有数据文件）</param>
        /// <param name="startDate">可选开始日期（用于 Hive 分区剪枝）</param>
        /// <param name="endDate">可选结束日期（用于 Hive 分区剪枝）</param>
        /// <returns>排好序的物理数据文件完整路径列表</returns>
        IReadOnlyList<string> ResolvePhysicalFiles(string tableId, string? symbol = null, DateTime? startDate = null, DateTime? endDate = null);
    }
}

