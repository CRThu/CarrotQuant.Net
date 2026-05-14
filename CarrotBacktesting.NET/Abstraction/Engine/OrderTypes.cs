namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 订单请求结构
/// </summary>
public record OrderRequest(string Symbol, OrderDirection Direction, OrderType Type, double Price, decimal Quantity);

/// <summary>
/// 交割回报结构（物理成交单）
/// </summary>
public record ExecutionReport(string OrderId, string Symbol, double FillPrice, decimal FillQuantity, double Commission, DateTime Time);

/// <summary>
/// 订单簿快照（简版）
/// </summary>
public class Orderbook
{
    public string Symbol { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public IReadOnlyList<PriceLevel> Bids { get; init; } = Array.Empty<PriceLevel>();
    public IReadOnlyList<PriceLevel> Asks { get; init; } = Array.Empty<PriceLevel>();
}

/// <summary>
/// 价格档位信息
/// </summary>
public record PriceLevel(double Price, decimal Quantity);

/// <summary>
/// 订单方向
/// </summary>
public enum OrderDirection { Buy, Sell }

/// <summary>
/// 订单类型
/// </summary>
public enum OrderType { Market, Limit }

/// <summary>
/// 订单状态机
/// </summary>
public enum OrderStatus 
{ 
    Pending,        // 本地已创建，未发送
    Submitted,      // 已发送至网关/交易所
    PartialFilled,  // 部分成交
    Filled,         // 全部成交
    Canceled,       // 已撤销
    Rejected        // 被交易所拒绝（如余额不足、价格超出涨跌停限制）
}
