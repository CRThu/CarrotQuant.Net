using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 订单生命周期动作类型。
/// 映射国际金融信息交换标准 FIX Tag 150 (ExecType)。
/// </summary>
public enum OrderEventType : byte
{
    /// <summary>
    /// 订单已提交并进入挂单排队。对齐 FIX Tag 150 = 0 (New)。
    /// </summary>
    Submitted = 0,

    /// <summary>
    /// 订单产生部分或全部成交。对齐 FIX Tag 150 = 1/2 (PartialFill/Fill)。
    /// </summary>
    Fill = 1,

    /// <summary>
    /// 订单已成功撤销。对齐 FIX Tag 150 = 4 (Canceled)。
    /// </summary>
    Canceled = 2,

    /// <summary>
    /// 订单被拒绝（如资金不足、超出涨跌停、流动性限制等）。对齐 FIX Tag 150 = 8 (Rejected)。
    /// </summary>
    Rejected = 3
}

/// <summary>
/// 订单生命周期动作事件不可变记录。
/// 专职负责记录订单从提交、成交、撤销到被拒的全流程事实凭证，用于订单审计与执行归因。
/// </summary>
public sealed record OrderEvent(
    MarketTime Time,
    string OrderId,
    string Symbol,
    OrderDirection Direction,
    OrderType Type,
    double Price,
    decimal Quantity,
    OrderEventType EventType,
    OrderStatus Status,
    decimal FillQuantity = 0m,
    decimal CumQuantity = 0m,
    decimal LeavesQuantity = 0m,
    double FillPrice = 0.0,
    LiquidityRole Role = LiquidityRole.None,
    string Reason = "",
    string OrderTag = ""
);
