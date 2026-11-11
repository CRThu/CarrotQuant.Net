using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序连续分块管理器非泛型统一接口。
    /// 供上层 Provider 进行多字段统一调度、落盘与生命周期管理。
    /// </summary>
    public interface ITimeseriesChunkManager : IDisposable
    {
        /// <summary>
        /// 字段名称。
        /// </summary>
        string FieldName { get; }

        /// <summary>
        /// 存储的数据类型。
        /// </summary>
        Type DataType { get; }

        /// <summary>
        /// 当前矩阵已加载或已追加的总行数。
        /// </summary>
        int TotalRows { get; }

        /// <summary>
        /// 当前末尾活跃分块的页索引（0-based）。
        /// </summary>
        int ActivePageIndex { get; }

        /// <summary>
        /// 当前末尾活跃分块内部的有效行数。
        /// </summary>
        int ActivePageRowCount { get; }

        /// <summary>
        /// 内存数据是否包含尚未持久化落盘的增量修改。
        /// </summary>
        bool IsDirty { get; }

        /// <summary>
        /// 内存中暂存未持久化落盘的增量行数。
        /// </summary>
        int UncommittedRowCount { get; }

        /// <summary>
        /// 手动标记当前分块处于脏状态。
        /// </summary>
        void MarkDirty();

        /// <summary>
        /// 新标的挂牌扩列（因果顺序：先结清未落盘行，再拓宽列并提交元数据）。
        /// </summary>
        /// <param name="symbol">新增标的代码。</param>
        void AppendCol(string symbol);

        /// <summary>
        /// 将内存中修改或新增的时序分块增量同步落盘至持久化存储 <see cref="ITimeseriesStore"/>。
        /// 仅刷写活跃分块，历史分块物理零触碰。
        /// </summary>
        void Commit();
    }

    /// <summary>
    /// 单表单字段时序连续分块（Multi-Chunk）状态机与生命周期调度器。
    /// 统一管理非托管内存矩阵 <see cref="ChunkMatrix2D{T}"/> 与磁盘物理持久化存储 <see cref="ITimeseriesStore"/> 之间的连续挂载、
    /// 股票池列拓宽 (NaN填充)、实盘分块轮转 (Rollover) 与精准增量落盘 (Commit)。
    /// </summary>
    /// <typeparam name="T">非托管数值类型。</typeparam>
    public sealed class TimeseriesChunkManager<T> : ITimeseriesChunkManager where T : unmanaged
    {
        private readonly string _fieldName;
        private readonly IMarketMetadata _metadata;
        private readonly IMarketSeriesSource _source;
        private readonly ITimeseriesStore? _store;
        private readonly int _pageSize;
        private readonly string _refreshMode;
        private readonly int _commitBatchSize;

        private ChunkMatrix2D<T>? _matrix;
        private int _totalRows;
        private int _committedRowCount;
        private readonly List<DateTime> _uncommittedDates = new();
        private bool _isDirty;
        private bool _disposed;
        private readonly object _gate = new();

        /// <inheritdoc />
        public string FieldName => _fieldName;

        /// <inheritdoc />
        public Type DataType => typeof(T);

        /// <inheritdoc />
        public int TotalRows
        {
            get
            {
                EnsureLoaded();
                return _totalRows;
            }
        }

        /// <inheritdoc />
        public int ActivePageIndex
        {
            get
            {
                EnsureLoaded();
                return _totalRows > 0 ? (_totalRows - 1) / _pageSize : 0;
            }
        }

        /// <inheritdoc />
        public int ActivePageRowCount
        {
            get
            {
                EnsureLoaded();
                return _totalRows > 0 ? (_totalRows - 1) % _pageSize + 1 : 0;
            }
        }

        /// <inheritdoc />
        public bool IsDirty => _isDirty;

        /// <inheritdoc />
        public int UncommittedRowCount
        {
            get
            {
                lock (_gate)
                {
                    return _uncommittedDates.Count;
                }
            }
        }

        /// <summary>
        /// 初始化 <see cref="TimeseriesChunkManager{T}"/> 实例（默认定量 64 行自动提交）。
        /// </summary>
        public TimeseriesChunkManager(
            string fieldName,
            IMarketMetadata metadata,
            IMarketSeriesSource source,
            ITimeseriesStore? store,
            int pageSize = 4096,
            string refreshMode = "lazy")
            : this(fieldName, metadata, source, store, pageSize, refreshMode, 64)
        {
        }

        /// <summary>
        /// 初始化 <see cref="TimeseriesChunkManager{T}"/> 实例。
        /// </summary>
        public TimeseriesChunkManager(
            string fieldName,
            IMarketMetadata metadata,
            IMarketSeriesSource source,
            ITimeseriesStore? store,
            int pageSize,
            string refreshMode,
            int commitBatchSize)
        {
            _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
            ArgumentNullException.ThrowIfNull(metadata);
            _metadata = metadata is DynamicMarketMetadata dynamicMeta
                ? dynamicMeta
                : new DynamicMarketMetadata(metadata);
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _store = store;
            _pageSize = pageSize;
            _refreshMode = refreshMode;
            _commitBatchSize = commitBatchSize > 0 ? commitBatchSize : 64;
        }

        /// <summary>
        /// 获取当前字段时序矩阵的只读视图接口。
        /// </summary>
        public IReadOnlyMatrix2D<T> AsReadOnly()
        {
            EnsureLoaded();
            return _matrix!.AsReadOnly();
        }

        /// <summary>
        /// 获取底层内存矩阵实例。
        /// </summary>
        public ChunkMatrix2D<T> Matrix
        {
            get
            {
                EnsureLoaded();
                return _matrix!;
            }
        }

        /// <summary>
        /// 实盘单行/单截面数据追加（纯内存高频通道）：记录真实时间戳，自动处理跨页分块轮转 (Chunk Rollover)。
        /// 达到定量阈值（默认 64 行）时自动触发一次批量增量落盘。
        /// </summary>
        /// <param name="timestamp">当前时间步物理时间戳。</param>
        /// <param name="rowValues">当日/当前时间步全市场个股截面数据，长度必须等于代码总数。</param>
        public void AppendRow(DateTime timestamp, ReadOnlySpan<T> rowValues)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();

            if (rowValues.Length != _metadata.Symbols.Count)
            {
                throw new ArgumentException($"Row values length ({rowValues.Length}) does not match symbols count ({_metadata.Symbols.Count}).");
            }

            bool shouldCommit = false;
            lock (_gate)
            {
                // 分块轮转 (Rollover)：若当前总行数达到容器物理容量，自动扩容开辟新内存页
                if (_totalRows >= _matrix!.RowCount)
                {
                    _matrix.EnsureCapacity(_totalRows + 1);
                }

                _matrix.SetRow(_totalRows, 0, rowValues);
                _uncommittedDates.Add(timestamp);
                _metadata.AppendDate(timestamp);
                _totalRows++;
                _isDirty = true;

                if (_uncommittedDates.Count >= _commitBatchSize)
                {
                    shouldCommit = true;
                }
            }

            if (shouldCommit)
            {
                Commit();
            }
        }

        /// <summary>
        /// 兼容旧签名的重载（若调用方未传时间戳，按元数据已有时间或当前时间步顺延）。
        /// </summary>
        public void AppendRow(ReadOnlySpan<T> rowValues)
        {
            DateTime date = _totalRows < _metadata.TradeDates.Count
                ? _metadata.TradeDates[_totalRows]
                : (_totalRows > 0 ? _metadata.TradeDates[^1].AddDays(1) : DateTime.Today);
            AppendRow(date, rowValues);
        }

        /// <inheritdoc />
        public void AppendCol(string symbol)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

            lock (_gate)
            {
                // 1. 因果守恒：若当前有积攒未提交的行，先以旧宽度彻底结清落盘！
                if (_uncommittedDates.Count > 0)
                {
                    Commit();
                }

                // 2. 元数据追加新标的代码
                int newColIndex = _metadata.AppendSymbol(symbol);
                int newWidth = _metadata.Symbols.Count;

                // 3. 内存矩阵拓宽，新列的历史行全部自动填入 NaN
                if (newWidth > _matrix!.Width)
                {
                    _matrix.ExpandWidth(newWidth);
                }

                // 4. 更新活跃分块与持久化元数据中的列维度（Scheme B 自包含分块宽度）
                if (_store != null && _totalRows > 0)
                {
                    int activePageIndex = ActivePageIndex;
                    _store.RewriteChunk<T>(
                        _fieldName,
                        activePageIndex,
                        (globalRow, destRow) => _matrix.CopyRow(globalRow, 0, destRow),
                        newChunkWidth: newWidth,
                        updatedSymbols: _metadata.Symbols);
                }

                _isDirty = false;
            }
        }

        /// <summary>
        /// 更新指定行的截面数据并标记脏状态。
        /// </summary>
        /// <param name="rowIndex">目标行索引。</param>
        /// <param name="rowValues">更新的截面数据，长度必须等于代码总数。</param>
        public void UpdateRow(int rowIndex, ReadOnlySpan<T> rowValues)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();

            if (rowValues.Length != _metadata.Symbols.Count)
            {
                throw new ArgumentException($"Row values length ({rowValues.Length}) does not match symbols count ({_metadata.Symbols.Count}).");
            }
            if ((uint)rowIndex >= (uint)_totalRows)
            {
                throw new ArgumentOutOfRangeException(nameof(rowIndex), "Row index out of range.");
            }

            lock (_gate)
            {
                _matrix!.SetRow(rowIndex, 0, rowValues);
                _isDirty = true;
            }
        }

        /// <inheritdoc />
        public void MarkDirty()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _isDirty = true;
        }

        /// <inheritdoc />
        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_isDirty || _store == null || _matrix == null || _totalRows <= 0) return;

            lock (_gate)
            {
                if (!_isDirty) return;

                int deltaRows = _uncommittedDates.Count;
                if (deltaRows > 0)
                {
                    // 1. 批量增量追加具有物理真实时间戳的新行到持久化分块
                    var datesToCommit = _uncommittedDates.ToArray();
                    _store.AppendRows<T>(
                        _fieldName,
                        deltaRows,
                        (globalRow, destRow) => _matrix.CopyRow(globalRow, 0, destRow),
                        datesToCommit);

                    _committedRowCount = _totalRows;
                    _uncommittedDates.Clear();
                }
                else
                {
                    // 2. 行数未增加，仅活跃分块内部微调或列拓宽元数据刷新
                    int activePageIndex = ActivePageIndex;
                    _store.RewriteChunk<T>(
                        _fieldName,
                        activePageIndex,
                        (globalRow, destRow) => _matrix.CopyRow(globalRow, 0, destRow),
                        newChunkWidth: _matrix.Width,
                        updatedSymbols: _metadata.Symbols);
                }

                _isDirty = false;
            }
        }

        /// <summary>
        /// 确保该字段的时序数据已完成连续多块挂载或全量加载。
        /// </summary>
        public void EnsureLoaded()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_matrix != null) return;

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_matrix != null) return;
                LoadBufferInternal();
            }
        }

        private void LoadBufferInternal()
        {
            int totalDays = _metadata.TradeDates.Count;
            if (totalDays <= 0) throw new InvalidOperationException("Metadata TradeDates count must be > 0.");

            if (_store != null)
            {
                if (string.Equals(_refreshMode, "force", StringComparison.OrdinalIgnoreCase))
                {
                    _store.Clear(_fieldName);
                    var mat = LoadFromSource(totalDays);
                    _store.WriteMatrix(_fieldName, mat, _metadata.Symbols, _metadata.TradeDates);
                    _matrix = mat;
                    _totalRows = totalDays;
                    _committedRowCount = totalDays;
                    _uncommittedDates.Clear();
                    _isDirty = false;
                    return;
                }
                else if (string.Equals(_refreshMode, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    if (_store.IsCompatible(_fieldName, _metadata.Symbols, typeof(T), out var meta))
                    {
                        if (meta!.RowCount == totalDays)
                        {
                            if (_metadata.Symbols.Count > meta.Symbols.Count)
                            {
                                RefreshActiveChunkForExpandedSymbols(meta);
                            }

                            var mat = _store.LoadMatrix<T>(_fieldName, _metadata.Symbols.Count);
                            _matrix = mat;
                            _totalRows = mat.RowCount;
                            _committedRowCount = mat.RowCount;
                            _uncommittedDates.Clear();
                            _isDirty = false;
                            return;
                        }
                    }

                    var fallbackMat = LoadFromSource(totalDays);
                    _store.WriteMatrix(_fieldName, fallbackMat, _metadata.Symbols, _metadata.TradeDates);
                    _matrix = fallbackMat;
                    _totalRows = totalDays;
                    _committedRowCount = totalDays;
                    _uncommittedDates.Clear();
                    _isDirty = false;
                    return;
                }
                else // lazy
                {
                    if (_store.IsCompatible(_fieldName, _metadata.Symbols, typeof(T), out var meta))
                    {
                        try
                        {
                            if (_metadata.Symbols.Count > meta!.Symbols.Count)
                            {
                                RefreshActiveChunkForExpandedSymbols(meta);
                            }

                            var mat = _store.LoadMatrix<T>(_fieldName, _metadata.Symbols.Count);
                            _matrix = mat;
                            _totalRows = mat.RowCount;
                            _committedRowCount = mat.RowCount;
                            _uncommittedDates.Clear();
                            _isDirty = false;
                            return;
                        }
                        catch
                        {
                            // 持久化存储文件损坏或异常时回退全量重建
                        }
                    }

                    var fallbackMat = LoadFromSource(totalDays);
                    _store.WriteMatrix(_fieldName, fallbackMat, _metadata.Symbols, _metadata.TradeDates);
                    _matrix = fallbackMat;
                    _totalRows = totalDays;
                    _committedRowCount = totalDays;
                    _uncommittedDates.Clear();
                    _isDirty = false;
                    return;
                }
            }

            // 无持久化存储模式，直接从原始数据源加载
            var directMat = LoadFromSource(totalDays);
            _matrix = directMat;
            _totalRows = totalDays;
            _committedRowCount = totalDays;
            _uncommittedDates.Clear();
            _isDirty = false;
        }

        private void RefreshActiveChunkForExpandedSymbols(TimeseriesMetadata meta)
        {
            if (_store == null || meta.RowCount <= 0) return;

            int activePageIndex = (meta.RowCount - 1) / _pageSize;
            int startRow = activePageIndex * _pageSize;
            int activeRows = Math.Min(_pageSize, meta.RowCount - startRow);
            int newWidth = _metadata.Symbols.Count;

            T[][] activeData = new T[newWidth][];
            try
            {
                for (int c = 0; c < newWidth; c++)
                {
                    activeData[c] = ArrayPool<T>.Shared.Rent(activeRows);
                    _source.ReadSymbolSeries(_metadata.Symbols[c], _fieldName, startRow, activeRows, activeData[c].AsSpan(0, activeRows));
                }

                _store.RewriteChunk<T>(
                    _fieldName,
                    activePageIndex,
                    (globalRow, destRow) =>
                    {
                        int r = globalRow - startRow;
                        for (int c = 0; c < newWidth; c++)
                        {
                            destRow[c] = activeData[c][r];
                        }
                    },
                    newChunkWidth: newWidth,
                    updatedSymbols: _metadata.Symbols);
            }
            finally
            {
                for (int c = 0; c < newWidth; c++)
                {
                    if (activeData[c] != null)
                    {
                        ArrayPool<T>.Shared.Return(activeData[c]);
                    }
                }
            }
        }

        private ChunkMatrix2D<T> LoadFromSource(int totalDays)
        {
            var matrix = new ChunkMatrix2D<T>(_metadata.Symbols.Count, _pageSize, totalDays);

            T[] temp = ArrayPool<T>.Shared.Rent(totalDays);
            try
            {
                for (int colIdx = 0; colIdx < _metadata.Symbols.Count; colIdx++)
                {
                    string symbol = _metadata.Symbols[colIdx];
                    _source.ReadSymbolSeries(symbol, _fieldName, 0, totalDays, temp.AsSpan(0, totalDays));
                    matrix.SetColumn(0, colIdx, temp.AsSpan(0, totalDays));
                }
            }
            finally
            {
                ArrayPool<T>.Shared.Return(temp);
            }

            return matrix;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            lock (_gate)
            {
                if (_disposed) return;

                // 退出时保底提交：将残留未满 64 行的数据强制持久化落盘
                try
                {
                    if (_isDirty && _uncommittedDates.Count > 0)
                    {
                        Commit();
                    }
                }
                catch
                {
                    // 防御性释放
                }

                _matrix?.Dispose();
                _matrix = null;
                _disposed = true;
            }
        }
    }
}
