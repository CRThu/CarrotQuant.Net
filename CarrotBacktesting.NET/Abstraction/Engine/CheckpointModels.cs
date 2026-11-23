using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 账本持仓不可变快照模型（专供检查点状态持久化与断点恢复）。
/// </summary>
public sealed record LedgerPositionSnapshot(
    string Symbol,
    decimal TotalQuantity,
    decimal AvailableQuantity,
    decimal FrozenQuantity,
    double AverageCost,
    double OpenCommission,
    double LastPrice,
    MarketTime OpenTime,
    string OpenTag,
    IReadOnlyList<PendingLotSnapshot> PendingLots,
    double OpenSlippageCost = 0.0
);

/// <summary>
/// 在途 T+N 批次快照模型。
/// </summary>
public sealed record PendingLotSnapshot(
    decimal Quantity,
    int RemainingDays,
    MarketTime AvailableDate = default,
    MarketTime LastUpdatedDate = default)
{
    public PendingLotSnapshot(decimal quantity, MarketTime availableDate)
        : this(quantity, 1, availableDate, default) { }
}

/// <summary>
/// 纯内存财务账本不可变状态快照。
/// </summary>
public sealed record AccountLedgerSnapshot(
    double Cash,
    double FrozenCash,
    IReadOnlyList<LedgerPositionSnapshot> Positions,
    TradingStatus Status = TradingStatus.Normal
);

/// <summary>
/// 经纪商在途活跃订单不可变快照。
/// </summary>
public sealed record BrokerOrderSnapshot(
    long OrderId,
    OrderRequest Request,
    OrderStatus Status,
    string? RejectReason,
    double Multiplier,
    double MarginRate,
    int SettleDays,
    decimal CloseQuantity,
    decimal OpenQuantity,
    decimal RemainingCloseQuantity,
    decimal RemainingOpenQuantity,
    decimal FilledQuantity,
    double InitialFrozenCash,
    double RemainingFrozenCash,
    decimal InitialFrozenPosition,
    decimal RemainingFrozenPosition
);

/// <summary>
/// 经纪商层状态快照（聚合纯账本与在途活跃订单）。
/// </summary>
public sealed record BrokerSnapshot(
    AccountLedgerSnapshot Ledger,
    IReadOnlyList<BrokerOrderSnapshot> ActiveOrders
);

/// <summary>
/// 仿真挂单不可变快照。
/// </summary>
public sealed record SimulatedOrderSnapshot(
    long OrderId,
    OrderRequest Request,
    MarketTime CreateTime,
    double Multiplier,
    double MarginRate,
    int SettleDays,
    decimal FilledQuantity = 0m
);

/// <summary>
/// 仿真交易所与挂单簿不可变状态快照。
/// </summary>
public sealed record ExchangeSnapshot(
    IReadOnlyDictionary<string, double> MarketPrices,
    IReadOnlyList<SimulatedOrderSnapshot> PendingOrders
);

/// <summary>
/// 交易事实记录器不可变状态快照。
/// </summary>
public sealed record TradingRecorderSnapshot(
    IReadOnlyList<SettlementRecord> Settlements,
    IReadOnlyList<Trade> ClosedTrades,
    IReadOnlyList<EquityPoint> EquityCurve,
    IReadOnlyList<DailySnapshot> DailySnapshots,
    IReadOnlyList<SignalRecord> Signals,
    IReadOnlyList<OrderEvent>? OrderEvents = null
);

/// <summary>
/// 市场黑板自描述条目快照模型。
/// 单点权威统筹条目键名、完整类型限定名、JSON 数据与更新时间戳，
/// 100% 保持原生类型精度与保真度，天然支持标量、record、struct、class 及复杂集合。
/// </summary>
public sealed record MarketStateEntrySnapshot(
    string Key,
    string Type,
    string Data,
    MarketTime? Time = null
);

/// <summary>
/// 市场黑板不可变状态快照。
/// 基于统一自描述条目集合构建，遵循单一事实来源，消除类型穷举与字典分裂。
/// </summary>
public sealed record MarketStateSnapshot(
    IReadOnlyList<MarketStateEntrySnapshot> Entries
);

/// <summary>
/// 回测引擎检查点状态聚合根，聚合调度时钟进度、经纪商账本、挂单撮合网关、交易流水记录器与黑板状态快照。
/// </summary>
public sealed record EngineCheckpointState(
    int Version,
    int CurrentIndex,
    MarketTime CurrentTime,
    MarketTime LastDate,
    double Progress,
    BrokerSnapshot Broker,
    ExchangeSnapshot Exchange,
    TradingRecorderSnapshot? Recorder = null,
    MarketStateSnapshot? Market = null
)
{
    /// <summary>
    /// 便捷访问账本状态（等价于 Broker.Ledger）。
    /// </summary>
    [JsonIgnore]
    public AccountLedgerSnapshot Ledger => Broker.Ledger;

    /// <summary>
    /// 便捷访问已平仓交易列表（等价于 Recorder.ClosedTrades）。
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<Trade> ClosedTrades => Recorder?.ClosedTrades ?? Array.Empty<Trade>();
}
