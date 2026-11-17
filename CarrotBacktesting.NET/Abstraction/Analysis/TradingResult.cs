using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 交易账本综合结果模型，包含交割单、已平仓交易、在手持仓、净值曲线、每日快照与策略信号。
/// 回测与实盘通用。
/// </summary>
public sealed class TradingResult
{
    /// <summary>
    /// 券商交割单流水列表。
    /// </summary>
    public IReadOnlyList<SettlementRecord> Settlements { get; init; } = Array.Empty<SettlementRecord>();

    /// <summary>
    /// 已平仓交易结算列表。
    /// </summary>
    public IReadOnlyList<Trade> ClosedTrades { get; init; } = Array.Empty<Trade>();

    /// <summary>
    /// 未平仓持仓快照列表（含未实现收益）。
    /// </summary>
    public IReadOnlyList<PositionSnapshot> OpenPositions { get; init; } = Array.Empty<PositionSnapshot>();

    /// <summary>
    /// 时间步资产净值曲线（含余额、保证金与总浮动盈亏）。
    /// </summary>
    public IReadOnlyList<EquityPoint> EquityCurve { get; init; } = Array.Empty<EquityPoint>();

    /// <summary>
    /// 每日资产与持仓快照列表。
    /// </summary>
    public IReadOnlyList<DailySnapshot> DailySnapshots { get; init; } = Array.Empty<DailySnapshot>();

    /// <summary>
    /// 策略信号记录列表。
    /// </summary>
    public IReadOnlyList<SignalRecord> Signals { get; init; } = Array.Empty<SignalRecord>();

    /// <summary>
    /// 无参构造函数。
    /// </summary>
    public TradingResult() { }

    /// <summary>
    /// 完整要素构造函数。
    /// </summary>
    public TradingResult(
        IReadOnlyList<SettlementRecord> settlements,
        IReadOnlyList<Trade> closedTrades,
        IReadOnlyList<PositionSnapshot> openPositions,
        IReadOnlyList<EquityPoint> equityCurve,
        IReadOnlyList<DailySnapshot> dailySnapshots,
        IReadOnlyList<SignalRecord> signals)
    {
        Settlements = settlements ?? Array.Empty<SettlementRecord>();
        ClosedTrades = closedTrades ?? Array.Empty<Trade>();
        OpenPositions = openPositions ?? Array.Empty<PositionSnapshot>();
        EquityCurve = equityCurve ?? Array.Empty<EquityPoint>();
        DailySnapshots = dailySnapshots ?? Array.Empty<DailySnapshot>();
        Signals = signals ?? Array.Empty<SignalRecord>();
    }
}
