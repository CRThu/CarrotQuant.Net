using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data.Views;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 基于连续定长分页（Chunk）的高性能二维无锁只读/可写内存矩阵。
    /// 专为大规模金融时序数据设计，底座采用非托管内存管理（NativeMemory），消除 GC 碎片；
    /// 寻址全面采用位运算（Shift/Mask）实现零除法开销；
    /// 读取路径提供无锁（Lock-Free）零并发竞争保证，满足高并发多线程回测读取需求。
    /// </summary>
    /// <typeparam name="T">非托管数值类型（如 double, float, long, int 等）。</typeparam>
    public sealed unsafe class ChunkMatrix2D<T> : IMatrix2D<T>, IReadOnlyMatrix2D<T>, IDisposable where T : unmanaged
    {
        private int _width;
        private readonly int _pageSize;
        private readonly int _shift;
        private readonly int _mask;
        private readonly int _maxPages;
        private readonly int _slotMask;
        private readonly object _gate = new();

        private T*[] _pages;
        private int _pageCount;
        private int _rowCount;
        private int _baseRowIndex;
        private int _disposed;

        /// <inheritdoc />
        public int RowCount => Volatile.Read(ref _rowCount);

        /// <summary>
        /// 获取当前常驻有效区间的起始基准行号（老于该行号的数据已滑出常驻内存）。
        /// 全量常驻模式下恒为 0。
        /// </summary>
        public int BaseRowIndex => Volatile.Read(ref _baseRowIndex);

        /// <summary>
        /// 获取最大常驻内存页数（0 表示全量常驻，无上限）。
        /// </summary>
        public int MaxPages => _maxPages;

        /// <inheritdoc />
        public int Width => Volatile.Read(ref _width);

        /// <summary>
        /// 获取单页行数容量。
        /// </summary>
        public int PageSize => _pageSize;

        /// <summary>
        /// 获取已分配的内存页总数。
        /// </summary>
        public int PageCount => Volatile.Read(ref _pageCount);

        /// <summary>
        /// 初始化 <see cref="ChunkMatrix2D{T}"/> 实例。
        /// </summary>
        /// <param name="width">矩阵宽度（列数，如股票代码总数）。</param>
        /// <param name="pageSize">单页行数容量，必须为大于 0 且为 2 的整数幂（如 1024, 2048, 4096）。默认为 4096。</param>
        /// <param name="initialRowCount">初始预分配行数。默认为 0。</param>
        /// <param name="maxPages">最大常驻内存页数，必须为 0 或 2 的整数幂（如 2, 4, 8）。设为 0 或负数表示不限常驻页数（全量常驻）。默认为 0。</param>
        public ChunkMatrix2D(int width, int pageSize = 4096, int initialRowCount = 0, int maxPages = 0)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Width 必须大于 0。");
            }
            if (pageSize <= 0 || (pageSize & (pageSize - 1)) != 0)
            {
                throw new ArgumentException("PageSize 必须为大于 0 且为 2 的整数次幂。", nameof(pageSize));
            }
            if (initialRowCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialRowCount), "InitialRowCount 不能为负数。");
            }
            if (maxPages > 0 && (maxPages & (maxPages - 1)) != 0)
            {
                throw new ArgumentException("MaxPages 必须为 2 的整数次幂（例如 2, 4, 8, 16）。", nameof(maxPages));
            }

            _width = width;
            _pageSize = pageSize;
            _shift = BitOperations.TrailingZeroCount((uint)pageSize);
            _mask = pageSize - 1;
            _maxPages = maxPages > 0 ? maxPages : 0;
            _slotMask = _maxPages > 0 ? (_maxPages - 1) : 0;

            if (_maxPages > 0)
            {
                _pages = new T*[_maxPages];
            }
            else
            {
                int initialPages = initialRowCount > 0 ? (int)(((long)initialRowCount + pageSize - 1) >> _shift) : 4;
                _pages = new T*[Math.Max(4, initialPages)];
            }

            if (initialRowCount > 0)
            {
                EnsureCapacity(initialRowCount);
            }
        }

        /// <summary>
        /// 获取当前实例的只读视图接口。
        /// </summary>
        public IReadOnlyMatrix2D<T> AsReadOnly() => this;

        /// <summary>
        /// 获取或设置指定行列位置的元素引用。
        /// 读路径为完全无锁的无等待寻址，并发读安全。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">列索引。</param>
        /// <returns>元素的可读写引用。</returns>
        public ref T this[int r, int c]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                int rowCount = Volatile.Read(ref _rowCount);
                int baseRow = Volatile.Read(ref _baseRowIndex);
                if ((uint)(r - baseRow) >= (uint)(rowCount - baseRow) || (uint)c >= (uint)_width)
                {
                    ThrowIndexOutOfRangeException("行列索引越界或请求的历史行已滑出常驻内存窗口。");
                }
                int pageIdx = r >> _shift;
                int rowInPage = r & _mask;
                int slotIdx = _maxPages > 0 ? (pageIdx & _slotMask) : pageIdx;
                var pages = Volatile.Read(ref _pages);
                return ref *(pages[slotIdx] + ((long)rowInPage * _width + c));
            }
        }

        /// <inheritdoc />
        ref readonly T IReadOnlyMatrix2D<T>.this[int r, int c]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref this[r, c];
        }

        /// <summary>
        /// 推进常驻有效区间的起始基准行号（淘汰老于 <paramref name="newBaseRowIndex"/> 的分块）。
        /// </summary>
        public void AdvanceBaseRow(int newBaseRowIndex)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            lock (_gate)
            {
                if (newBaseRowIndex > _baseRowIndex)
                {
                    Volatile.Write(ref _baseRowIndex, newBaseRowIndex);
                }
            }
        }

        /// <summary>
        /// 确保矩阵总行数容量至少达到 <paramref name="targetRowCount"/>。
        /// 写入阶段线程安全，多线程扩容受内部锁保护；读线程不受阻断。
        /// </summary>
        /// <param name="targetRowCount">目标行数容量。</param>
        public void EnsureCapacity(int targetRowCount)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (targetRowCount <= _rowCount) return;

            lock (_gate)
            {
                if (targetRowCount <= _rowCount) return;

                int neededPages = (int)(((long)targetRowCount + _pageSize - 1) >> _shift);
                nuint bytesPerPage = (nuint)_pageSize * (nuint)_width * (nuint)sizeof(T);

                if (_maxPages > 0)
                {
                    // 环形槽位模式：槽位数量固定为 _maxPages
                    while (_pageCount < Math.Min(neededPages, _maxPages))
                    {
                        _pages[_pageCount] = (T*)NativeMemory.AllocZeroed(bytesPerPage);
                        _pageCount++;
                    }

                    // 若目标行数跨越的逻辑页超出常驻页数，自动向前推进 BaseRowIndex 淘汰最老页
                    int oldestAllowedPage = neededPages - _maxPages;
                    if (oldestAllowedPage > 0)
                    {
                        int newBaseRow = oldestAllowedPage * _pageSize;
                        if (newBaseRow > _baseRowIndex)
                        {
                            Volatile.Write(ref _baseRowIndex, newBaseRow);
                        }
                    }

                    Volatile.Write(ref _rowCount, targetRowCount);
                }
                else
                {
                    // 全量线性模式
                    if (neededPages > _pages.Length)
                    {
                        int newCap = Math.Max(neededPages, _pages.Length * 2);
                        var newPages = new T*[newCap];
                        Array.Copy(_pages, newPages, _pageCount);
                        while (_pageCount < neededPages)
                        {
                            newPages[_pageCount] = (T*)NativeMemory.AllocZeroed(bytesPerPage);
                            _pageCount++;
                        }
                        Volatile.Write(ref _pages, newPages);
                    }
                    else
                    {
                        while (_pageCount < neededPages)
                        {
                            _pages[_pageCount] = (T*)NativeMemory.AllocZeroed(bytesPerPage);
                            _pageCount++;
                        }
                    }

                    Volatile.Write(ref _rowCount, targetRowCount);
                }
            }
        }

        /// <summary>
        /// 原子拓宽矩阵列维度（新股/新标的上市）。
        /// 遵循单一事实来源（SSOT）契约：列宽只增不减；旧列数据保持不变，新扩展列的已有历史行全部自动填充 NaN。
        /// </summary>
        /// <param name="newWidth">新的列宽度，必须大于当前 Width。</param>
        public void ExpandWidth(int newWidth)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (newWidth <= _width) return;

            lock (_gate)
            {
                if (newWidth <= _width) return;

                int oldWidth = _width;
                int pageCount = _pageCount;
                nuint newBytesPerPage = (nuint)_pageSize * (nuint)newWidth * (nuint)sizeof(T);
                T*[] newPages = new T*[_pages.Length];
                T nanValue = MarketDataAligner.GetNaNValue<T>();

                for (int p = 0; p < pageCount; p++)
                {
                    T* pNewPage = (T*)NativeMemory.AllocZeroed(newBytesPerPage);
                    newPages[p] = pNewPage;
                    T* pOldPage = _pages[p];

                    // 计算该页有效行数
                    int rowsInPage = (p == pageCount - 1 && (_rowCount & _mask) != 0)
                        ? (_rowCount & _mask) 
                        : _pageSize;

                    for (int r = 0; r < rowsInPage; r++)
                    {
                        T* pSrcRow = pOldPage + ((long)r * oldWidth);
                        T* pDstRow = pNewPage + ((long)r * newWidth);

                        // 1. 拷贝旧标的数据
                        Buffer.MemoryCopy(pSrcRow, pDstRow, (long)oldWidth * sizeof(T), (long)oldWidth * sizeof(T));

                        // 2. 新标的上市前的历史行填充 NaN
                        for (int c = oldWidth; c < newWidth; c++)
                        {
                            pDstRow[c] = nanValue;
                        }
                    }

                    // 释放旧页内存
                    NativeMemory.Free(pOldPage);
                }

                // 原子更新分页引用与宽度
                Volatile.Write(ref _pages, newPages);
                Volatile.Write(ref _width, newWidth);
            }
        }

        /// <inheritdoc />
        public void SetElement(int r, int c, T value)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if ((uint)c >= (uint)_width)
            {
                ThrowIndexOutOfRangeException("列索引越界。");
            }
            if (r < _baseRowIndex)
            {
                ThrowIndexOutOfRangeException("行索引不能小于当前常驻基准行。");
            }
            if (r >= _rowCount)
            {
                EnsureCapacity(r + 1);
            }

            int pageIdx = r >> _shift;
            int rowInPage = r & _mask;
            int slotIdx = _maxPages > 0 ? (pageIdx & _slotMask) : pageIdx;
            var pages = Volatile.Read(ref _pages);
            pages[slotIdx][(long)rowInPage * _width + c] = value;
        }

        /// <summary>
        /// 垂直批量写入单列时序数据块。
        /// 针对时序批量灌入（ETL）做连续寻址优化，自动完成跨分页切分。
        /// </summary>
        /// <param name="r">起始行索引。</param>
        /// <param name="c">列索引。</param>
        /// <param name="data">待写入的一维切片。</param>
        public void SetColumn(int r, int c, ReadOnlySpan<T> data)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if ((uint)c >= (uint)_width)
            {
                ThrowIndexOutOfRangeException("列索引越界。");
            }
            if (r < _baseRowIndex)
            {
                ThrowIndexOutOfRangeException("起始行索引不能小于当前常驻基准行。");
            }
            if (data.Length == 0) return;

            int targetRowCount = r + data.Length;
            if (targetRowCount > _rowCount)
            {
                EnsureCapacity(targetRowCount);
            }

            int remaining = data.Length;
            int srcOffset = 0;
            int currRow = r;
            var pages = Volatile.Read(ref _pages);

            fixed (T* pSrc = data)
            {
                while (remaining > 0)
                {
                    int pageIdx = currRow >> _shift;
                    int rowInPage = currRow & _mask;
                    int slotIdx = _maxPages > 0 ? (pageIdx & _slotMask) : pageIdx;
                    int canWrite = Math.Min(_pageSize - rowInPage, remaining);
                    T* pDstPage = pages[slotIdx];

                    T* pDst = pDstPage + ((long)rowInPage * _width + c);
                    T* pCurSrc = pSrc + srcOffset;

                    for (int i = 0; i < canWrite; i++)
                    {
                        *pDst = *pCurSrc;
                        pDst += _width;
                        pCurSrc++;
                    }

                    currRow += canWrite;
                    srcOffset += canWrite;
                    remaining -= canWrite;
                }
            }
        }

        /// <summary>
        /// 水平批量写入单行截面数据块。
        /// 内存物理连续，使用底层向量化内存拷贝（MemoryCopy）。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">起始列索引。</param>
        /// <param name="data">待写入的一维截面切片。</param>
        public void SetRow(int r, int c, ReadOnlySpan<T> data)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (r < _baseRowIndex)
            {
                ThrowIndexOutOfRangeException("行索引不能小于当前常驻基准行。");
            }
            if ((uint)c + (uint)data.Length > (uint)_width)
            {
                ThrowIndexOutOfRangeException("行写入跨越列边界。");
            }
            if (data.Length == 0) return;
            if (r >= _rowCount)
            {
                EnsureCapacity(r + 1);
            }

            int pageIdx = r >> _shift;
            int rowInPage = r & _mask;
            int slotIdx = _maxPages > 0 ? (pageIdx & _slotMask) : pageIdx;
            var pages = Volatile.Read(ref _pages);
            T* pDst = pages[slotIdx] + ((long)rowInPage * _width + c);

            fixed (T* pSrc = data)
            {
                Buffer.MemoryCopy(pSrc, pDst, (long)data.Length * sizeof(T), (long)data.Length * sizeof(T));
            }
        }

        /// <summary>
        /// 垂直批量读取单列数据到目标内存缓冲区。
        /// </summary>
        /// <param name="r">起始行索引。</param>
        /// <param name="c">列索引。</param>
        /// <param name="destination">目标输出缓冲区。</param>
        public void CopyColumn(int r, int c, Span<T> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            int rowCount = Volatile.Read(ref _rowCount);
            int baseRow = Volatile.Read(ref _baseRowIndex);
            if ((uint)c >= (uint)_width || r < baseRow || (uint)(r - baseRow) + (uint)destination.Length > (uint)(rowCount - baseRow))
            {
                ThrowIndexOutOfRangeException("列读取越界或已滑出常驻内存窗口。");
            }
            if (destination.Length == 0) return;

            int remaining = destination.Length;
            int dstOffset = 0;
            int currRow = r;
            var pages = Volatile.Read(ref _pages);

            fixed (T* pDst = destination)
            {
                while (remaining > 0)
                {
                    int pageIdx = currRow >> _shift;
                    int rowInPage = currRow & _mask;
                    int slotIdx = _maxPages > 0 ? (pageIdx & _slotMask) : pageIdx;
                    int canRead = Math.Min(_pageSize - rowInPage, remaining);
                    T* pSrcPage = pages[slotIdx];

                    T* pSrc = pSrcPage + ((long)rowInPage * _width + c);
                    T* pCurDst = pDst + dstOffset;

                    for (int i = 0; i < canRead; i++)
                    {
                        *pCurDst = *pSrc;
                        pSrc += _width;
                        pCurDst++;
                    }

                    currRow += canRead;
                    dstOffset += canRead;
                    remaining -= canRead;
                }
            }
        }

        /// <summary>
        /// 水平批量读取单行数据到目标内存缓冲区。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">起始列索引。</param>
        /// <param name="destination">目标输出缓冲区。</param>
        public void CopyRow(int r, int c, Span<T> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            int rowCount = Volatile.Read(ref _rowCount);
            int baseRow = Volatile.Read(ref _baseRowIndex);
            if ((uint)(r - baseRow) >= (uint)(rowCount - baseRow) || (uint)c + (uint)destination.Length > (uint)_width)
            {
                ThrowIndexOutOfRangeException("行读取越界或已滑出常驻内存窗口。");
            }
            if (destination.Length == 0) return;

            int pageIdx = r >> _shift;
            int rowInPage = r & _mask;
            int slotIdx = _maxPages > 0 ? (pageIdx & _slotMask) : pageIdx;
            var pages = Volatile.Read(ref _pages);
            T* pSrc = pages[slotIdx] + ((long)rowInPage * _width + c);

            fixed (T* pDst = destination)
            {
                Buffer.MemoryCopy(pSrc, pDst, (long)destination.Length * sizeof(T), (long)destination.Length * sizeof(T));
            }
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RowView<T> GetRowView(int row, int col, int len)
        {
            int rowCount = Volatile.Read(ref _rowCount);
            int baseRow = Volatile.Read(ref _baseRowIndex);
            if ((uint)(row - baseRow) >= (uint)(rowCount - baseRow) || (uint)col + (uint)len > (uint)_width)
            {
                ThrowIndexOutOfRangeException("行视图索引或截取长度越界（或已滑出常驻内存窗口）。");
            }

            int pageIdx = row >> _shift;
            int rowInPage = row & _mask;
            int slotIdx = _maxPages > 0 ? (pageIdx & _slotMask) : pageIdx;
            var pages = Volatile.Read(ref _pages);
            T* ptr = pages[slotIdx] + ((long)rowInPage * _width + col);
            return new RowView<T>(new Span<T>(ptr, len));
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ReadOnlyRowView<T> IReadOnlyMatrix2D<T>.GetRowView(int row, int col, int len)
        {
            int rowCount = Volatile.Read(ref _rowCount);
            int baseRow = Volatile.Read(ref _baseRowIndex);
            if ((uint)(row - baseRow) >= (uint)(rowCount - baseRow) || (uint)col + (uint)len > (uint)_width)
            {
                ThrowIndexOutOfRangeException("只读行视图索引或截取长度越界（或已滑出常驻内存窗口）。");
            }

            int pageIdx = row >> _shift;
            int rowInPage = row & _mask;
            int slotIdx = _maxPages > 0 ? (pageIdx & _slotMask) : pageIdx;
            var pages = Volatile.Read(ref _pages);
            T* ptr = pages[slotIdx] + ((long)rowInPage * _width + col);
            return new ReadOnlyRowView<T>(new ReadOnlySpan<T>(ptr, len));
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ColumnView<T> GetColumnView(int row, int col, int len)
        {
            int rowCount = Volatile.Read(ref _rowCount);
            int baseRow = Volatile.Read(ref _baseRowIndex);
            if ((uint)col >= (uint)_width || row < baseRow || (uint)(row - baseRow) + (uint)len > (uint)(rowCount - baseRow))
            {
                ThrowIndexOutOfRangeException("列视图索引或截取长度越界（或已滑出常驻内存窗口）。");
            }

            return new ColumnView<T>(this, row, col, len);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ReadOnlyColumnView<T> IReadOnlyMatrix2D<T>.GetColumnView(int row, int col, int len)
        {
            int rowCount = Volatile.Read(ref _rowCount);
            int baseRow = Volatile.Read(ref _baseRowIndex);
            if ((uint)col >= (uint)_width || row < baseRow || (uint)(row - baseRow) + (uint)len > (uint)(rowCount - baseRow))
            {
                ThrowIndexOutOfRangeException("只读列视图索引或截取长度越界（或已滑出常驻内存窗口）。");
            }

            return new ReadOnlyColumnView<T>(this, row, col, len);
        }

        /// <summary>
        /// 获取指定单页的连续内存切片（长度为 rowsInPage * Width）。
        /// 供块级批量 I/O 与压缩/解压流水线直接操作底层非托管内存。
        /// </summary>
        /// <param name="pageIndex">页索引。</param>
        /// <param name="rowsInPage">该页包含的有效行数。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetPageSpan(int pageIndex, int rowsInPage)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (rowsInPage < 0 || rowsInPage > _pageSize)
            {
                ThrowIndexOutOfRangeException("单页行数超出页容量。");
            }
            int basePage = _baseRowIndex >> _shift;
            int maxPage = _rowCount > 0 ? ((_rowCount + _pageSize - 1) >> _shift) : 0;
            if (pageIndex < basePage || pageIndex >= maxPage)
            {
                ThrowIndexOutOfRangeException("页索引越界或已滑出常驻内存窗口。");
            }
            int slotIdx = _maxPages > 0 ? (pageIndex & _slotMask) : pageIndex;
            var pages = Volatile.Read(ref _pages);
            if (pages[slotIdx] == null)
            {
                ThrowIndexOutOfRangeException("目标分块槽位尚未分配。");
            }
            return new Span<T>(pages[slotIdx], rowsInPage * _width);
        }

        /// <summary>
        /// 获取指定单页的连续只读内存切片。
        /// </summary>
        /// <param name="pageIndex">页索引。</param>
        /// <param name="rowsInPage">该页包含的有效行数。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> GetReadOnlyPageSpan(int pageIndex, int rowsInPage)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (rowsInPage < 0 || rowsInPage > _pageSize)
            {
                ThrowIndexOutOfRangeException("单页行数超出页容量。");
            }
            int basePage = _baseRowIndex >> _shift;
            int maxPage = _rowCount > 0 ? ((_rowCount + _pageSize - 1) >> _shift) : 0;
            if (pageIndex < basePage || pageIndex >= maxPage)
            {
                ThrowIndexOutOfRangeException("页索引越界或已滑出常驻内存窗口。");
            }
            int slotIdx = _maxPages > 0 ? (pageIndex & _slotMask) : pageIndex;
            var pages = Volatile.Read(ref _pages);
            if (pages[slotIdx] == null)
            {
                ThrowIndexOutOfRangeException("目标分块槽位尚未分配。");
            }
            return new ReadOnlySpan<T>(pages[slotIdx], rowsInPage * _width);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            lock (_gate)
            {
                int pagesToFree = _maxPages > 0 ? Math.Min(_pageCount, _maxPages) : _pageCount;
                for (int i = 0; i < pagesToFree; i++)
                {
                    if (_pages[i] != null)
                    {
                        NativeMemory.Free(_pages[i]);
                        _pages[i] = null;
                    }
                }
                _pageCount = 0;
                _rowCount = 0;
            }

            GC.SuppressFinalize(this);
        }

        ~ChunkMatrix2D()
        {
            Dispose();
        }

        [DoesNotReturn]
        private static void ThrowIndexOutOfRangeException(string message) => throw new IndexOutOfRangeException(message);
    }
}
