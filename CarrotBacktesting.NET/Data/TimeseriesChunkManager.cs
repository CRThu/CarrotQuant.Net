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
        /// 手动标记当前分块处于脏状态。
        /// </summary>
        void MarkDirty();

        /// <summary>
        /// 将内存中修改或新增的时序分块增量同步落盘至 TimeseriesCacheStore。
        /// 仅刷写活跃分块，历史分块物理零触碰。
        /// </summary>
        void Commit();
    }

    /// <summary>
    /// 单表单字段时序连续分块（Multi-Chunk）状态机与生命周期调度器。
    /// 统一管理非托管内存容器 <see cref="ChunkBuffer2D{T}"/> 与磁盘物理缓存 <see cref="TimeseriesCacheStore"/> 之间的连续挂载、
    /// 股票池列拓宽 (NaN填充)、实盘分块轮转 (Rollover) 与精准增量落盘 (Commit)。
    /// </summary>
    /// <typeparam name="T">非托管数值类型。</typeparam>
    public sealed class TimeseriesChunkManager<T> : ITimeseriesChunkManager where T : unmanaged
    {
        private readonly string _fieldName;
        private readonly IMarketMetadata _metadata;
        private readonly IMarketSeriesSource _source;
        private readonly ITimeseriesCacheStore? _cacheStore;
        private readonly int _pageSize;
        private readonly string _refreshMode;

        private ChunkBuffer2D<T>? _buffer;
        private int _totalRows;
        private int _initialRowCount;
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

        /// <summary>
        /// 初始化 <see cref="TimeseriesChunkManager{T}"/> 实例。
        /// </summary>
        public TimeseriesChunkManager(
            string fieldName,
            IMarketMetadata metadata,
            IMarketSeriesSource source,
            ITimeseriesCacheStore? cacheStore,
            int pageSize = 4096,
            string refreshMode = "lazy")
        {
            _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _cacheStore = cacheStore;
            _pageSize = pageSize;
            _refreshMode = refreshMode;
        }

        /// <summary>
        /// 获取当前字段时序矩阵的只读视图接口。
        /// </summary>
        public IReadOnlyBuffer2D<T> AsReadOnly()
        {
            EnsureLoaded();
            return _buffer!.AsReadOnly();
        }

        /// <summary>
        /// 获取底层内存容器实例。
        /// </summary>
        public ChunkBuffer2D<T> Buffer
        {
            get
            {
                EnsureLoaded();
                return _buffer!;
            }
        }

        /// <summary>
        /// 实盘单行/单截面数据追加：自动处理跨页分块轮转 (Chunk Rollover)。
        /// </summary>
        /// <param name="rowValues">当日/当前时间步全市场个股截面数据，长度必须等于代码总数。</param>
        public void AppendRow(ReadOnlySpan<T> rowValues)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();

            if (rowValues.Length != _metadata.Symbols.Count)
            {
                throw new ArgumentException($"Row values length ({rowValues.Length}) does not match symbols count ({_metadata.Symbols.Count}).");
            }

            lock (_gate)
            {
                // 分块轮转 (Rollover)：若当前总行数达到容器物理容量，自动扩容开辟新内存页
                if (_totalRows >= _buffer!.RowCount)
                {
                    _buffer.EnsureCapacity(_totalRows + 1);
                }

                _buffer.SetRow(_totalRows, 0, rowValues);
                _totalRows++;
                _isDirty = true;
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
                _buffer!.SetRow(rowIndex, 0, rowValues);
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
            if (!_isDirty || _cacheStore == null || _buffer == null || _totalRows <= 0) return;

            lock (_gate)
            {
                if (!_isDirty) return;

                int deltaRows = _totalRows - _initialRowCount;
                if (deltaRows > 0)
                {
                    // 1. 增量追加新行到分块存储
                    var appendedDates = new List<DateTime>(deltaRows);
                    for (int i = _initialRowCount; i < _totalRows; i++)
                    {
                        var date = i < _metadata.TradeDates.Count 
                            ? _metadata.TradeDates[i] 
                            : DateTime.Today.AddDays(i - _initialRowCount);
                        appendedDates.Add(date);
                    }

                    _cacheStore.AppendRows<T>(
                        _fieldName,
                        deltaRows,
                        (globalRow, destRow) => _buffer.CopyRow(globalRow, 0, destRow),
                        appendedDates);

                    _initialRowCount = _totalRows;
                }
                else
                {
                    // 2. 行数未增加，仅活跃分块内部数据微调或刷新
                    int activePageIndex = ActivePageIndex;
                    int startRow = activePageIndex * _pageSize;
                    _cacheStore.RewriteChunk<T>(
                        _fieldName,
                        activePageIndex,
                        (globalRow, destRow) => _buffer.CopyRow(globalRow, 0, destRow),
                        newChunkWidth: _buffer.Width,
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
            if (_buffer != null) return;

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_buffer != null) return;
                LoadBufferInternal();
            }
        }

        private void LoadBufferInternal()
        {
            int totalDays = _metadata.TradeDates.Count;
            if (totalDays <= 0) throw new InvalidOperationException("Metadata TradeDates count must be > 0.");

            if (_cacheStore != null)
            {
                if (string.Equals(_refreshMode, "force", StringComparison.OrdinalIgnoreCase))
                {
                    _cacheStore.Clear(_fieldName);
                    var buf = LoadFromSource(totalDays);
                    _cacheStore.WriteBuffer(_fieldName, buf, _metadata.Symbols, _metadata.TradeDates);
                    _buffer = buf;
                    _totalRows = totalDays;
                    _initialRowCount = totalDays;
                    _isDirty = false;
                    return;
                }
                else if (string.Equals(_refreshMode, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    if (_cacheStore.IsCacheCompatible(_fieldName, _metadata.Symbols, typeof(T), out var meta))
                    {
                        if (meta!.RowCount == totalDays)
                        {
                            if (_metadata.Symbols.Count > meta.Symbols.Count)
                            {
                                RefreshActiveChunkForExpandedSymbols(meta);
                            }

                            var buf = _cacheStore.LoadBuffer<T>(_fieldName, _metadata.Symbols.Count);
                            _buffer = buf;
                            _totalRows = buf.RowCount;
                            _initialRowCount = buf.RowCount;
                            _isDirty = false;
                            return;
                        }
                    }

                    var fallbackBuf = LoadFromSource(totalDays);
                    _cacheStore.WriteBuffer(_fieldName, fallbackBuf, _metadata.Symbols, _metadata.TradeDates);
                    _buffer = fallbackBuf;
                    _totalRows = totalDays;
                    _initialRowCount = totalDays;
                    _isDirty = false;
                    return;
                }
                else // lazy
                {
                    if (_cacheStore.IsCacheCompatible(_fieldName, _metadata.Symbols, typeof(T), out var meta))
                    {
                        try
                        {
                            if (_metadata.Symbols.Count > meta!.Symbols.Count)
                            {
                                RefreshActiveChunkForExpandedSymbols(meta);
                            }

                            var buf = _cacheStore.LoadBuffer<T>(_fieldName, _metadata.Symbols.Count);
                            _buffer = buf;
                            _totalRows = buf.RowCount;
                            _initialRowCount = buf.RowCount;
                            _isDirty = false;
                            return;
                        }
                        catch
                        {
                            // 缓存文件损坏或异常时回退全量重建
                        }
                    }

                    var fallbackBuf = LoadFromSource(totalDays);
                    _cacheStore.WriteBuffer(_fieldName, fallbackBuf, _metadata.Symbols, _metadata.TradeDates);
                    _buffer = fallbackBuf;
                    _totalRows = totalDays;
                    _initialRowCount = totalDays;
                    _isDirty = false;
                    return;
                }
            }

            // 无缓存模式，直接从原始数据源加载
            var directBuf = LoadFromSource(totalDays);
            _buffer = directBuf;
            _totalRows = totalDays;
            _initialRowCount = totalDays;
            _isDirty = false;
        }

        private void RefreshActiveChunkForExpandedSymbols(TimeseriesCacheMetadata meta)
        {
            if (_cacheStore == null || meta.RowCount <= 0) return;

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

                _cacheStore.RewriteChunk<T>(
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

        private ChunkBuffer2D<T> LoadFromSource(int totalDays)
        {
            var buffer = new ChunkBuffer2D<T>(_metadata.Symbols.Count, _pageSize, totalDays);

            T[] temp = ArrayPool<T>.Shared.Rent(totalDays);
            try
            {
                for (int colIdx = 0; colIdx < _metadata.Symbols.Count; colIdx++)
                {
                    string symbol = _metadata.Symbols[colIdx];
                    _source.ReadSymbolSeries(symbol, _fieldName, 0, totalDays, temp.AsSpan(0, totalDays));
                    buffer.SetColumn(0, colIdx, temp.AsSpan(0, totalDays));
                }
            }
            finally
            {
                ArrayPool<T>.Shared.Return(temp);
            }

            return buffer;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            lock (_gate)
            {
                if (_disposed) return;
                _buffer?.Dispose();
                _buffer = null;
                _disposed = true;
            }
        }
    }
}
