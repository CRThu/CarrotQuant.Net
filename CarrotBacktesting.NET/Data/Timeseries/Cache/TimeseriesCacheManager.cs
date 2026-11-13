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
    public interface ITimeseriesCacheManager : IDisposable
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
        /// 最大常驻内存页数（0 表示不设上限全量常驻）。
        /// </summary>
        int MaxPages { get; }

        /// <summary>
        /// 当前常驻内存窗口的最老有效基准行号。
        /// </summary>
        int BaseRowIndex { get; }

        /// <summary>
        /// 时间步步进通知：告知管理器当前引擎已推进至第 <paramref name="currentLogicalRow"/> 行。
        /// 触发后台自动异步预读（Prefetch）并推进常驻窗口基准行（淘汰冷分块）。
        /// </summary>
        void StepTo(int currentLogicalRow);

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
        /// 将内存中修改或新增的时序分块增量同步落盘至持久化存储 <see cref="ITimeseriesCache"/>。
        /// 仅刷写活跃分块，历史分块物理零触碰。
        /// </summary>
        void Commit();
    }

    /// <summary>
    /// 单表单字段时序连续分块（Multi-Chunk）状态机与生命周期调度器。
    /// 统一管理非托管内存矩阵 <see cref="ChunkMatrix2D{T}"/> 与磁盘物理持久化存储 <see cref="ITimeseriesCache"/> 之间的连续挂载、
    /// 股票池列拓宽 (NaN填充)、实盘分块轮转 (Rollover) 与精准增量落盘 (Commit)。
    /// </summary>
    /// <typeparam name="T">非托管数值类型。</typeparam>
    public sealed class TimeseriesCacheManager<T> : ITimeseriesCacheManager where T : unmanaged
    {
        private readonly string _fieldName;
        private readonly IMarketMetadata _metadata;
        private readonly ITimeseriesSource _source;
        private readonly ITimeseriesCache? _store;
        private readonly int _pageSize;
        private readonly string _refreshMode;
        private readonly int _commitBatchSize;
        private readonly int _maxPages;
        private readonly bool _prefetch;
        private readonly bool _isLive;
        private readonly bool _canPrefetch;

        private ChunkMatrix2D<T>? _matrix;
        private int _totalRows;
        private int _committedRowCount;
        private readonly List<DateTime> _uncommittedDates = new();
        private bool _isDirty;
        private bool _disposed;
        private readonly object _gate = new();

        private Task<T[]>? _prefetchTask;
        private int _prefetchedPageIndex = -1;
        private int _loadedMaxPageIndex = -1;

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
        public int MaxPages => _maxPages;

        /// <summary>
        /// 是否允许异步预读（回测且未显式禁用时为 true；实盘 Living 下强制为 false）。
        /// </summary>
        public bool CanPrefetch => _canPrefetch;

        /// <summary>
        /// 是否运行于实盘（Living）模式。
        /// </summary>
        public bool IsLive => _isLive;

        /// <inheritdoc />
        public int BaseRowIndex
        {
            get
            {
                EnsureLoaded();
                return _matrix!.BaseRowIndex;
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
        /// 初始化 <see cref="TimeseriesCacheManager{T}"/> 实例（默认定量 64 行自动提交，默认 4 个常驻分块）。
        /// </summary>
        public TimeseriesCacheManager(
            string fieldName,
            IMarketMetadata metadata,
            ITimeseriesSource source,
            ITimeseriesCache? store,
            int pageSize = 4096,
            string refreshMode = "lazy")
            : this(fieldName, metadata, source, store, pageSize, refreshMode, 64, 4)
        {
        }

        /// <summary>
        /// 初始化 <see cref="TimeseriesCacheManager{T}"/> 实例。
        /// </summary>
        public TimeseriesCacheManager(
            string fieldName,
            IMarketMetadata metadata,
            ITimeseriesSource source,
            ITimeseriesCache? store,
            int pageSize,
            string refreshMode,
            int commitBatchSize)
            : this(fieldName, metadata, source, store, pageSize, refreshMode, commitBatchSize, 4)
        {
        }

        /// <summary>
        /// 初始化 <see cref="TimeseriesCacheManager{T}"/> 实例（包含常驻内存页数、预读与实盘模式配置）。
        /// </summary>
        public TimeseriesCacheManager(
            string fieldName,
            IMarketMetadata metadata,
            ITimeseriesSource source,
            ITimeseriesCache? store,
            int pageSize,
            string refreshMode,
            int commitBatchSize,
            int maxPages = 4,
            bool prefetch = true,
            bool isLive = false)
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
            _maxPages = maxPages > 0 ? maxPages : 4;
            _prefetch = prefetch;
            _isLive = isLive;
            _canPrefetch = prefetch && !isLive;
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

        /// <inheritdoc />
        public void StepTo(int currentLogicalRow)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();

            if (currentLogicalRow < 0) return;

            int targetPage = currentLogicalRow / _pageSize;
            int rowInPage = currentLogicalRow % _pageSize;

            // 1. 若当前步进入了尚未挂接但已存在的分块（跨页推进，防跨步跳跃）
            if (targetPage > _loadedMaxPageIndex && _matrix != null && _totalRows > 0)
            {
                lock (_gate)
                {
                    while (_loadedMaxPageIndex < targetPage && (_loadedMaxPageIndex + 1) * _pageSize < _totalRows)
                    {
                        int pageToMount = _loadedMaxPageIndex + 1;
                        MountTargetPage(pageToMount);
                    }
                }
            }

            // 2. 检查是否达到预读阈值（后 25%），自动触发后台异步预读下一块（实盘与禁用预读时物理熔断）
            if (_canPrefetch && _maxPages > 0 && rowInPage >= (_pageSize * 3 / 4))
            {
                int nextPrefetchPage = targetPage + 1;
                int totalPages = (_totalRows + _pageSize - 1) / _pageSize;

                if (nextPrefetchPage < totalPages && nextPrefetchPage != _prefetchedPageIndex && _prefetchTask == null)
                {
                    lock (_gate)
                    {
                        if (nextPrefetchPage < totalPages && nextPrefetchPage != _prefetchedPageIndex && _prefetchTask == null)
                        {
                            _prefetchedPageIndex = nextPrefetchPage;
                            _prefetchTask = Task.Run(() => LoadChunkData(nextPrefetchPage));
                        }
                    }
                }
            }
        }

        private void MountTargetPage(int targetPage)
        {
            T[]? pageData = null;
            if (_prefetchedPageIndex == targetPage && _prefetchTask != null)
            {
                pageData = _prefetchTask.GetAwaiter().GetResult();
                _prefetchTask = null;
                _prefetchedPageIndex = -1;
            }
            else
            {
                pageData = LoadChunkData(targetPage);
            }

            int startRow = targetPage * _pageSize;
            int rowsInChunk = Math.Min(_pageSize, _totalRows - startRow);
            int width = _metadata.Symbols.Count;

            _matrix!.EnsureCapacity((targetPage + 1) * _pageSize);
            for (int r = 0; r < rowsInChunk; r++)
            {
                _matrix.SetRow(startRow + r, 0, pageData.AsSpan(r * width, width));
            }

            _loadedMaxPageIndex = targetPage;
        }

        private T[] LoadChunkData(int pageIndex)
        {
            int startRow = pageIndex * _pageSize;
            int rowsInChunk = Math.Min(_pageSize, _totalRows - startRow);
            int width = _metadata.Symbols.Count;
            T[] buffer = new T[rowsInChunk * width];

            // 1. 优先快路径：从 TimeseriesCache 直接读取二进制压缩分块
            if (_store != null && _store.TryReadChunk<T>(_fieldName, pageIndex, buffer.AsSpan(), out int rowsRead) && rowsRead == rowsInChunk)
            {
                return buffer;
            }

            // 2. 回退慢路径：从 ITimeseriesSource 逐股票流式读取
            T[] temp = ArrayPool<T>.Shared.Rent(rowsInChunk);
            try
            {
                for (int c = 0; c < width; c++)
                {
                    string sym = _metadata.Symbols[c];
                    _source.ReadSymbolSeries(sym, _fieldName, startRow, rowsInChunk, temp.AsSpan(0, rowsInChunk));
                    for (int r = 0; r < rowsInChunk; r++)
                    {
                        buffer[r * width + c] = temp[r];
                    }
                }
            }
            finally
            {
                ArrayPool<T>.Shared.Return(temp);
            }

            return buffer;
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

                // 满 64 行定时落盘，或跨越整页边界时保底落盘
                if (_uncommittedDates.Count >= _commitBatchSize || (_totalRows % _pageSize == 0))
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
        /// 便捷追加截面数据（自动依据元数据交易日历按当前行步长顺延时间戳）。
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
                    using (var fullMat = LoadFromSource(totalDays))
                    {
                        _store.WriteMatrix(_fieldName, fullMat, _metadata.Symbols, _metadata.TradeDates);
                    }
                    _matrix = InitializeMatrix(totalDays);
                    _committedRowCount = totalDays;
                    _uncommittedDates.Clear();
                    _isDirty = false;
                    return;
                }
                else if (string.Equals(_refreshMode, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    if (_store.IsCompatible(_fieldName, _metadata.Symbols, typeof(T), out var meta))
                    {
                        if (meta!.RowCount >= totalDays)
                        {
                            if (_metadata.Symbols.Count > meta.Symbols.Count)
                            {
                                RefreshActiveChunkForExpandedSymbols(meta);
                            }

                            int effectiveRows = Math.Max(totalDays, meta.RowCount);
                            _matrix = InitializeMatrix(effectiveRows);
                            _committedRowCount = effectiveRows;
                            _uncommittedDates.Clear();
                            _isDirty = false;
                            return;
                        }
                    }

                    using (var fallbackMat = LoadFromSource(totalDays))
                    {
                        _store.WriteMatrix(_fieldName, fallbackMat, _metadata.Symbols, _metadata.TradeDates);
                    }
                    _matrix = InitializeMatrix(totalDays);
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

                            int effectiveRows = Math.Max(totalDays, meta!.RowCount);
                            _matrix = InitializeMatrix(effectiveRows);
                            _committedRowCount = effectiveRows;
                            _uncommittedDates.Clear();
                            _isDirty = false;
                            return;
                        }
                        catch
                        {
                            // 持久化存储文件损坏或异常时回退全量重建
                        }
                    }

                    using (var fallbackMat = LoadFromSource(totalDays))
                    {
                        _store.WriteMatrix(_fieldName, fallbackMat, _metadata.Symbols, _metadata.TradeDates);
                    }
                    _matrix = InitializeMatrix(totalDays);
                    _committedRowCount = totalDays;
                    _uncommittedDates.Clear();
                    _isDirty = false;
                    return;
                }
            }

            // 无持久化存储模式，直接从原始数据源加载
            _matrix = InitializeMatrix(totalDays);
            _committedRowCount = totalDays;
            _uncommittedDates.Clear();
            _isDirty = false;
        }

        private ChunkMatrix2D<T> InitializeMatrix(int totalRows)
        {
            _totalRows = totalRows;
            int totalPages = (totalRows + _pageSize - 1) / _pageSize;
            int initialPages = (_maxPages > 0 && totalPages > _maxPages) 
                ? _maxPages 
                : totalPages;
            int initialRows = Math.Min(totalRows, initialPages * _pageSize);

            var matrix = new ChunkMatrix2D<T>(_metadata.Symbols.Count, _pageSize, initialRows, _maxPages);
            int width = _metadata.Symbols.Count;

            for (int p = 0; p < initialPages; p++)
            {
                T[] chunkData = LoadChunkData(p);
                int startRow = p * _pageSize;
                int rowsInChunk = Math.Min(_pageSize, totalRows - startRow);

                for (int r = 0; r < rowsInChunk; r++)
                {
                    matrix.SetRow(startRow + r, 0, chunkData.AsSpan(r * width, width));
                }
            }

            _loadedMaxPageIndex = initialPages - 1;
            return matrix;
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
            var matrix = new ChunkMatrix2D<T>(_metadata.Symbols.Count, _pageSize, totalDays, maxPages: 0);

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
                    if (_prefetchTask != null)
                    {
                        try { _prefetchTask.Wait(500); } catch { }
                        _prefetchTask = null;
                    }

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
