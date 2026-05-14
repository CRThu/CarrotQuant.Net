using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 交易执行记录器。负责收集订单、交易、资金曲线快照，并生成最终分析报告。
/// </summary>
public interface IRecorder
{
    // --- 记录方法 (Write) ---
    
    /// <summary>
    /// 记录订单状态变更（如：已提交、已成交、已撤销）
    /// </summary>
    void RecordOrder(OrderContext order);

    /// <summary>
    /// 记录一笔完整的交易流水（开仓 -> 平仓的闭环，或者单边成交记录）
    /// </summary>
    void RecordTrade(TradeContext trade);

    /// <summary>
    /// 记录每日/每 Tick 的账户资金与持仓快照（用于画净值曲线）
    /// </summary>
    void RecordSnapshot(PortfolioSnapshot snapshot);

    // --- 导出方法 (Read) ---

    /// <summary>
    /// 回测/实盘结束时，生成并获取综合分析报告
    /// </summary>
    /// <returns>包含胜率、盈亏比、最大回撤等指标的结果对象</returns>
    BacktestReport GetReport();
}

/// <summary>
/// 订单上下文记录
/// </summary>
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
/// 交易上下文记录
/// </summary>
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
/// 投资组合快照（用于计算每日净值、最大回撤）
/// </summary>
public record PortfolioSnapshot(
    DateTime Time,
    double AvailableCash,
    double TotalEquity, // 总净值
    int OpenPositionsCount
);

/// <summary>
/// 回测报告结果（占位符，后续可扩展）
/// </summary>
public record BacktestReport();
