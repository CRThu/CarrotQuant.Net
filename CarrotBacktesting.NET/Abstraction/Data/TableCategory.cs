namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 数据表的业务分类。
    /// </summary>
    public enum TableCategory
    {
        /// <summary>
        /// 连续时序数据（矩阵模态，稠密对齐）。
        /// </summary>
        Timeseries,

        /// <summary>
        /// 离散事件数据（KV/稀疏模态，事件驱动）。
        /// </summary>
        Event,

        /// <summary>
        /// 标的静态维表（无时间戳维表，属性标签）。
        /// </summary>
        Field
    }
}
