using System;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 时序数据表接口，定义单张二维时序矩阵的数据访问与主从时间对齐契约。
    /// </summary>
    public interface ITimeseriesTable
    {
        /// <summary>
        /// 时序表名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取当前表是否为主驱动时钟表。
        /// </summary>
        bool IsPrimary { get; }

        /// <summary>
        /// 获取当前表的行情元数据（交易日历、代码列表及字段定义）。
        /// </summary>
        IMarketMetadata Metadata { get; }

        /// <summary>
        /// 获取指定字段的只读二维矩阵。
        /// </summary>
        /// <typeparam name="T">非托管数值类型。</typeparam>
        /// <param name="fieldName">字段名称。</param>
        /// <returns>二维矩阵视图。</returns>
        IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged;

        /// <summary>
        /// 检查当前表是否包含指定字段。
        /// </summary>
        /// <param name="fieldName">字段名称。</param>
        /// <returns>存在返回 true，否则返回 false。</returns>
        bool HasField(string fieldName);

        /// <summary>
        /// 根据主时钟当前步索引，获取当前表对应的最新有效行索引。
        /// 若为主表则直接返回 primaryStepIndex；若无有效数据则返回 -1。
        /// </summary>
        /// <param name="primaryStepIndex">主时钟当前步索引。</param>
        /// <returns>当前表对应的行索引，若当前步前尚无数据则返回 -1。</returns>
        int GetCurrentRowIndex(int primaryStepIndex);

        /// <summary>
        /// 获取主时钟指定时间步下当前表对应行的横向数据切片。
        /// </summary>
        /// <typeparam name="T">非托管数值类型。</typeparam>
        /// <param name="fieldName">字段名称。</param>
        /// <param name="primaryStepIndex">主时钟当前步索引。</param>
        /// <returns>对应行的标的数据切片；若索引越界或小于 0 则返回空 Span。</returns>
        ReadOnlySpan<T> GetCurrentRow<T>(string fieldName, int primaryStepIndex) where T : unmanaged;

        /// <summary>
        /// 获取主时钟指定时间步下当前表对应标的字段值。
        /// </summary>
        /// <typeparam name="T">非托管数值类型。</typeparam>
        /// <param name="fieldName">字段名称。</param>
        /// <param name="symbolIndex">标的代码列索引。</param>
        /// <param name="primaryStepIndex">主时钟当前步索引。</param>
        /// <returns>字段数值；若索引无效则返回默认值。</returns>
        T GetCurrentValue<T>(string fieldName, int symbolIndex, int primaryStepIndex) where T : unmanaged;
    }
}
