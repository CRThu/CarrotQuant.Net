using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 高性能时序数据供给门面（Facade）。
    /// 实现 <see cref="IDataProvider"/>，对外向策略提供只读二维时序矩阵（GetMatrix）与单标的一维序列（GetSeries），
    /// 对内纯粹持有并协调各字段分块调度器（<see cref="ITimeseriesChunkManager"/>）的生命周期与增量落盘（Commit）。
    /// 物理存储路径、分块配置与持久化存储器完全由外部工厂装配隔离。
    /// </summary>
    public class CachedDataProvider : IDataProvider, IDisposable
    {
        private readonly IMarketMetadata _metadata;
        private readonly IFieldRegistry _registry;
        private readonly Func<string, Type, ITimeseriesChunkManager> _managerFactory;
        private readonly ConcurrentDictionary<string, ITimeseriesChunkManager> _managers = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        /// <inheritdoc />
        public IMarketMetadata Metadata => _metadata;

        /// <inheritdoc />
        public bool HasField(string fieldName) => _registry.FieldExists(fieldName);

        /// <summary>
        /// 初始化 <see cref="CachedDataProvider"/> 实例（纯粹的工厂装配模式，彻底解耦底层存储细节）。
        /// </summary>
        /// <param name="metadata">市场元数据。</param>
        /// <param name="registry">字段注册表。</param>
        /// <param name="managerFactory">单字段分块管理器解析工厂委托。</param>
        public CachedDataProvider(
            IMarketMetadata metadata,
            IFieldRegistry registry,
            Func<string, Type, ITimeseriesChunkManager> managerFactory)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            _metadata = metadata is DynamicMarketMetadata dynamicMeta 
                ? dynamicMeta 
                : new DynamicMarketMetadata(metadata);
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _managerFactory = managerFactory ?? throw new ArgumentNullException(nameof(managerFactory));
        }

        /// <summary>
        /// 初始化 <see cref="CachedDataProvider"/> 实例（以数据源为单一事实来源的便捷构造函数）。
        /// </summary>
        public CachedDataProvider(
            IMarketSeriesSource source, 
            IFieldRegistry registry, 
            string storageRoot,
            int pageSize = 4096,
            bool enableCache = false,
            string refreshMode = "lazy",
            string tableId = "default",
            string compression = "lz4",
            int commitBatchSize = 64)
            : this(source, registry, CreateDefaultFactory(source, storageRoot, pageSize, enableCache, refreshMode, tableId, compression, commitBatchSize))
        {
        }

        /// <summary>
        /// 初始化 <see cref="CachedDataProvider"/> 实例。
        /// </summary>
        public CachedDataProvider(
            IMarketMetadata metadata, 
            IFieldRegistry registry, 
            IMarketSeriesSource source, 
            string storageRoot,
            int pageSize = 4096,
            bool enableCache = false,
            string refreshMode = "lazy",
            string tableId = "default",
            string compression = "lz4",
            int commitBatchSize = 64)
            : this(metadata, registry, CreateDefaultFactory(source, storageRoot, pageSize, enableCache, refreshMode, tableId, compression, commitBatchSize))
        {
        }

        private static Func<string, Type, ITimeseriesChunkManager> CreateDefaultFactory(
            IMarketSeriesSource source,
            string storageRoot,
            int pageSize,
            bool enableCache,
            string refreshMode,
            string tableId,
            string compression,
            int commitBatchSize = 64)
        {
            ITimeseriesStore? store = null;
            if (enableCache && !string.IsNullOrWhiteSpace(storageRoot))
            {
                store = new TimeseriesStore(storageRoot, tableId, pageSize, compression);
            }

            return (fieldName, dataType) =>
            {
                var managerType = typeof(TimeseriesChunkManager<>).MakeGenericType(dataType);
                return (ITimeseriesChunkManager)Activator.CreateInstance(
                    managerType,
                    fieldName,
                    source,
                    source,
                    store,
                    pageSize,
                    refreshMode,
                    commitBatchSize)!;
            };
        }

        /// <inheritdoc />
        public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 1. 字段注册路由校验
            if (!_registry.FieldExists(fieldName))
            {
                throw new KeyNotFoundException($"Field '{fieldName}' not registered in registry.");
            }

            // 2. 类型检查 (确保 requested T 与 field 实际类型一致)
            var fieldType = _registry.GetFieldType(fieldName);
            if (fieldType != typeof(T))
            {
                throw new InvalidOperationException($"Type mismatch for field '{fieldName}'. Expected {fieldType}, requested {typeof(T)}.");
            }

            // 3. 原子获取或通过工厂委托创建该字段的 TimeseriesChunkManager
            var manager = (TimeseriesChunkManager<T>)_managers.GetOrAdd(fieldName, name => _managerFactory(name, typeof(T)));
            return manager.AsReadOnly();
        }

        /// <summary>
        /// 获取指定字段的分块管理器实例（供实盘时间步推进与高级调度使用）。
        /// </summary>
        /// <typeparam name="T">非托管数值类型。</typeparam>
        /// <param name="fieldName">字段名称。</param>
        /// <returns>对应的分块调度器实例。</returns>
        public TimeseriesChunkManager<T> GetChunkManager<T>(string fieldName) where T : unmanaged
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_registry.FieldExists(fieldName))
            {
                throw new KeyNotFoundException($"Field '{fieldName}' not registered in registry.");
            }

            var fieldType = _registry.GetFieldType(fieldName);
            if (fieldType != typeof(T))
            {
                throw new InvalidOperationException($"Type mismatch for field '{fieldName}'. Expected {fieldType}, requested {typeof(T)}.");
            }

            return (TimeseriesChunkManager<T>)_managers.GetOrAdd(fieldName, name => _managerFactory(name, typeof(T)));
        }

        /// <summary>
        /// 门面级时序行追加：向指定字段追加截面数据并同步推进元数据时间轴。
        /// </summary>
        public void AppendRow<T>(string fieldName, DateTime timestamp, ReadOnlySpan<T> rowValues) where T : unmanaged
        {
            var manager = GetChunkManager<T>(fieldName);
            manager.AppendRow(timestamp, rowValues);
        }

        /// <summary>
        /// 门面级新标的上市扩列（因果顺序：先结清未落盘行，再拓宽列并提交元数据）。
        /// 广播通知所有已活跃的字段管理器执行列拓宽。
        /// </summary>
        public void AppendCol(string symbol)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _metadata.AppendSymbol(symbol);

            foreach (var manager in _managers.Values)
            {
                manager.AppendCol(symbol);
            }
        }

        /// <inheritdoc />
        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var manager in _managers.Values)
            {
                manager.Commit();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var manager in _managers.Values)
            {
                manager.Dispose();
            }
            _managers.Clear();
        }
    }
}
