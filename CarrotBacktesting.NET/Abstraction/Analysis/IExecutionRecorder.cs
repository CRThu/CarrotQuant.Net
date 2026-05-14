using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 定义交易记录器接口。
/// 负责在回测或实盘过程中采集订单变更、交易流水以及投资组合的净值快照。
/// </summary>
public interface IRecorder
{
    /// <summary>
    /// 记录订单的状态变更。
    /// </summary>
    /// <param name="order">包含订单最新信息的上下文对象。</param>
    void RecordOrder(OrderContext order);

    /// <summary>
    /// 记录一笔完整的交易流水。
    /// </summary>
    /// <param name="trade">包含入场、出场及损益信息的交易上下文对象。</param>
    void RecordTrade(TradeContext trade);

    /// <summary>
    /// 记录当前时间点的投资组合快照，用于后续生成净值曲线及回撤分析。
    /// </summary>
    /// <param name="snapshot">账户资金与持仓的快照数据。</param>
    void RecordSnapshot(PortfolioSnapshot snapshot);

    /// <summary>
    /// 在运行结束时，根据已记录的数据生成综合分析报告。
    /// </summary>
    /// <returns>包含各项量化统计指标的 <see cref="BacktestReport"/>。</returns>
    BacktestReport GetReport();
}

/// <summary>
/// 记录订单执行的快照信息。
/// </summary>
/// <param name="OrderId">订单唯一标识符。</param>
/// <param name="Symbol">交易品种。</param>
/// <param name="Direction">买卖方向。</param>
/// <param name="Price">委托或成交价格。</param>
/// <param name="Volume">委托或成交数量。</param>
/// <param name="Status">订单当前状态。</param>
/// <param name="Time">记录时间。</param>
public record OrderContext(
    string OrderId, 
    string Symbol, 
    OrderDirection Direction, 
    double Price, 
    double Volume, 
    OrderStatus Status, 
    DateTime Time
);

/// <summary>
/// 记录一笔完整交易（开平仓循环）的详细信息。
/// </summary>
/// <param name="TradeId">交易唯一标识符。</param>
/// <param name="Symbol">交易品种。</param>
/// <param name="EntryPrice">入场价格。</param>
/// <param name="ExitPrice">出场价格。</param>
/// <param name="ProfitAndLoss">该笔交易的实现盈亏。</param>
/// <param name="EntryTime">入场时间。</param>
/// <param name="ExitTime">出场时间。</param>
public record TradeContext(
    string TradeId,
    string Symbol,
    double EntryPrice,
    double ExitPrice,
    double ProfitAndLoss,
    DateTime EntryTime,
    DateTime ExitTime
);

/// <summary>
/// 记录特定时间点的投资组合整体状态。
/// </summary>
/// <param name="Time">快照时间。</param>
/// <param name="AvailableCash">当前可用现金。</param>
/// <param name="TotalEquity">账户总净值（含持仓市值）。</param>
/// <param name="OpenPositionsCount">当前持有中的仓位总数。</param>
public record PortfolioSnapshot(
    DateTime Time,
    double AvailableCash,
    double TotalEquity, 
    int OpenPositionsCount
);

/// <summary>
/// 表示最终生成的量化分析报告（占位符，可根据需求扩展指标）。
/// </summary>
public record BacktestReport();
