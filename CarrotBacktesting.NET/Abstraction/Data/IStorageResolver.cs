using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 数据区物理存储调度与元数据统一契约。
    /// 单点权威负责表元数据缓存管理与物理文件路径寻址、分区剪枝。
    /// </summary>
    public interface IStorageResolver
    {
        /// <summary>
        /// 数据区存储根目录完整物理路径。
        /// </summary>
        string StorageRoot { get; }

        /// <summary>
        /// 检查数据区内是否存在指定表 ID 的数据目录或元数据。
        /// </summary>
        /// <param name="tableId">数据表唯一标识</param>
        bool HasTable(string tableId);

        /// <summary>
        /// 获取指定表的权威元数据聚合模型（单点权威缓存）。
        /// </summary>
        /// <param name="tableId">数据表唯一标识</param>
        /// <returns>不可变 TableMetadata 实体</returns>
        TableMetadata GetMetadata(string tableId);

        /// <summary>
        /// 基于表 ID 解析物理数据文件列表，支持按代码路由与起止日期时间分区剪枝。
        /// </summary>
        /// <param name="tableId">数据表唯一标识</param>
        /// <param name="symbol">可选股票代码（若指定则精确路由单标的文件；若为 null 则返回全量文件）</param>
        /// <param name="startDate">可选开始日期（用于 Hive 分区剪枝）</param>
        /// <param name="endDate">可选结束日期（用于 Hive 分区剪枝）</param>
        /// <returns>排好序的物理数据文件完整路径列表</returns>
        IReadOnlyList<string> ResolvePhysicalFiles(string tableId, string? symbol = null, DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// 直通重载：直接基于已获取的表元数据解析物理文件列表，避免反复查询元数据缓存字典。
        /// </summary>
        /// <param name="metadata">已加载的不可变表元数据实体</param>
        /// <param name="symbol">可选股票代码</param>
        /// <param name="startDate">可选开始日期</param>
        /// <param name="endDate">可选结束日期</param>
        /// <returns>排好序的物理数据文件完整路径列表</returns>
        IReadOnlyList<string> ResolvePhysicalFiles(TableMetadata metadata, string? symbol = null, DateTime? startDate = null, DateTime? endDate = null);
    }
}
