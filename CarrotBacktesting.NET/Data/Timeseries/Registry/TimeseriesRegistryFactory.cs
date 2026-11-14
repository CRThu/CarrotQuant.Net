using System;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序注册表装配工厂。
    /// 统一装配并初始化 <see cref="TimeseriesRegistry"/> 实例。
    /// </summary>
    public static class TimeseriesRegistryFactory
    {
        /// <summary>
        /// 根据底层已初始化的时序数据源装配并初始化 <see cref="ITimeseriesRegistry"/> 实例。
        /// </summary>
        public static ITimeseriesRegistry Create(
            string storageRoot,
            ITimeseriesSource source,
            int pageSize = 4096,
            bool enableCache = false,
            string refreshMode = "lazy",
            string tableId = "default",
            string compression = "lz4",
            int commitBatchSize = 64,
            int maxPages = 4,
            bool prefetch = true,
            bool isLive = false)
        {
            return new TimeseriesRegistry(source, storageRoot, pageSize, enableCache, refreshMode, tableId, compression, commitBatchSize, maxPages, prefetch, isLive);
        }

        /// <summary>
        /// 根据路径寻址解析器与单表配置装配并初始化 <see cref="ITimeseriesRegistry"/> 实例。
        /// 自动根据表 ID 解析底层物理格式（CSV/Parquet）自适应构建数据源。
        /// </summary>
        /// <param name="resolver">物理存储寻址解析器。</param>
        /// <param name="config">时序配置（来自 SessionConfig.Data.Timeseries）。</param>
        /// <param name="startDate">可选回测起始日期（用于物理年份分区剪枝）。</param>
        /// <param name="endDate">可选回测结束日期（用于物理年份分区剪枝）。</param>
        /// <param name="isLive">是否为实盘环境（实盘下一票否决关闭预读）。</param>
        /// <returns>已装配完成的 ITimeseriesRegistry 实例。</returns>
        public static ITimeseriesRegistry Create(
            IStorageResolver resolver,
            TimeseriesConfig config,
            DateTime? startDate = null,
            DateTime? endDate = null,
            bool isLive = false)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentNullException.ThrowIfNull(config);
            ArgumentException.ThrowIfNullOrWhiteSpace(config.TableId);

            var source = TimeseriesSourceFactory.Create(resolver, config.TableId, startDate, endDate, config.TimeZone);
            return new TimeseriesRegistry(
                source,
                resolver.StorageRoot,
                pageSize: config.PageSize,
                enableCache: config.Cache,
                refreshMode: config.Refresh,
                tableId: config.TableId,
                compression: config.Compression,
                commitBatchSize: config.CommitBatchSize,
                maxPages: config.MaxPages,
                prefetch: config.Prefetch,
                isLive: isLive);
        }

        /// <summary>
        /// 根据物理存储根目录与单表配置装配并初始化 <see cref="ITimeseriesRegistry"/> 实例。
        /// </summary>
        public static ITimeseriesRegistry Create(
            string storageRoot,
            TimeseriesConfig config,
            DateTime? startDate = null,
            DateTime? endDate = null,
            bool isLive = false)
        {
            return Create(new StorageResolver(storageRoot), config, startDate, endDate, isLive);
        }
    }
}
