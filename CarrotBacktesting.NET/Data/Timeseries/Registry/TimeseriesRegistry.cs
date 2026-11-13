using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 高性能时序数据供给门面（Facade）。
    /// 同时实现只读契约 <see cref="ITimeseriesRegistry"/> 与写入/生命周期调度契约 <see cref="ITimeseriesWriter"/>，
    /// 对外向策略提供只读二维时序矩阵（GetMatrix）与单标的一维序列（GetSeries），
    /// 对内纯粹持有并协调各字段分块调度器（<see cref="ITimeseriesCacheManager"/>）的生命周期与持久化一致性提交（Commit）。
    /// </summary>
    public class TimeseriesRegistry : ITimeseriesRegistry, ITimeseriesWriter, IDisposable
    {
        private readonly IMarketMetadata _metadata;
        private readonly Func<string, Type, ITimeseriesCacheManager> _managerFactory;
        private readonly ConcurrentDictionary<string, ITimeseriesCacheManager> _managers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _tableIndices = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _tableNames = new();
        private readonly object _tableLock = new();
        private ulong _updatedMask = ulong.MaxValue;
        private bool _disposed;

        /// <inheritdoc />
        public IMarketMetadata Metadata => _metadata;

        /// <inheritdoc />
        public bool HasField(string fieldName) => _metadata.FieldNames.Any(f => f.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 初始化 <see cref="TimeseriesRegistry"/> 实例（纯粹的工厂装配模式，彻底解耦底层存储细节）。
        /// </summary>
        /// <param name="metadata">市场元数据。</param>
        /// <param name="managerFactory">单字段分块管理器解析工厂委托。</param>
        public TimeseriesRegistry(
            IMarketMetadata metadata,
            Func<string, Type, ITimeseriesCacheManager> managerFactory)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            _metadata = metadata is DynamicMarketMetadata dynamicMeta 
                ? dynamicMeta 
                : new DynamicMarketMetadata(metadata);
            _managerFactory = managerFactory ?? throw new ArgumentNullException(nameof(managerFactory));
        }

        /// <summary>
        /// 初始化 <see cref="TimeseriesRegistry"/> 实例（以数据源为单一事实来源的便捷构造函数）。
        /// </summary>
        public TimeseriesRegistry(
            ITimeseriesSource source, 
            string storageRoot,
            int pageSize = 4096,
            bool enableCache = false,
            string refreshMode = "lazy",
            string tableId = "default",
            string compression = "lz4",
            int commitBatchSize = 64,
            int maxPages = 4,
            bool prefetch = true,
            bool isLive = false)
            : this(source, CreateDefaultFactory(source, storageRoot, pageSize, enableCache, refreshMode, tableId, compression, commitBatchSize, maxPages, prefetch, isLive))
        {
            if (!string.IsNullOrWhiteSpace(tableId)) RegisterTable(tableId);
        }

        /// <summary>
        /// 初始化 <see cref="TimeseriesRegistry"/> 实例。
        /// </summary>
        public TimeseriesRegistry(
            IMarketMetadata metadata, 
            ITimeseriesSource source, 
            string storageRoot,
            int pageSize = 4096,
            bool enableCache = false,
            string refreshMode = "lazy",
            string tableId = "default",
            string compression = "lz4",
            int commitBatchSize = 64,
            int maxPages = 4,
            bool prefetch = true,
            bool isLive = false)
            : this(metadata, CreateDefaultFactory(source, storageRoot, pageSize, enableCache, refreshMode, tableId, compression, commitBatchSize, maxPages, prefetch, isLive))
        {
            if (!string.IsNullOrWhiteSpace(tableId)) RegisterTable(tableId);
        }

        private static Func<string, Type, ITimeseriesCacheManager> CreateDefaultFactory(
            ITimeseriesSource source,
            string storageRoot,
            int pageSize,
            bool enableCache,
            string refreshMode,
            string tableId,
            string compression,
            int commitBatchSize = 64,
            int maxPages = 4,
            bool prefetch = true,
            bool isLive = false)
        {
            ITimeseriesCache? store = null;
            if (enableCache && !string.IsNullOrWhiteSpace(storageRoot))
            {
                store = new TimeseriesCache(storageRoot, tableId, pageSize, compression);
            }

            return (fieldName, dataType) =>
            {
                var managerType = typeof(TimeseriesCacheManager<>).MakeGenericType(dataType);
                return (ITimeseriesCacheManager)Activator.CreateInstance(
                    managerType,
                    fieldName,
                    source,
                    source,
                    store,
                    pageSize,
                    refreshMode,
                    commitBatchSize,
                    maxPages,
                    prefetch,
                    isLive)!;
            };
        }

        /// <inheritdoc />
        public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 1. 字段注册路由校验
            if (!HasField(fieldName))
            {
                throw new KeyNotFoundException($"Field '{fieldName}' not registered in metadata.");
            }

            // 2. 类型检查 (确保 requested T 与 field 实际类型一致)
            var fieldType = _metadata.GetFieldType(fieldName);
            if (fieldType != typeof(T))
            {
                throw new InvalidOperationException($"Type mismatch for field '{fieldName}'. Expected {fieldType}, requested {typeof(T)}.");
            }

            // 3. 原子获取或通过工厂委托创建该字段的 TimeseriesCacheManager
            var manager = (TimeseriesCacheManager<T>)_managers.GetOrAdd(fieldName, name => _managerFactory(name, typeof(T)));
            return manager.AsReadOnly();
        }

        /// <summary>
        /// 获取指定字段的分块管理器实例（供实盘时间步推进与高级调度使用）。
        /// </summary>
        /// <typeparam name="T">非托管数值类型。</typeparam>
        /// <param name="fieldName">字段名称。</param>
        /// <returns>对应的分块调度器实例。</returns>
        public TimeseriesCacheManager<T> GetCacheManager<T>(string fieldName) where T : unmanaged
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!HasField(fieldName))
            {
                throw new KeyNotFoundException($"Field '{fieldName}' not registered in metadata.");
            }

            var fieldType = _metadata.GetFieldType(fieldName);
            if (fieldType != typeof(T))
            {
                throw new InvalidOperationException($"Type mismatch for field '{fieldName}'. Expected {fieldType}, requested {typeof(T)}.");
            }

            return (TimeseriesCacheManager<T>)_managers.GetOrAdd(fieldName, name => _managerFactory(name, typeof(T)));
        }

        /// <summary>
        /// 门面级时序行追加：向指定字段追加截面数据并同步推进元数据时间轴。
        /// </summary>
        public void AppendRow<T>(string fieldName, DateTime timestamp, ReadOnlySpan<T> rowValues) where T : unmanaged
        {
            var manager = GetCacheManager<T>(fieldName);
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

        /// <summary>
        /// 门面级时间步步进通知：广播通知所有活跃的分块管理器推进至第 <paramref name="currentLogicalRow"/> 行，
        /// 触发后台异步预读（Prefetch）与常驻窗口滑动淘汰。
        /// </summary>
        public void StepTo(int currentLogicalRow)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var manager in _managers.Values)
            {
                manager.StepTo(currentLogicalRow);
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

        /// <summary>
        /// 注册并分配时序表在当前注册中心内的整型位序号（0 ~ 63）。
        /// 若表名已存在则直接返回已有序号；若超过 64 张表则抛出强类型异常防御。
        /// </summary>
        /// <param name="tableName">时序表名称。</param>
        /// <returns>分配的序号（0 ~ 63）。</returns>
        public int RegisterTable(string tableName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
            if (_tableIndices.TryGetValue(tableName, out var existingIndex))
            {
                return existingIndex;
            }

            lock (_tableLock)
            {
                if (_tableIndices.TryGetValue(tableName, out var index))
                {
                    return index;
                }

                if (_tableNames.Count >= 64)
                {
                    throw new NotSupportedException($"时序表数量超过上限（最大支持 64 张表），当前无法注册 '{tableName}'。");
                }

                index = _tableNames.Count;
                _tableNames.Add(tableName);
                _tableIndices[tableName] = index;
                return index;
            }
        }

        /// <inheritdoc />
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool IsUpdated(int tableIndex)
        {
            if ((uint)tableIndex >= 64) return false;
            return (System.Threading.Volatile.Read(ref _updatedMask) & (1UL << tableIndex)) != 0;
        }

        /// <inheritdoc />
        public bool IsUpdated(string tableName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
            if (!_tableIndices.TryGetValue(tableName, out var index))
            {
                throw new KeyNotFoundException($"时序表 '{tableName}' 未注册。请确认表名拼写或预先注册该表。");
            }
            return IsUpdated(index);
        }

        /// <inheritdoc />
        public int GetTableIndex(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return -1;
            return _tableIndices.TryGetValue(tableName, out var index) ? index : -1;
        }

        /// <summary>
        /// 设置当前时间步的时序变更位掩码。由引擎在每步推进时统一刷新。
        /// </summary>
        /// <param name="mask">64 位变更掩码。</param>
        public void SetUpdatedMask(ulong mask)
        {
            lock (_tableLock)
            {
                System.Threading.Volatile.Write(ref _updatedMask, mask);
            }
        }

        /// <summary>
        /// 设置指定序号时序表在当前时间步的更新状态。
        /// </summary>
        /// <param name="tableIndex">表序号（0 ~ 63）。</param>
        /// <param name="isUpdated">是否更新。</param>
        public void SetUpdated(int tableIndex, bool isUpdated)
        {
            if ((uint)tableIndex >= 64)
            {
                throw new ArgumentOutOfRangeException(nameof(tableIndex), "tableIndex 必须在 0 到 63 之间。");
            }

            lock (_tableLock)
            {
                var mask = System.Threading.Volatile.Read(ref _updatedMask);
                if (isUpdated)
                {
                    mask |= (1UL << tableIndex);
                }
                else
                {
                    mask &= ~(1UL << tableIndex);
                }
                System.Threading.Volatile.Write(ref _updatedMask, mask);
            }
        }

        /// <summary>
        /// 设置指定名称时序表在当前时间步的更新状态。若表尚未分配序号则自动注册。
        /// </summary>
        public void SetUpdated(string tableName, bool isUpdated)
        {
            var idx = GetTableIndex(tableName);
            if (idx < 0) idx = RegisterTable(tableName);
            SetUpdated(idx, isUpdated);
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
