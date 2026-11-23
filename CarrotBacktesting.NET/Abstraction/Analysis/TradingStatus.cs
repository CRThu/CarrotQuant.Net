namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 交易会话与账户运行状态枚举。
/// </summary>
public enum TradingStatus
{
    /// <summary>
    /// 正常运行或正常完成状态。
    /// </summary>
    Normal = 0,

    /// <summary>
    /// 穿仓破产状态，账户总权益小于等于零并执行强平清仓。
    /// </summary>
    Bankrupt = 1,

    /// <summary>
    /// 异常中断或中止退出状态。
    /// </summary>
    Aborted = 2
}
