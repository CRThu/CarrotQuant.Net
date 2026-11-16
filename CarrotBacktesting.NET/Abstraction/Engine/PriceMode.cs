namespace CarrotBacktesting.NET.Abstraction.Engine
{
    /// <summary>
    /// 价格模式枚举。
    /// 定义策略与系统在访问行情数据时的复权口径。
    /// </summary>
    public enum PriceMode : byte
    {
        /// <summary>
        /// 物理未复权价格（底层原始行情形态，用于交易所撮合、滑点与资产账本核算）。
        /// </summary>
        Raw = 0,

        /// <summary>
        /// 后复权价格（消除历史除权除息跳空的平滑价，用于特征工程、技术指标与信号生产）。
        /// </summary>
        Post = 1
    }
}
