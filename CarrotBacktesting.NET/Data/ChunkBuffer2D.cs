using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Carrot.Memory;
using Carrot.Memory.Abstractions;
using Carrot.Memory.Views;
using CommunityToolkit.HighPerformance;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 基于连续定长分页（Chunk）的高性能二维无锁只读/可写内存容器。
    /// 专为大规模行情矩阵设计，底座采用非托管内存管理，消除 GC 碎片；
    /// 寻址全面采用位运算（Shift/Mask）实现零除法开销；
    /// 读取路径提供无锁（Lock-Free）零并发竞争保证，满足高并发多线程回测读取需求。
    /// </summary>
    /// <typeparam name="T">非托管数值类型（如 double, float, long, int 等）。</typeparam>
    public sealed unsafe class ChunkBuffer2D<T> : IBuffer2D<T>, IReadOnlyBuffer2D<T>, IDisposable where T : unmanaged
    {
        private readonly int _width;
        private readonly int _pageSize;
        private readonly int _shift;
        private readonly int _mask;
        private readonly object _gate = new();

        private T*[] _pages;
        private int _pageCount;
        private int _rowCount;
        private int _disposed;

        /// <inheritdoc />
        public int RowCount => Volatile.Read(ref _rowCount);

        /// <inheritdoc />
        public int Width => _width;

        /// <summary>
        /// 获取单页行数容量。
        /// </summary>
        public int PageSize => _pageSize;

        /// <summary>
        /// 获取已分配的内存页总数。
        /// </summary>
        public int PageCount => Volatile.Read(ref _pageCount);

        /// <summary>
        /// 初始化 <see cref="ChunkBuffer2D{T}"/> 实例。
        /// </summary>
        /// <param name="width">矩阵宽度（列数，如股票代码总数）。</param>
        /// <param name="pageSize">单页行数容量，必须为大于 0 且为 2 的整数幂（如 1024, 2048, 4096）。默认为 4096。</param>
        /// <param name="initialRowCount">初始预分配行数。默认为 0。</param>
        public ChunkBuffer2D(int width, int pageSize = 4096, int initialRowCount = 0)
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

            _width = width;
            _pageSize = pageSize;
            _shift = BitOperations.TrailingZeroCount((uint)pageSize);
            _mask = pageSize - 1;

            int initialPages = initialRowCount > 0 ? (int)(((long)initialRowCount + pageSize - 1) >> _shift) : 4;
            _pages = new T*[Math.Max(4, initialPages)];

            if (initialRowCount > 0)
            {
                EnsureCapacity(initialRowCount);
            }
        }

        /// <summary>
        /// 获取当前实例的只读视图接口。
        /// </summary>
        public IReadOnlyBuffer2D<T> AsReadOnly() => this;

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
                if ((uint)r >= (uint)_rowCount || (uint)c >= (uint)_width)
                {
                    ThrowIndexOutOfRangeException("行列索引越界。");
                }
                int pageIdx = r >> _shift;
                int rowInPage = r & _mask;
                var pages = Volatile.Read(ref _pages);
                return ref *(pages[pageIdx] + ((long)rowInPage * _width + c));
            }
        }

        /// <inheritdoc />
        ref readonly T IReadOnlyBuffer2D<T>.this[int r, int c]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref this[r, c];
        }

        /// <summary>
        /// 确保容器总行数容量至少达到 <paramref name="targetRowCount"/>。
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
                if (neededPages > _pages.Length)
                {
                    int newCap = Math.Max(neededPages, _pages.Length * 2);
                    var newPages = new T*[newCap];
                    Array.Copy(_pages, newPages, _pageCount);
                    Volatile.Write(ref _pages, newPages);
                }

                nuint bytesPerPage = (nuint)_pageSize * (nuint)_width * (nuint)sizeof(T);
                while (_pageCount < neededPages)
                {
                    _pages[_pageCount] = (T*)NativeMemory.AllocZeroed(bytesPerPage);
                    _pageCount++;
                }

                Volatile.Write(ref _rowCount, targetRowCount);
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
            if (r < 0)
            {
                ThrowIndexOutOfRangeException("行索引不能为负数。");
            }
            if (r >= _rowCount)
            {
                EnsureCapacity(r + 1);
            }

            int pageIdx = r >> _shift;
            int rowInPage = r & _mask;
            var pages = Volatile.Read(ref _pages);
            pages[pageIdx][(long)rowInPage * _width + c] = value;
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
            if (r < 0)
            {
                ThrowIndexOutOfRangeException("起始行索引不能为负数。");
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
                    int canWrite = Math.Min(_pageSize - rowInPage, remaining);
                    T* pDstPage = pages[pageIdx];

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
            if (r < 0)
            {
                ThrowIndexOutOfRangeException("行索引不能为负数。");
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
            var pages = Volatile.Read(ref _pages);
            T* pDst = pages[pageIdx] + ((long)rowInPage * _width + c);

            fixed (T* pSrc = data)
            {
                Buffer.MemoryCopy(pSrc, pDst, (long)data.Length * sizeof(T), (long)data.Length * sizeof(T));
            }
        }

        /// <inheritdoc />
        public void SetBlock(int r, int c, ReadOnlySpan2D<T> data)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (r < 0 || c < 0 || (uint)(c + data.Width) > (uint)_width)
            {
                throw new ArgumentException("写入区域越界或非法。");
            }

            int targetRowCount = r + data.Height;
            if (targetRowCount > _rowCount)
            {
                EnsureCapacity(targetRowCount);
            }

            for (int i = 0; i < data.Height; i++)
            {
                SetRow(r + i, c, data.GetRowSpan(i));
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
            if ((uint)c >= (uint)_width || (uint)r + (uint)destination.Length > (uint)_rowCount)
            {
                ThrowIndexOutOfRangeException("列读取越界。");
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
                    int canRead = Math.Min(_pageSize - rowInPage, remaining);
                    T* pSrcPage = pages[pageIdx];

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
            if ((uint)r >= (uint)_rowCount || (uint)c + (uint)destination.Length > (uint)_width)
            {
                ThrowIndexOutOfRangeException("行读取越界。");
            }
            if (destination.Length == 0) return;

            int pageIdx = r >> _shift;
            int rowInPage = r & _mask;
            var pages = Volatile.Read(ref _pages);
            T* pSrc = pages[pageIdx] + ((long)rowInPage * _width + c);

            fixed (T* pDst = destination)
            {
                Buffer.MemoryCopy(pSrc, pDst, (long)destination.Length * sizeof(T), (long)destination.Length * sizeof(T));
            }
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RowView<T> GetRowView(int row, int col, int len)
        {
            if ((uint)row >= (uint)_rowCount || (uint)col + (uint)len > (uint)_width)
            {
                ThrowIndexOutOfRangeException("行视图索引或截取长度越界。");
            }

            int pageIdx = row >> _shift;
            int rowInPage = row & _mask;
            var pages = Volatile.Read(ref _pages);
            T* ptr = pages[pageIdx] + ((long)rowInPage * _width + col);
            return new RowView<T>(new Span<T>(ptr, len));
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ReadOnlyRowView<T> IReadOnlyBuffer2D<T>.GetRowView(int row, int col, int len)
        {
            if ((uint)row >= (uint)_rowCount || (uint)col + (uint)len > (uint)_width)
            {
                ThrowIndexOutOfRangeException("只读行视图索引或截取长度越界。");
            }

            int pageIdx = row >> _shift;
            int rowInPage = row & _mask;
            var pages = Volatile.Read(ref _pages);
            T* ptr = pages[pageIdx] + ((long)rowInPage * _width + col);
            return new ReadOnlyRowView<T>(new ReadOnlySpan<T>(ptr, len));
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ColumnView<T> GetColumnView(int row, int col, int len)
        {
            if ((uint)col >= (uint)_width || (uint)row + (uint)len > (uint)_rowCount)
            {
                ThrowIndexOutOfRangeException("列视图索引或截取长度越界。");
            }

            return new ColumnView<T>(this, row, col, len);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ReadOnlyColumnView<T> IReadOnlyBuffer2D<T>.GetColumnView(int row, int col, int len)
        {
            if ((uint)col >= (uint)_width || (uint)row + (uint)len > (uint)_rowCount)
            {
                ThrowIndexOutOfRangeException("只读列视图索引或截取长度越界。");
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
            if ((uint)pageIndex >= (uint)_pageCount)
            {
                ThrowIndexOutOfRangeException("分页索引越界。");
            }
            if (rowsInPage < 0 || rowsInPage > _pageSize)
            {
                ThrowIndexOutOfRangeException("单页行数超出页容量。");
            }
            var pages = Volatile.Read(ref _pages);
            return new Span<T>(pages[pageIndex], rowsInPage * _width);
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
            if ((uint)pageIndex >= (uint)_pageCount)
            {
                ThrowIndexOutOfRangeException("分页索引越界。");
            }
            if (rowsInPage < 0 || rowsInPage > _pageSize)
            {
                ThrowIndexOutOfRangeException("单页行数超出页容量。");
            }
            var pages = Volatile.Read(ref _pages);
            return new ReadOnlySpan<T>(pages[pageIndex], rowsInPage * _width);
        }

        /// <inheritdoc />
        public void Commit()
        {
            // 纯内存模型，数据立即可用，无需物理同步
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            lock (_gate)
            {
                for (int i = 0; i < _pageCount; i++)
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

        ~ChunkBuffer2D()
        {
            Dispose();
        }

        [DoesNotReturn]
        private static void ThrowIndexOutOfRangeException(string message) => throw new IndexOutOfRangeException(message);
    }
}
