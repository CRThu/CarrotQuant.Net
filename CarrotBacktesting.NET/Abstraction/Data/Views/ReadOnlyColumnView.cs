using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 专用于列访问的只读静态视图类型，封装行列索引与 <see cref="IReadOnlyMatrix2D{T}"/> 的寻址逻辑。
    /// </summary>
    /// <typeparam name="T">数据类型，必须为非托管类型。</typeparam>
    public readonly ref struct ReadOnlyColumnView<T> where T : unmanaged
    {
        private readonly IReadOnlyMatrix2D<T> _parent;
        private readonly int _r, _c, _len;

        /// <summary>
        /// 获取视图中的元素总数。
        /// </summary>
        public int Length => _len;

        /// <summary>
        /// 构造针对指定二维时序矩阵的只读列视图。
        /// </summary>
        /// <param name="parent">所属的只读二维矩阵实例。</param>
        /// <param name="r">起始行索引。</param>
        /// <param name="c">列索引。</param>
        /// <param name="len">截取的垂直长度。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyColumnView(IReadOnlyMatrix2D<T> parent, int r, int c, int len)
        {
            _parent = parent;
            _r = r;
            _c = c;
            _len = len;
        }

        /// <summary>
        /// 获取视图中指定偏移位置的元素只读引用。
        /// </summary>
        /// <param name="i">索引。</param>
        /// <returns>数据的只读引用。</returns>
        public ref readonly T this[int i]
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

        /// <summary>
        /// 支持通过 foreach 枚举视图中的元素。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(this);

        /// <summary>
        /// 只读列视图枚举器。
        /// </summary>
        public ref struct Enumerator
        {
            private readonly ReadOnlyColumnView<T> _view;
            private int _index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator(ReadOnlyColumnView<T> view)
            {
                _view = view;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() => ++_index < _view.Length;

            public ref readonly T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _view[_index];
            }
        }
    }
}
