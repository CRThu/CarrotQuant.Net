using System;
using System.Runtime.CompilerServices;

namespace CarrotBacktesting.NET.Data.Views
{
    /// <summary>
    /// 专用于行访问的只读静态视图类型，直接包装 <see cref="ReadOnlySpan{T}"/>。
    /// </summary>
    /// <typeparam name="T">数据类型。</typeparam>
    public readonly ref struct ReadOnlyRowView<T>
    {
        private readonly ReadOnlySpan<T> _span;

        /// <summary>
        /// 获取视图中的元素总数。
        /// </summary>
        public int Length => _span.Length;

        /// <summary>
        /// 使用只读连续内存切片构造只读行视图。
        /// </summary>
        /// <param name="span">行数据的只读内存切片。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyRowView(ReadOnlySpan<T> span) => _span = span;

        /// <summary>
        /// 获取视图中指定偏移位置的元素只读引用。
        /// </summary>
        /// <param name="i">索引。</param>
        /// <returns>数据的只读引用。</returns>
        public ref readonly T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _span[i];
        }

        /// <summary>
        /// 将视图转换为 <see cref="ReadOnlySpan{T}"/>。
        /// </summary>
        /// <returns>对应的只读 Span。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> AsSpan() => _span;

        /// <summary>
        /// 支持通过 foreach 枚举视图中的元素。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T>.Enumerator GetEnumerator() => _span.GetEnumerator();

        /// <summary>
        /// 支持隐式转换为 <see cref="ReadOnlySpan{T}"/>，无需显式调用 AsSpan()。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<T>(ReadOnlyRowView<T> view) => view._span;
    }
}
