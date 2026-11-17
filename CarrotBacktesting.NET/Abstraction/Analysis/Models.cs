using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 交易方向。
/// 0 代表多头（Long），1 代表空头（Short）。
/// </summary>
public enum TradeSide : byte
{
    /// <summary>
    /// 做多（买开平卖）。
    /// </summary>
    Long = 0,

    /// <summary>
    /// 做空（卖开平买）。
    /// </summary>
    Short = 1
}

/// <summary>
/// 时间步资产与保证金净值点。
/// </summary>
/// <param name="Time">时间戳。</param>
/// <param name="TotalEquity">当前总动态权益。</param>
/// <param name="AvailableCash">当前可用现金。</param>
/// <param name="FrozenCash">当前挂单冻结资金。</param>
/// <param name="OccupiedMargin">当前占用保证金总额。</param>
/// <param name="TotalMarketValue">持仓总名义市值。</param>
/// <param name="UnrealizedPnL">全账户持仓未实现收益（总浮动盈亏）。</param>
public readonly record struct EquityPoint(
    DateTime Time,
    double TotalEquity,
    double AvailableCash,
    double FrozenCash,
    double OccupiedMargin,
    double TotalMarketValue,
    double UnrealizedPnL
)
{
    /// <summary>
    /// 保证金占用率（OccupiedMargin / TotalEquity）。
    /// </summary>
    public double MarginRatio => TotalEquity > 0 ? OccupiedMargin / TotalEquity : 0.0;
}

/// <summary>
/// 单标的持仓与未实现收益快照。
/// </summary>
/// <param name="Symbol">标的代码。</param>
/// <param name="Side">持仓方向（多头/空头）。</param>
/// <param name="Quantity">当前持仓总数量。</param>
/// <param name="AvailableQuantity">当前可用数量。</param>
/// <param name="FrozenQuantity">当前挂单冻结数量。</param>
/// <param name="CostPrice">持仓成本均价。</param>
/// <param name="CurrentPrice">当期盯市价格。</param>
/// <param name="Multiplier">合约乘数。</param>
/// <param name="MarketValue">盯市市值。</param>
/// <param name="UnrealizedPnL">该持仓未实现收益（浮动盈亏）。</param>
/// <param name="MarginRate">标的保证金率。</param>
/// <param name="OccupiedMargin">该持仓实际占用的保证金。</param>
/// <param name="Weight">占总资产净值的权重比例。</param>
public sealed record PositionSnapshot(
    string Symbol,
    TradeSide Side,
    decimal Quantity,
    decimal AvailableQuantity,
    decimal FrozenQuantity,
    double CostPrice,
    double CurrentPrice,
    double Multiplier,
    double MarketValue,
    double UnrealizedPnL,
    double MarginRate,
    double OccupiedMargin,
    double Weight
);

/// <summary>
/// 日终全景资产与持仓快照（用于跨日归因、行业敞口分析与实盘对账）。
/// </summary>
/// <param name="Time">快照时间戳（通常为日终收盘时间）。</param>
/// <param name="TotalEquity">总资产权益。</param>
/// <param name="AvailableCash">可用资金。</param>
/// <param name="FrozenCash">挂单冻结资金。</param>
/// <param name="OccupiedMargin">占用保证金。</param>
/// <param name="TotalMarketValue">持仓总市值。</param>
/// <param name="UnrealizedPnL">全账户未实现收益（总浮盈）。</param>
/// <param name="Positions">当前持仓标的明细集合。</param>
public sealed record DailySnapshot(
    DateTime Time,
    double TotalEquity,
    double AvailableCash,
    double FrozenCash,
    double OccupiedMargin,
    double TotalMarketValue,
    double UnrealizedPnL,
    IReadOnlyList<PositionSnapshot> Positions
)
{
    /// <summary>
    /// 保证金占用率（OccupiedMargin / TotalEquity）。
    /// </summary>
    public double MarginRatio => TotalEquity > 0 ? OccupiedMargin / TotalEquity : 0.0;

    /// <summary>
    /// 当前持仓标的总只数。
    /// </summary>
    public int PositionCount => Positions.Count;
}

/// <summary>
/// 券商交割单记录，记录物理成交后的账目交割与发生后资金余额。
/// </summary>
/// <param name="Time">交割发生时间。</param>
/// <param name="Symbol">标的代码。</param>
/// <param name="OrderId">关联订单号。</param>
/// <param name="Direction">买卖方向。</param>
/// <param name="Price">成交价格。</param>
/// <param name="Quantity">成交数量。</param>
/// <param name="Turnover">成交金额。</param>
/// <param name="Commission">手续费与税费。</param>
/// <param name="RealizedPnL">本次平仓产生的已实现盈亏（开仓为 0）。</param>
/// <param name="BalanceCash">交割后可用现金余额。</param>
/// <param name="OrderTag">策略归因标签。</param>
public sealed record SettlementRecord(
    DateTime Time,
    string Symbol,
    string OrderId,
    OrderDirection Direction,
    double Price,
    decimal Quantity,
    double Turnover,
    double Commission,
    double RealizedPnL,
    double BalanceCash,
    string OrderTag
);

/// <summary>
/// 开平仓配对交易记录。
/// </summary>
/// <param name="Symbol">标的代码。</param>
/// <param name="Side">交易方向。</param>
/// <param name="EntryTime">开仓成交时间。</param>
/// <param name="EntryPrice">开仓成交均价。</param>
/// <param name="ExitTime">平仓成交时间。</param>
/// <param name="ExitPrice">平仓成交均价。</param>
/// <param name="Quantity">配对平仓数量。</param>
/// <param name="Multiplier">合约乘数。</param>
/// <param name="RealizedPnL">实现净盈亏（扣除手续费与滑点）。</param>
/// <param name="ReturnRate">该笔交易收益率。</param>
/// <param name="Commission">分配的手续费。</param>
/// <param name="SlippageCost">分配的滑点损耗。</param>
/// <param name="HoldingSteps">持有时间步数。</param>
/// <param name="EntryTag">开仓标签。</param>
/// <param name="ExitTag">平仓标签。</param>
public sealed record Trade(
    string Symbol,
    TradeSide Side,
    DateTime EntryTime,
    double EntryPrice,
    DateTime ExitTime,
    double ExitPrice,
    decimal Quantity,
    double Multiplier,
    double RealizedPnL,
    double ReturnRate,
    double Commission,
    double SlippageCost,
    int HoldingSteps,
    string EntryTag,
    string ExitTag
);

/// <summary>
/// 策略决策信号记录。
/// </summary>
/// <param name="Time">信号发生时间。</param>
/// <param name="Symbol">标的代码。</param>
/// <param name="SignalValue">信号数值。</param>
/// <param name="SignalGroup">信号组别或分类。</param>
/// <param name="Reason">信号触发原因。</param>
public sealed record SignalRecord(
    DateTime Time,
    string Symbol,
    double SignalValue,
    string SignalGroup,
    string Reason
);

/// <summary>
/// 成交执行详情事件，包含订单买卖方向、乘数与归因标签。
/// </summary>
/// <param name="Report">撮合成交回报。</param>
/// <param name="Direction">订单买卖方向。</param>
/// <param name="Multiplier">合约乘数。</param>
/// <param name="OrderTag">订单附加的归因标签。</param>
public readonly record struct TradeExecutionDetails(
    ExecutionReport Report,
    OrderDirection Direction,
    double Multiplier,
    string OrderTag
);
