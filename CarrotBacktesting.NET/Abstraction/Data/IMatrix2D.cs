using System;
using CarrotBacktesting.NET.Data.Views;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 提供对二维时序矩阵的可读写访问契约。
    /// 继承自 <see cref="IReadOnlyMatrix2D{T}"/>，并提供原位修改与向量化批量填充能力。
    /// </summary>
    /// <typeparam name="T">存储的数据类型，必须为非托管类型。</typeparam>
    public interface IMatrix2D<T> : IReadOnlyMatrix2D<T>, IDisposable where T : unmanaged
    {
        /// <summary>
        /// 获取指定行号和列号的数据可写引用。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">列索引。</param>
        /// <returns>数据的可写引用。</returns>
        new ref T this[int r, int c] { get; }

        /// <summary>
        /// 获取指定行中某一段的水平可写视图（截面视图）。
        /// </summary>
        /// <param name="row">起始行索引。</param>
        /// <param name="col">起始列索引。</param>
        /// <param name="len">截取长度。</param>
        /// <returns>对应的可写视图对象。</returns>
        new RowView<T> GetRowView(int row, int col, int len);

        /// <summary>
        /// 获取指定列中某一段的垂直可写视图（序列视图），支持跨页连续寻址。
        /// </summary>
        /// <param name="row">起始行索引。</param>
        /// <param name="col">列索引。</param>
        /// <param name="len">垂直截取长度。</param>
        /// <returns>对应的可写视图对象。</returns>
        new ColumnView<T> GetColumnView(int row, int col, int len);

        /// <summary>
        /// 在指定行列坐标设置单个元素。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">列索引。</param>
        /// <param name="value">要设置的值。</param>
        void SetElement(int r, int c, T value);

        /// <summary>
        /// 批量写入单行截面数据。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">起始列索引。</param>
        /// <param name="data">待写入的一维截面切片。</param>
        void SetRow(int r, int c, ReadOnlySpan<T> data);

        /// <summary>
        /// 批量写入单列时序数据。
        /// </summary>
        /// <param name="r">起始行索引。</param>
        /// <param name="c">列索引。</param>
        /// <param name="data">待写入的一维时序切片。</param>
        void SetColumn(int r, int c, ReadOnlySpan<T> data);

        /// <summary>
        /// 原子拓宽矩阵列维度（新标的上市扩列）。
        /// </summary>
        /// <param name="newWidth">新列总数。</param>
        void ExpandWidth(int newWidth);
    }
}
