using System;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序数据源工厂类。
    /// 依据表的物理格式自适应构建 CSV 或 Parquet 数据源实例，支持强类型元数据与统一调度器装配。
    /// </summary>
    public static class TimeseriesSourceFactory
    {
        /// <summary>
        /// 基于不可变表元数据与统一存储调度器构建时序市场数据源。
        /// </summary>
        public static ITimeseriesSource Create(
            TableMetadata metadata,
            IStorageResolver resolver,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? timeZone = null,
            IReadOnlySet<string>? nullableFields = null)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(resolver);

            return metadata.Format switch
            {
                StorageFormat.Csv => new CsvTimeseriesSource(metadata, resolver, startDate, endDate, timeZone),
                StorageFormat.Parquet => new ParquetTimeseriesSource(metadata, resolver, startDate, endDate, timeZone, nullableFields),
                _ => throw new NotSupportedException($"Unsupported market series storage format: '{metadata.Format}' for table '{metadata.TableId}'.")
            };
        }

        /// <summary>
        /// 基于存储调度器自适应构建时序市场数据源（工厂层自动解析元数据并注入）。
        /// </summary>
        public static ITimeseriesSource Create(
            IStorageResolver resolver,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? timeZone = null,
            IReadOnlySet<string>? nullableFields = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

            var metadata = resolver.GetMetadata(tableId);
            return Create(metadata, resolver, startDate, endDate, timeZone, nullableFields);
        }

        /// <summary>
        /// 基于存储物理根目录自适应构建时序市场数据源。
        /// </summary>
        public static ITimeseriesSource Create(
            string storageRoot,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? timeZone = null,
            IReadOnlySet<string>? nullableFields = null)
        {
            return Create(new StorageResolver(storageRoot), tableId, startDate, endDate, timeZone, nullableFields);
        }
    }
}
