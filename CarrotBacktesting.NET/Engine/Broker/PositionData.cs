using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Engine.Broker;

/// <summary>
/// 标的持仓数据聚合实体。
/// 专职维护单个交易标的的多空持仓数量、可用数量、挂单冻结数量、加权持仓成本与在途 T+N 交收队列。
/// </summary>
internal sealed class PositionData
{
    public string Symbol { get; }

    /// <summary>
    /// 总持仓数量（正数为多头，负数为虚拟合约空头，0 为无持仓）。
    /// </summary>
    public decimal TotalQuantity { get; set; }

    /// <summary>
    /// 可用持仓数量（受 T+N 结算和挂单平仓冻结约束，卖出/平仓时受此数量限制）。
    /// </summary>
    public decimal AvailableQuantity { get; set; }

    /// <summary>
    /// 当前因挂单平仓而预冻结的持仓数量。
    /// </summary>
    public decimal FrozenQuantity { get; set; }

    /// <summary>
    /// 当前持仓的加权平均买入/开仓成本价。
    /// </summary>
    public double AverageCost { get; set; }

    /// <summary>
    /// 该标的的最新行情基准价（用于资产盯市估值）。
    /// </summary>
    public double LastPrice { get; set; }

    /// <summary>
    /// 在途 T+N 交收批次队列（如 A 股 T+1 买入后在此排队等待换日解锁）。
    /// </summary>
    public List<PendingLot> PendingLots { get; } = new();

    public PositionData(string symbol)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
    }
}

/// <summary>
/// 在途交收份额批次模型。
/// </summary>
internal sealed record PendingLot(decimal Quantity, DateTime AvailableDate);
