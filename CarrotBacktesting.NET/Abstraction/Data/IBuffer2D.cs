using System;
using CarrotBacktesting.NET.Data.Views;
using CommunityToolkit.HighPerformance;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 提供对二维内存缓冲区的可读写访问契约。
    /// 该接口继承自 <see cref="IReadOnlyBuffer2D{T}"/>，并提供修改数据的能力。
    /// </summary>
    /// <typeparam name="T">存储的数据类型，必须为非托管类型。</typeparam>
    public interface IBuffer2D<T> : IReadOnlyBuffer2D<T>, IDisposable where T : unmanaged
    {
        /// <summary>
        /// 获取指定行号和列号的数据可写引用。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">列索引。</param>
        /// <returns>数据的可写引用。</returns>
        new ref T this[int r, int c] { get; }

        /// <summary>
        /// 获取指定行中某一段的水平可写视图（行视图）。
        /// </summary>
        /// <param name="row">起始行索引。</param>
        /// <param name="col">起始列索引。</param>
        /// <param name="len">截取长度。</param>
        /// <returns>对应的可写视图对象。</returns>
        new RowView<T> GetRowView(int row, int col, int len);

        /// <summary>
        /// 获取指定列中某一段的垂直可写视图（列视图），支持跨页。
        /// </summary>
        /// <param name="row">起始行索引。</param>
        /// <param name="col">列索引。</param>
        /// <param name="len">垂直截取长度。</param>
        /// <returns>对应的可写视图对象。</returns>
        new ColumnView<T> GetColumnView(int row, int col, int len);

        /// <summary>
        /// 在指定位置设置单个元素。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">列索引。</param>
        /// <param name="value">要设置的值。</param>
        void SetElement(int r, int c, T value);

        /// <summary>
        /// 批量设置单行截面数据。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">起始列索引。</param>
        /// <param name="data">待写入的一维截面切片。</param>
        void SetRow(int r, int c, ReadOnlySpan<T> data);

        /// <summary>
        /// 批量设置单列时序数据。
        /// </summary>
        /// <param name="r">起始行索引。</param>
        /// <param name="c">列索引。</param>
        /// <param name="data">待写入的一维时序切片。</param>
        void SetColumn(int r, int c, ReadOnlySpan<T> data);

        /// <summary>
        /// 将二维数据块写入指定位置。
        /// </summary>
        /// <param name="r">起始行。</param>
        /// <param name="c">起始列。</param>
        /// <param name="data">待写入的数据块。</param>
        void SetBlock(int r, int c, ReadOnlySpan2D<T> data);
    }
}
