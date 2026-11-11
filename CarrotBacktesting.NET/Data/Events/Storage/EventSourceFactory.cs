using System;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 异构事件市场数据源工厂。
    /// 依据表的物理格式（由 IStorageResolver 自动推断）自适应派发并构建强类型事件表容器。
    /// </summary>
    public static class EventSourceFactory
    {
        private static readonly CsvEventSource _csvSource = new();
        private static readonly ParquetEventSource _parquetSource = new();

        /// <summary>
        /// 基于路径调度器自适应构建强类型事件表。
        /// </summary>
        public static IEventTable<T> Create<T>(IStorageResolver resolver, string tableId) where T : class
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

            return source.LoadTable<T>(resolver, tableId);
        }

        /// <summary>
        /// 基于物理存储根目录自适应构建强类型事件表。
        /// </summary>
        public static IEventTable<T> Create<T>(string storageRoot, string tableId) where T : class
        {
            return Create<T>(new StorageResolver(storageRoot), tableId);
        }
    }
}
