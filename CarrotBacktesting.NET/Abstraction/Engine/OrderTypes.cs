namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 表示一个订单请求，包含了下单所需的全部核心参数。
/// </summary>
/// <param name="Symbol">交易品种代码。</param>
/// <param name="Direction">订单方向（买/卖）。</param>
/// <param name="Type">订单类型（市价/限价）。</param>
/// <param name="Price">委托价格。对于市价单，此值通常忽略。</param>
/// <param name="Quantity">委托数量。</param>
public record OrderRequest(string Symbol, OrderDirection Direction, OrderType Type, double Price, decimal Quantity);

/// <summary>
/// 表示一笔成交的执行回报，包含了物理成交的详细信息。
/// </summary>
/// <param name="OrderId">原始订单 ID。</param>
/// <param name="Symbol">成交品种代码。</param>
/// <param name="FillPrice">实际成交价格。</param>
/// <param name="FillQuantity">实际成交数量。</param>
/// <param name="Commission">该笔成交产生的手续费。</param>
/// <param name="Time">成交时间。</param>
public record ExecutionReport(string OrderId, string Symbol, double FillPrice, decimal FillQuantity, double Commission, DateTime Time);

/// <summary>
/// 表示一个交易品种的实时订单簿（Depth）快照。
/// </summary>
public class Orderbook
{
    /// <summary>
    /// 获取或设置品种代码。
    /// </summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置该快照产生的时间戳。
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 获取或设置买盘档位列表。
    /// </summary>
    public IReadOnlyList<PriceLevel> Bids { get; init; } = Array.Empty<PriceLevel>();

    /// <summary>
    /// 获取或设置卖盘档位列表。
    /// </summary>
    public IReadOnlyList<PriceLevel> Asks { get; init; } = Array.Empty<PriceLevel>();
}

/// <summary>
/// 表示订单簿中的一个价格档位。
/// </summary>
/// <param name="Price">在该档位的挂单价格。</param>
/// <param name="Quantity">在该档位的挂单总量。</param>
public record PriceLevel(double Price, decimal Quantity);

/// <summary>
/// 定义订单的买卖方向。
/// </summary>
public enum OrderDirection 
{ 
    /// <summary>
    /// 买入。
    /// </summary>
    Buy, 

    /// <summary>
    /// 卖出。
    /// </summary>
    Sell 
}

/// <summary>
/// 定义订单的执行策略类型。
/// </summary>
public enum OrderType 
{ 
    /// <summary>
    /// 市价单：以当前市场最优价格立即成交。
    /// </summary>
    Market, 

    /// <summary>
    /// 限价单：以指定价格或更好的价格成交。
    /// </summary>
    Limit 
}

/// <summary>
/// 表示订单在生命周期中的当前状态。
/// </summary>
public enum OrderStatus 
{ 
    /// <summary>
    /// 订单已在本地创建，尚未发送至网关或交易所。
    /// </summary>
    Pending,

    /// <summary>
    /// 订单已成功提交并发送至网关或交易所。
    /// </summary>
    Submitted,

    /// <summary>
    /// 订单已部分成交。
    /// </summary>
    PartialFilled,

    /// <summary>
    /// 订单已全部成交。
    /// </summary>
    Filled,

    /// <summary>
    /// 用户已成功撤销订单。
    /// </summary>
    Canceled,

    /// <summary>
    /// 订单已被网关或交易所拒绝（例如：资金不足、超出涨跌停等）。
    /// </summary>
    Rejected
}
