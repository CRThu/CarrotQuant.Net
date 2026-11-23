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
        /// 根据已初始化的时序数据源与分块缓存选项装配单表模式的 <see cref="TimeseriesRegistry"/> 实例。
        /// </summary>
        public static TimeseriesRegistry Create(
            ITimeseriesSource source,
            TimeseriesCacheOptions? options = null,
            string tableId = "primary",
            bool ownsSource = false)
        {
            var primaryTable = TimeseriesTableFactory.Create(
                string.IsNullOrWhiteSpace(tableId) ? "primary" : tableId,
                isPrimary: true,
                source: source,
                options: options,
                subRowMap: null,
                ownsSource: ownsSource);
            return new TimeseriesRegistry(primaryTable);
        }


        /// <summary>
        /// 根据路径寻址解析器与单表配置装配并初始化 <see cref="TimeseriesRegistry"/> 实例。
        /// </summary>
        public static TimeseriesRegistry Create(
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
        /// 根据配置列表装配主时序表及辅助子表，构建时间轴映射并初始化 <see cref="TimeseriesRegistry"/> 实例。
        /// 具备严格的异常安全保障：装配过程中若发生任何异常，自动安全释放已分配的所有时序表资源。
        /// </summary>
        public static TimeseriesRegistry Create(
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
            var subTables = new List<ITimeseriesTable>();

            try
            {
                // 3. 构建辅助子表，并生成相对于主表时间轴的行映射
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

                    var subOptions = cfg.ToCacheOptions(resolver.StorageRoot, isLive);
                    var subTable = TimeseriesTableFactory.Create(
                        cfg.TableId,
                        isPrimary: false,
                        source: subSource,
                        options: subOptions,
                        subRowMap: subRowMap,
                        ownsSource: true);

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
            catch
            {
                // 异常安全回滚：释放已创建的所有表句柄，防范文件句柄与连续非托管内存泄漏
                primaryTable.Dispose();
                for (int i = subTables.Count - 1; i >= 0; i--)
                {
                    (subTables[i] as IDisposable)?.Dispose();
                }
                throw;
            }
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
            return TimeseriesTableFactory.Create(
                resolver,
                config,
                isPrimary,
                startDate,
                endDate,
                isLive,
                subRowMap);
        }

        /// <summary>
        /// 根据物理存储根目录与单表配置装配并初始化 <see cref="TimeseriesRegistry"/> 实例。
        /// </summary>
        public static TimeseriesRegistry Create(
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
