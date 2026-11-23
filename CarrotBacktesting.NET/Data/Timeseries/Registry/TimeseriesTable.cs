using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序表实现类，管理单一时序表的数据源、字段分块调度器及与主时钟的行映射。
    /// </summary>
    public sealed class TimeseriesTable : ITimeseriesTable, ITimeseriesScheduler, ITimeseriesWriter, IDisposable
    {
        private readonly IMarketMetadata _metadata;
        private readonly Func<string, Type, ITimeseriesCacheManager> _managerFactory;
        private readonly ConcurrentDictionary<string, ITimeseriesCacheManager> _managers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, object> _matrixCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _fieldSet;
        private readonly int[]? _subRowMap;
        private readonly IDisposable? _disposableSource;
        private readonly object _managerLock = new();
        private ITimeseriesCacheManager[] _managerArray = Array.Empty<ITimeseriesCacheManager>();
        private bool _disposed;

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public bool IsPrimary { get; }

        /// <inheritdoc />
        public IMarketMetadata Metadata => _metadata;

        /// <summary>
        /// 构建时序表实例。
        /// </summary>
        /// <param name="name">表名称。</param>
        /// <param name="isPrimary">是否为主时钟表。</param>
        /// <param name="metadata">市场元数据。</param>
        /// <param name="managerFactory">分块调度器解析工厂委托。</param>
        /// <param name="subRowMap">对应主时序时间轴的 As-Of 映射数组；主表自身传 null。</param>
        /// <param name="disposableSource">底层需随表释放的资源句柄（可选）。</param>
        public TimeseriesTable(
            string name,
            bool isPrimary,
            IMarketMetadata metadata,
            Func<string, Type, ITimeseriesCacheManager> managerFactory,
            int[]? subRowMap = null,
            IDisposable? disposableSource = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(managerFactory);

            Name = name;
            IsPrimary = isPrimary;
            _metadata = metadata is DynamicMarketMetadata dynamicMeta
                ? dynamicMeta
                : new DynamicMarketMetadata(metadata);
            _fieldSet = new HashSet<string>(_metadata.FieldNames, StringComparer.OrdinalIgnoreCase);
            _managerFactory = managerFactory;
            _subRowMap = subRowMap;
            _disposableSource = disposableSource;
        }

        /// <summary>
        /// 检查当前时序表是否包含指定字段。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasField(string fieldName)
        {
            return !string.IsNullOrEmpty(fieldName) && _fieldSet.Contains(fieldName);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_matrixCache.TryGetValue(fieldName, out var cached))
            {
                return (IReadOnlyMatrix2D<T>)cached;
            }

            return GetMatrixSlow<T>(fieldName);
        }

        private IReadOnlyMatrix2D<T> GetMatrixSlow<T>(string fieldName) where T : unmanaged
        {
            if (!HasField(fieldName))
            {
                throw new KeyNotFoundException($"时序表 '{Name}' 中未找到字段 '{fieldName}'。");
            }

            var fieldType = _metadata.GetFieldType(fieldName);
            if (fieldType != typeof(T))
            {
                throw new InvalidOperationException(
                    $"时序表 '{Name}' 字段 '{fieldName}' 请求类型不匹配（字段物理类型: {fieldType.Name}，请求类型: {typeof(T).Name}）。");
            }

            var manager = (TimeseriesCacheManager<T>)_managers.GetOrAdd(fieldName, name => _managerFactory(name, typeof(T)));
            UpdateManagerArray();
            var matrix = manager.AsReadOnly();
            _matrixCache[fieldName] = matrix;
            return matrix;
        }

        /// <summary>
        /// 获取指定字段的分块调度器实例。
        /// </summary>
        public ITimeseriesCacheManager GetCacheManager(string fieldName)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!HasField(fieldName))
            {
                throw new KeyNotFoundException($"时序表 '{Name}' 中未找到字段 '{fieldName}'。");
            }

            var fieldType = _metadata.GetFieldType(fieldName);
            var manager = _managers.GetOrAdd(fieldName, name => _managerFactory(name, fieldType));
            UpdateManagerArray();
            return manager;
        }

        private void UpdateManagerArray()
        {
            lock (_managerLock)
            {
                _managerArray = _managers.Values.ToArray();
            }
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetCurrentRowIndex(int primaryStepIndex)
        {
            if (IsPrimary)
            {
                return primaryStepIndex;
            }

            if (_subRowMap == null || (uint)primaryStepIndex >= (uint)_subRowMap.Length)
            {
                return -1;
            }

            return _subRowMap[primaryStepIndex];
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> GetCurrentRow<T>(string fieldName, int primaryStepIndex) where T : unmanaged
        {
            int rowIdx = GetCurrentRowIndex(primaryStepIndex);
            if (rowIdx < 0) return ReadOnlySpan<T>.Empty;

            var matrix = GetMatrix<T>(fieldName);
            return (uint)rowIdx < (uint)matrix.RowCount ? matrix.GetRowView(rowIdx, 0, matrix.Width) : ReadOnlySpan<T>.Empty;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetCurrentValue<T>(string fieldName, int symbolIndex, int primaryStepIndex) where T : unmanaged
        {
            int rowIdx = GetCurrentRowIndex(primaryStepIndex);
            if (rowIdx < 0 || symbolIndex < 0)
            {
                return MarketDataAligner.GetNaNValue<T>();
            }

            var matrix = GetMatrix<T>(fieldName);
            if ((uint)rowIdx >= (uint)matrix.RowCount || (uint)symbolIndex >= (uint)matrix.Width)
            {
                return MarketDataAligner.GetNaNValue<T>();
            }

            return matrix[rowIdx, symbolIndex];
        }

        /// <inheritdoc />
        public void StepTo(int stepIndex)
        {
            int targetRow = GetCurrentRowIndex(stepIndex);
            if (targetRow < 0) return;

            var managers = _managerArray;
            for (int i = 0; i < managers.Length; i++)
            {
                managers[i].StepTo(targetRow);
            }
        }

        /// <inheritdoc />
        public void AppendRow<T>(string fieldName, MarketTime timestamp, ReadOnlySpan<T> rowValues) where T : unmanaged
        {
            var fieldType = _metadata.GetFieldType(fieldName);
            if (fieldType != typeof(T))
            {
                throw new InvalidOperationException($"时序表 '{Name}' 字段 '{fieldName}' 类型不匹配。");
            }

            var manager = (TimeseriesCacheManager<T>)GetCacheManager(fieldName);
            manager.AppendRow(timestamp, rowValues);
        }

        /// <inheritdoc />
        public void AppendCol(string symbol)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_metadata is IMutableMarketMetadata mutableMeta)
            {
                mutableMeta.AppendSymbol(symbol);
            }
            else
            {
                throw new NotSupportedException($"时序表 '{Name}' 元数据不支持动态拓宽标的代码。");
            }

            var managers = _managerArray;
            for (int i = 0; i < managers.Length; i++)
            {
                managers[i].AppendCol(symbol);
            }
        }

        /// <inheritdoc />
        public void Commit()
        {
            var managers = _managerArray;
            for (int i = 0; i < managers.Length; i++)
            {
                managers[i].Commit();
            }
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var managers = _managerArray;
            for (int i = 0; i < managers.Length; i++)
            {
                if (managers[i] is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _managers.Clear();
            _matrixCache.Clear();
            _managerArray = Array.Empty<ITimeseriesCacheManager>();
            _disposableSource?.Dispose();
        }
    }
}
