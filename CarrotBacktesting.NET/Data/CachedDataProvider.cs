using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 带时序二进制缓存（TimeseriesCache）的高性能数据供给器。
    /// 实现 <see cref="IDataProvider"/>，对外向策略提供只读二维时序矩阵，对内协调各字段分块管理器的加载与落盘。
    /// </summary>
    public class CachedDataProvider : IDataProvider, IDisposable
    {
        private readonly IMarketMetadata _metadata;
        private readonly IFieldRegistry _registry;
        private readonly IMarketSeriesSource _source;
        private readonly string _storageRoot;
        private readonly int _pageSize;
        private readonly bool _enableCache;
        private readonly string _refreshMode;
        private readonly ITimeseriesCacheStore? _cacheStore;
        private bool _disposed;

        // 统一持有各个字段的 TimeseriesChunkManager
        private readonly ConcurrentDictionary<string, ITimeseriesChunkManager> _managers = new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public IMarketMetadata Metadata => _metadata;

        /// <inheritdoc />
        public bool HasField(string fieldName) => _registry.FieldExists(fieldName);

        /// <summary>
        /// 初始化 <see cref="CachedDataProvider"/> 实例（以 <paramref name="source"/> 作为元数据单一事实来源）。
        /// </summary>
        public CachedDataProvider(
            IMarketSeriesSource source, 
            IFieldRegistry registry, 
            string storageRoot,
            int pageSize = 4096,
            bool enableCache = false,
            string refreshMode = "lazy",
            string tableId = "default",
            string compression = "lz4")
            : this(source, registry, source, storageRoot, pageSize, enableCache, refreshMode, tableId, compression)
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
            string compression = "lz4")
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _storageRoot = storageRoot;
            _pageSize = pageSize;
            _enableCache = enableCache;
            _refreshMode = refreshMode;

            if (enableCache && !string.IsNullOrWhiteSpace(storageRoot))
            {
                _cacheStore = new TimeseriesCacheStore(storageRoot, tableId, pageSize, compression);
            }
        }

        /// <inheritdoc />
        public IReadOnlyBuffer2D<T> GetBuffer<T>(string fieldName) where T : unmanaged
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

            // 3. 原子获取或创建该字段的 TimeseriesChunkManager
            var manager = (TimeseriesChunkManager<T>)_managers.GetOrAdd(fieldName, _ =>
                new TimeseriesChunkManager<T>(fieldName, _metadata, _source, _cacheStore, _pageSize, _refreshMode));

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

            return (TimeseriesChunkManager<T>)_managers.GetOrAdd(fieldName, _ =>
                new TimeseriesChunkManager<T>(fieldName, _metadata, _source, _cacheStore, _pageSize, _refreshMode));
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
