using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序注册表装配工厂，负责装配并初始化 <see cref="ITimeseriesRegistry"/> 实例。
    /// </summary>
    public static class TimeseriesRegistryFactory
    {
        /// <summary>
        /// 根据已初始化的时序数据源装配单表模式的 <see cref="ITimeseriesRegistry"/> 实例。
        /// </summary>
        public static ITimeseriesRegistry Create(
            string storageRoot,
            ITimeseriesSource source,
            int pageSize = 4096,
            bool enableCache = false,
            CacheRefreshMode refreshMode = CacheRefreshMode.Lazy,
            string tableId = "default",
            CacheCompression compression = CacheCompression.Lz4,
            int commitBatchSize = 64,
            int maxPages = 4,
            bool prefetch = true,
            bool isLive = false)
        {
            var primaryTable = new TimeseriesTable(
                tableId,
                isPrimary: true,
                source,
                storageRoot,
                pageSize,
                enableCache,
                refreshMode,
                compression,
                commitBatchSize,
                maxPages,
                prefetch,
                isLive,
                ownsSource: false);
            return new TimeseriesRegistry(primaryTable);
        }

        /// <summary>
        /// 根据路径寻址解析器与单表配置装配并初始化 <see cref="ITimeseriesRegistry"/> 实例。
        /// </summary>
        public static ITimeseriesRegistry Create(
            IStorageResolver resolver,
            TimeseriesConfig config,
            MarketTime? startDate = null,
            MarketTime? endDate = null,
            bool isLive = false)
        {
            ArgumentNullException.ThrowIfNull(config);
            return Create(resolver, new[] { config }, startDate, endDate, isLive);
        }

        /// <summary>
        /// 根据配置列表装配主时序表及辅助子表，构建时间轴映射并初始化 <see cref="ITimeseriesRegistry"/> 实例。
        /// </summary>
        public static ITimeseriesRegistry Create(
            IStorageResolver resolver,
            IEnumerable<TimeseriesConfig> configs,
            MarketTime? startDate = null,
            MarketTime? endDate = null,
            bool isLive = false)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentNullException.ThrowIfNull(configs);

            var configList = configs.ToList();
            if (configList.Count == 0)
            {
                throw new InvalidOperationException("时序配置列表不能为空。回测引擎依赖主时序行情表作为推进时间轴。");
            }

            // 1. 确定主表：未显式指定时默认第一张为 true，若有多个 true 则抛出异常
            int primaryCount = configList.Count(c => c.Primary);
            if (primaryCount > 1)
            {
                throw new InvalidOperationException(
                    $"检测到配置了 {primaryCount} 个 primary: true 的时序行情表。全局仅允许一个主时钟表。");
            }
            if (primaryCount == 0)
            {
                configList[0].Primary = true;
            }

            var primaryConfig = configList.First(c => c.Primary);

            // 2. 装配主表
            var primaryTable = CreateTable(resolver, primaryConfig, isPrimary: true, startDate, endDate, isLive, subRowMap: null);

            // 3. 构建辅助子表，并生成相对于主表时间轴的行映射
            var subTables = new List<ITimeseriesTable>();
            foreach (var cfg in configList)
            {
                if (ReferenceEquals(cfg, primaryConfig))
                {
                    continue;
                }

                var subNullable = cfg.NullableFields != null
                    ? new HashSet<string>(cfg.NullableFields, StringComparer.OrdinalIgnoreCase)
                    : null;
                var subSource = TimeseriesSourceFactory.Create(resolver, cfg.TableId, startDate, endDate, cfg.TimeZone, subNullable);

                // 计算当前子表在主表各时间步的有效行索引映射
                int[] subRowMap = TimeseriesTimelineRouter.BuildAsOfMap(
                    primaryTable.Metadata.TradeDates,
                    subSource.TradeDates,
                    cfg.TimeOffset);

                var subTable = new TimeseriesTable(
                    cfg.TableId,
                    isPrimary: false,
                    subSource,
                    subSource,
                    resolver.StorageRoot,
                    pageSize: cfg.PageSize,
                    enableCache: cfg.Cache,
                    refreshMode: cfg.Refresh,
                    compression: cfg.Compression,
                    commitBatchSize: cfg.CommitBatchSize,
                    maxPages: cfg.MaxPages,
                    prefetch: cfg.Prefetch,
                    isLive: isLive,
                    ownsSource: true,
                    subRowMap: subRowMap);

                // 预热显式声明的字段
                if (cfg.Fields != null && cfg.Fields.Count > 0)
                {
                    foreach (var f in cfg.Fields)
                    {
                        if (subTable.HasField(f))
                        {
                            subTable.GetCacheManager(f).EnsureLoaded();
                        }
                    }
                }

                subTables.Add(subTable);
            }

            var registry = new TimeseriesRegistry(primaryTable, subTables);

            // 预热主表显式声明的字段
            if (primaryConfig.Fields != null && primaryConfig.Fields.Count > 0)
            {
                foreach (var f in primaryConfig.Fields)
                {
                    if (primaryTable.HasField(f))
                    {
                        primaryTable.GetCacheManager(f).EnsureLoaded();
                    }
                }
            }

            return registry;
        }

        /// <summary>
        /// 创建单个时序表实例。
        /// </summary>
        public static TimeseriesTable CreateTable(
            IStorageResolver resolver,
            TimeseriesConfig config,
            bool isPrimary,
            MarketTime? startDate = null,
            MarketTime? endDate = null,
            bool isLive = false,
            int[]? subRowMap = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentNullException.ThrowIfNull(config);
            ArgumentException.ThrowIfNullOrWhiteSpace(config.TableId);

            var nullableFields = config.NullableFields != null
                ? new HashSet<string>(config.NullableFields, StringComparer.OrdinalIgnoreCase)
                : null;
            var source = TimeseriesSourceFactory.Create(resolver, config.TableId, startDate, endDate, config.TimeZone, nullableFields);

            return new TimeseriesTable(
                config.TableId,
                isPrimary: isPrimary,
                source,
                source,
                resolver.StorageRoot,
                pageSize: config.PageSize,
                enableCache: config.Cache,
                refreshMode: config.Refresh,
                compression: config.Compression,
                commitBatchSize: config.CommitBatchSize,
                maxPages: config.MaxPages,
                prefetch: config.Prefetch,
                isLive: isLive,
                ownsSource: true,
                subRowMap: subRowMap);
        }

        /// <summary>
        /// 根据物理存储根目录与单表配置装配并初始化 <see cref="ITimeseriesRegistry"/> 实例。
        /// </summary>
        public static ITimeseriesRegistry Create(
            string storageRoot,
            TimeseriesConfig config,
            MarketTime? startDate = null,
            MarketTime? endDate = null,
            bool isLive = false)
        {
            return Create(new StorageResolver(storageRoot), config, startDate, endDate, isLive);
        }
    }
}
