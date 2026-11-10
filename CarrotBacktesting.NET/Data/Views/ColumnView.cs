using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data.Views
{
    /// <summary>
    /// 专用于列访问的静态视图类型，封装行列索引与 <see cref="IBuffer2D{T}"/> 的寻址逻辑。
    /// </summary>
    /// <typeparam name="T">数据类型，必须为非托管类型。</typeparam>
    public readonly ref struct ColumnView<T> where T : unmanaged
    {
        private readonly IBuffer2D<T> _parent;
        private readonly int _r, _c, _len;

        /// <summary>
        /// 获取视图中的元素总数。
        /// </summary>
        public int Length => _len;

        /// <summary>
        /// 构造针对指定二维缓冲区的列视图。
        /// </summary>
        /// <param name="parent">所属的二维缓冲区实例。</param>
        /// <param name="r">起始行索引。</param>
        /// <param name="c">列索引。</param>
        /// <param name="len">截取的垂直长度。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ColumnView(IBuffer2D<T> parent, int r, int c, int len)
        {
            _parent = parent;
            _r = r;
            _c = c;
            _len = len;
        }

        /// <summary>
        /// 获取视图中指定偏移位置的元素引用。
        /// </summary>
        /// <param name="i">索引。</param>
        /// <returns>数据的可写引用。</returns>
        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)i >= (uint)_len) ThrowIndexOutOfRangeException();
                return ref _parent[_r + i, _c];
            }
        }

        [DoesNotReturn]
        private static void ThrowIndexOutOfRangeException() => throw new IndexOutOfRangeException("视图访问越界。");
    }
}
