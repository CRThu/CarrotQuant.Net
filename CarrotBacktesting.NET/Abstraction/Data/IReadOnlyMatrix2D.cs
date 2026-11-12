using System;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 提供对二维时序矩阵的只读访问契约。
    /// 通过 ref readonly 索引器确保物理层面的只读隔离与零拷贝访问。
    /// </summary>
    /// <typeparam name="T">存储的数据类型，必须为非托管类型。</typeparam>
    public interface IReadOnlyMatrix2D<T> where T : unmanaged
    {
        /// <summary>
        /// 获取当前矩阵已存储的总行数（时间步/交易日）。
        /// </summary>
        int RowCount { get; }

        /// <summary>
        /// 获取每一行的固定宽度（列数，代码总数）。
        /// </summary>
        int Width { get; }

        /// <summary>
        /// 获取指定行号和列号的数据只读引用（无锁快速通道）。
        /// </summary>
        /// <param name="r">行索引。</param>
        /// <param name="c">列索引。</param>
        /// <returns>数据的只读引用。</returns>
        ref readonly T this[int r, int c] { get; }

        /// <summary>
        /// 获取指定行中某一段的水平只读视图（当日全市场截面）。
        /// </summary>
        /// <param name="row">起始行索引。</param>
        /// <param name="col">起始列索引。</param>
        /// <param name="len">截取长度。</param>
        /// <returns>对应的只读行视图对象。</returns>
        ReadOnlyRowView<T> GetRowView(int row, int col, int len);

        /// <summary>
        /// 获取指定列中某一段的垂直只读视图（单只标的时序序列），支持跨页连续寻址。
        /// </summary>
        /// <param name="row">起始行索引。</param>
        /// <param name="col">列索引。</param>
        /// <param name="len">垂直截取长度。</param>
        /// <returns>对应的只读列视图对象。</returns>
        ReadOnlyColumnView<T> GetColumnView(int row, int col, int len);
    }
}
