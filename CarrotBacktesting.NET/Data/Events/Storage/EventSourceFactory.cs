using System;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 事件数据源工厂。
    /// 根据存储格式自动选择对应的驱动并加载 IEventTable 实例。
    /// </summary>
    public static class EventSourceFactory
    {
        private static readonly CsvEventSource _csvSource = new();
        private static readonly ParquetEventSource _parquetSource = new();

        /// <summary>
        /// 加载事件表，支持按时间区间进行分区剪枝。
        /// </summary>
        public static IEventTable Create(
            IStorageResolver resolver,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

            string format = resolver.GetFormat(tableId);
            IEventSource source = format.ToLowerInvariant() switch
            {
                "csv" => _csvSource,
                "parquet" => _parquetSource,
                _ => throw new NotSupportedException($"Unsupported event storage format: '{format}' for table '{tableId}'.")
            };

            return source.LoadTable(resolver, tableId, startDate, endDate);
        }

        /// <summary>
        /// 基于存储根目录快速构建事件表。
        /// </summary>
        public static IEventTable Create(
            string storageRoot,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            return Create(new StorageResolver(storageRoot), tableId, startDate, endDate);
        }
    }
}
