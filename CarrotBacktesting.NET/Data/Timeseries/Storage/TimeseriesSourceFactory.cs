using System;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序数据源工厂类。
    /// 依据表的物理格式（由 IStorageResolver 自动推断）自适应构建 CSV 或 Parquet 数据源实例。
    /// </summary>
    public static class TimeseriesSourceFactory
    {
        /// <summary>
        /// 基于路径调度器自适应构建时序市场数据源。
        /// </summary>
        public static ITimeseriesSource Create(
            IStorageResolver resolver,
            IFieldRegistry registry,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

            string format = resolver.GetFormat(tableId);
            return format.ToLowerInvariant() switch
            {
                "csv" => new CsvTimeseriesSource(resolver, registry, tableId, startDate, endDate),
                "parquet" => new ParquetTimeseriesSource(resolver, registry, tableId, startDate, endDate),
                _ => throw new NotSupportedException($"Unsupported market series storage format: '{format}' for table '{tableId}'.")
            };
        }

        /// <summary>
        /// 基于存储物理根目录自适应构建时序市场数据源。
        /// </summary>
        public static ITimeseriesSource Create(
            string storageRoot,
            IFieldRegistry registry,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            return Create(new StorageResolver(storageRoot), registry, tableId, startDate, endDate);
        }
    }
}
