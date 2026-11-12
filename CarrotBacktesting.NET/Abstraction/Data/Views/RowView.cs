using System;
using System.Runtime.CompilerServices;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 专用于行访问的静态视图类型，直接包装 <see cref="Span{T}"/> 以实现零开销访问。
    /// </summary>
    /// <typeparam name="T">数据类型。</typeparam>
    public readonly ref struct RowView<T>
    {
        private readonly Span<T> _span;

        /// <summary>
        /// 获取视图中的元素总数。
        /// </summary>
        public int Length => _span.Length;

        /// <summary>
        /// 使用连续内存切片构造行视图。
        /// </summary>
        /// <param name="span">行数据的可写内存切片。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RowView(Span<T> span) => _span = span;

        /// <summary>
        /// 获取视图中指定偏移位置的元素引用。
        /// </summary>
        /// <param name="i">索引。</param>
        /// <returns>数据的可写引用。</returns>
        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _span[i];
        }

        /// <summary>
        /// 将视图转换为 <see cref="Span{T}"/>。
        /// </summary>
        /// <returns>对应的可写 Span。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> AsSpan() => _span;

        /// <summary>
        /// 支持通过 foreach 枚举视图中的元素。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T>.Enumerator GetEnumerator() => _span.GetEnumerator();

        /// <summary>
        /// 支持隐式转换为 <see cref="Span{T}"/>，无需显式调用 AsSpan()。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Span<T>(RowView<T> view) => view._span;

        /// <summary>
        /// 支持隐式转换为 <see cref="ReadOnlySpan{T}"/>。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<T>(RowView<T> view) => view._span;
    }
}
