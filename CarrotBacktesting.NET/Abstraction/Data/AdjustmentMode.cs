namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 行情价格复权模式。
    /// </summary>
    public enum AdjustmentMode
    {
        /// <summary>
        /// 未复权物理价（默认）。
        /// </summary>
        None = 0,

        /// <summary>
        /// 后复权（Post-adjusted）。
        /// </summary>
        Post = 1
    }
}
