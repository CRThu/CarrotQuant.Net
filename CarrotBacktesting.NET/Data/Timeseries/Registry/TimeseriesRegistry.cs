using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序数据注册中心，统一管理主时序表与辅助时序子表，支持数据读取、分块调度与增量写入。
    /// </summary>
    public sealed class TimeseriesRegistry : ITimeseriesRegistry, ITimeseriesScheduler, ITimeseriesWriter, IDisposable
    {
        private readonly ITimeseriesTable _primaryTable;
        private readonly ConcurrentDictionary<string, ITimeseriesTable> _tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _tableIndices = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _tableNames = new();
        private readonly object _tableLock = new();
        private ITimeseriesScheduler[] _schedulers = Array.Empty<ITimeseriesScheduler>();
        private ITimeseriesWriter[] _writers = Array.Empty<ITimeseriesWriter>();
        private ITimeseriesTable[] _tableArray = Array.Empty<ITimeseriesTable>();
        private ulong _updatedMask = ulong.MaxValue;
        private bool _disposed;

        /// <inheritdoc />
        public ITimeseriesTable Primary => _primaryTable;

        /// <inheritdoc />
        public ITimeseriesTable this[string tableName] => GetTable(tableName);

        /// <summary>
        /// 基于主时序表与辅助子表列表构建时序注册中心。
        /// </summary>
        /// <param name="primaryTable">主时钟时序表。</param>
        /// <param name="subTables">可选的辅助时序子表列表。</param>
        public TimeseriesRegistry(ITimeseriesTable primaryTable, IEnumerable<ITimeseriesTable>? subTables = null)
        {
            ArgumentNullException.ThrowIfNull(primaryTable);
            _primaryTable = primaryTable;

            RegisterTableInternal(primaryTable.Name, primaryTable);

            if (subTables != null)
            {
                foreach (var sub in subTables)
                {
                    if (sub != null)
                    {
                        RegisterTableInternal(sub.Name, sub);
                    }
                }
            }
        }

        private void RegisterTableInternal(string tableName, ITimeseriesTable table)
        {
            _tables[tableName] = table;
            RegisterTable(tableName);
            lock (_tableLock)
            {
                var tables = _tables.Values.ToArray();
                _tableArray = tables;
                _schedulers = tables.OfType<ITimeseriesScheduler>().ToArray();
                _writers = tables.OfType<ITimeseriesWriter>().ToArray();
            }
        }

        /// <inheritdoc />
        public ITimeseriesTable GetTable(string tableName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
            if (_tables.TryGetValue(tableName, out var table))
            {
                return table;
            }
            throw new KeyNotFoundException($"未找到名称为 '{tableName}' 的时序表。已注册表: [{string.Join(", ", GetTableNames())}]。");
        }

        /// <inheritdoc />
        public bool HasTable(string tableName)
        {
            return !string.IsNullOrWhiteSpace(tableName) && _tables.ContainsKey(tableName);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetTableNames()
        {
            lock (_tableLock)
            {
                return _tableNames.ToArray();
            }
        }

        /// <summary>
        /// 获取指定字段的分块调度器实例（供缓存预热与分块加载管理使用）。
        /// 默认调度主时钟表对应字段的分块调度器。
        /// </summary>
        public ITimeseriesCacheManager GetCacheManager(string fieldName)
        {
            if (_primaryTable is TimeseriesTable primaryTyped)
            {
                return primaryTyped.GetCacheManager(fieldName);
            }

            throw new NotSupportedException($"当前主表类型 '{_primaryTable.GetType().Name}' 未公开分块调度器。");
        }

        /// <inheritdoc />
        public void AppendRow<T>(string fieldName, MarketTime timestamp, ReadOnlySpan<T> rowValues) where T : unmanaged
        {
            if (_primaryTable is ITimeseriesWriter writer)
            {
                writer.AppendRow(fieldName, timestamp, rowValues);
            }
            else
            {
                throw new NotSupportedException($"当前主时序表 '{_primaryTable.Name}' 未实现 ITimeseriesWriter，不支持行追加操作。");
            }
        }

        /// <inheritdoc />
        public void AppendCol(string symbol)
        {
            if (_primaryTable is ITimeseriesWriter writer)
            {
                writer.AppendCol(symbol);
            }
            else
            {
                throw new NotSupportedException($"当前主时序表 '{_primaryTable.Name}' 未实现 ITimeseriesWriter，不支持标的扩列操作。");
            }
        }

        /// <inheritdoc />
        public void StepTo(int currentLogicalRow)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var schedulers = _schedulers;
            for (int i = 0; i < schedulers.Length; i++)
            {
                schedulers[i].StepTo(currentLogicalRow);
            }
        }

        /// <inheritdoc />
        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var writers = _writers;
            for (int i = 0; i < writers.Length; i++)
            {
                writers[i].Commit();
            }
        }

        /// <summary>
        /// 注册并分配时序表在当前注册中心内的整型位序号（0 ~ 63）。
        /// 若表名已存在则直接返回已有序号；若超过 64 张表则抛出强类型异常防御。
        /// </summary>
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
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
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
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public int GetTableIndex(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return -1;
            return _tableIndices.TryGetValue(tableName, out var index) ? index : -1;
        }

        /// <summary>
        /// 设置当前时间步的时序变更位掩码。由引擎在每步推进时统一刷新。
        /// </summary>
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
            var tables = _tableArray;
            for (int i = 0; i < tables.Length; i++)
            {
                if (tables[i] is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _tables.Clear();
            _tableArray = Array.Empty<ITimeseriesTable>();
            _schedulers = Array.Empty<ITimeseriesScheduler>();
            _writers = Array.Empty<ITimeseriesWriter>();
        }
    }
}
