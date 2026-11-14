using System;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 事件数据源工厂。
    /// 根据存储格式自动选择对应的驱动并加载 IEventTable 实例，支持强类型元数据与统一调度器装配。
    /// </summary>
    public static class EventSourceFactory
    {
        private static readonly CsvEventSource _csvSource = new();
        private static readonly ParquetEventSource _parquetSource = new();

        /// <summary>
        /// 基于不可变元数据与统一存储调度器构建事件表。
        /// </summary>
        public static IEventTable Create(
            TableMetadata metadata,
            IStorageResolver resolver,
            DateTime? startDate = null,
            DateTime? endDate = null,
            IReadOnlySet<string>? nullableFields = null)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(resolver);

            return metadata.Format switch
            {
                StorageFormat.Csv => _csvSource.LoadTable(metadata, resolver, startDate, endDate, nullableFields),
                StorageFormat.Parquet => _parquetSource.LoadTable(metadata, resolver, startDate, endDate, nullableFields),
                _ => throw new NotSupportedException($"Unsupported event storage format: '{metadata.Format}' for table '{metadata.TableId}'.")
            };
        }

        /// <summary>
        /// 加载事件表，支持按时间区间进行分区剪枝（工厂层自动解析元数据并注入）。
        /// </summary>
        public static IEventTable Create(
            IStorageResolver resolver,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            IReadOnlySet<string>? nullableFields = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

            var metadata = resolver.GetMetadata(tableId);
            return Create(metadata, resolver, startDate, endDate, nullableFields);
        }

        /// <summary>
        /// 基于存储根目录快速构建事件表。
        /// </summary>
        public static IEventTable Create(
            string storageRoot,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            IReadOnlySet<string>? nullableFields = null)
        {
            return Create(new StorageResolver(storageRoot), tableId, startDate, endDate, nullableFields);
        }
    }
}
