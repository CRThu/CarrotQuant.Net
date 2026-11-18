namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 全景统一综合分析报告实体。
/// 聚合四层量化统计与信号前向评估结果。
/// </summary>
/// <param name="Facts">底层不可变交易事实账本（交割单、平仓单、净值点、快照）。</param>
/// <param name="Portfolio">第一层：组合净值与宏观风险统计 (Portfolio Statistics)。</param>
/// <param name="Capital">第二层：资本利用与敞口统计 (Capital Statistics)。</param>
/// <param name="Trades">第三层：交易行为与执行归因统计 (Trade Statistics)。</param>
/// <param name="Cost">第四层：现实成本与摩擦损耗统计 (Cost Statistics)。</param>
/// <param name="Signals">独立 Alpha 信号前向质量统计 (Signal Statistics，可选)。</param>
public sealed record AnalysisReport(
    TradingResult Facts,
    PortfolioStatistics Portfolio,
    CapitalStatistics Capital,
    TradeStatistics Trades,
    CostStatistics Cost,
    SignalStatistics? Signals
);
