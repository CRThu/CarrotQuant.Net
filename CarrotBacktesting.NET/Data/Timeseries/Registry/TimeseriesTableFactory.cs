using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序表装配工厂，负责将物理时序源、磁盘分块缓存与分块调度器装配为运行期时序表实例 <see cref="TimeseriesTable"/>。
    /// </summary>
    public static class TimeseriesTableFactory
    {
        /// <summary>
        /// 基于时序数据源与分块缓存选项装配时序表实例。
        /// </summary>
        public static TimeseriesTable Create(
            string name,
            bool isPrimary,
            ITimeseriesSource source,
            TimeseriesCacheOptions? options = null,
            int[]? subRowMap = null,
            bool ownsSource = false)
        {
            return Create(name, isPrimary, source, source, options, subRowMap, ownsSource);
        }

        /// <summary>
        /// 基于指定元数据、时序数据源与分块缓存选项装配时序表实例。
        /// </summary>
        public static TimeseriesTable Create(
            string name,
            bool isPrimary,
            IMarketMetadata metadata,
            ITimeseriesSource source,
            TimeseriesCacheOptions? options = null,
            int[]? subRowMap = null,
            bool ownsSource = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(source);

            var managerFactory = CreateManagerFactory(source, metadata, name, options);

            return new TimeseriesTable(
                name,
                isPrimary,
                metadata,
                managerFactory,
                subRowMap,
                ownsSource ? source : null);
        }

        /// <summary>
        /// 根据路径寻址解析器与单表配置装配时序表实例。
        /// </summary>
        public static TimeseriesTable Create(
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
            var options = config.ToCacheOptions(resolver.StorageRoot, isLive);

            return Create(
                config.TableId,
                isPrimary: isPrimary,
                source: source,
                options: options,
                subRowMap: subRowMap,
                ownsSource: true);
        }

        /// <summary>
        /// 构建分块调度器工厂委托。
        /// </summary>
        public static Func<string, Type, ITimeseriesCacheManager> CreateManagerFactory(
            ITimeseriesSource source,
            IMarketMetadata? metadata = null,
            string tableId = "primary",
            TimeseriesCacheOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            var opt = options ?? TimeseriesCacheOptions.Default;
            var meta = metadata ?? source;

            ITimeseriesCache? store = null;
            if (opt.EnableCache && !string.IsNullOrWhiteSpace(opt.StorageRoot))
            {
                store = new TimeseriesCache(opt.StorageRoot, tableId, opt.PageSize, opt.Compression);
            }

            return (fieldName, dataType) =>
            {
                var managerType = typeof(TimeseriesCacheManager<>).MakeGenericType(dataType);
                return (ITimeseriesCacheManager)Activator.CreateInstance(
                    managerType,
                    fieldName,
                    meta,
                    source,
                    store,
                    opt)!;
            };
        }
    }
}
