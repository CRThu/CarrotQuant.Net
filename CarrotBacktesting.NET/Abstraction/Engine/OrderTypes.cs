namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 订单时效性与生命周期规则。
/// 严格收敛为全市场日频与分钟频回测的核心时效基元，杜绝无盘口深度下的伪高频实现。
/// </summary>
public enum TimeInForce : byte
{
    /// <summary>
    /// 当日有效（Day Order，默认）。收盘或日切结算时未成交挂单集中自动撤销并解冻资产。
    /// 物理对齐：A 股现货、国内期货默认规则，FIX Tag 59 = 0。
    /// </summary>
    Day = 0,

    /// <summary>
    /// 撤单前有效（Good 'Til Canceled）。跨交易日持续在挂单簿排队，直到完全成交或主动撤销。
    /// 物理对齐：美股、加密货币、网格长线挂单规则，FIX Tag 59 = 1。
    /// </summary>
    GoodTilCanceled = 1
}

/// <summary>
/// 表示一个订单请求，包含了下单所需的全部核心参数。
/// </summary>
/// <param name="Symbol">交易品种代码。</param>
/// <param name="Direction">订单方向（买/卖）。</param>
/// <param name="Type">订单类型（市价/限价）。</param>
/// <param name="Price">委托价格。对于市价单，此值通常忽略。</param>
/// <param name="Quantity">委托数量。</param>
/// <param name="OrderTag">策略业务标签（如开仓理由、特征 Tag），用于订单归因与成交流水追踪。</param>
/// <param name="TimeInForce">订单时效性。缺省为 null 时由经纪商配置 DefaultTimeInForce 统一确定。</param>
[method: System.Text.Json.Serialization.JsonConstructor]
public record OrderRequest(
    string Symbol,
    OrderDirection Direction,
    OrderType Type,
    double Price,
    decimal Quantity,
    string OrderTag = "",
    TimeInForce? TimeInForce = null)
{
    /// <summary>
    /// 市价单便捷构造函数（价格默认为 0.0）。
    /// </summary>
    public OrderRequest(string symbol, OrderDirection direction, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
        : this(symbol, direction, OrderType.Market, 0.0, quantity, orderTag, timeInForce)
    {
    }

    /// <summary>
    /// 限价单便捷构造函数。
    /// </summary>
    public OrderRequest(string symbol, OrderDirection direction, double price, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
        : this(symbol, direction, OrderType.Limit, price, quantity, orderTag, timeInForce)
    {
    }

    /// <summary>
    /// 是否为买入订单。
    /// </summary>
    public bool IsBuy => Direction == OrderDirection.Buy;

    /// <summary>
    /// 是否为卖出订单。
    /// </summary>
    public bool IsSell => Direction == OrderDirection.Sell;

    /// <summary>
    /// 本地客户端订单 ID（UUID），用于防重推、去重和实盘柜台唯一映射。若未指定可由系统自动生成。
    /// </summary>
    public string ClientOrderId { get; init; } = string.Empty;

    /// <summary>
    /// 条件单触发价（止损单/止盈单有效，阶段三基元预留）。
    /// </summary>
    public double StopPrice { get; init; } = 0.0;
}

/// <summary>
/// 表示一笔成交的执行回报，包含了物理成交的详细信息。
/// </summary>
/// <param name="OrderId">原始订单 ID。</param>
/// <param name="Symbol">成交品种代码。</param>
/// <param name="FillPrice">实际成交价格。</param>
/// <param name="FillQuantity">实际成交数量。</param>
/// <param name="Commission">该笔成交产生的手续费。</param>
/// <param name="Time">成交时间。</param>
public record ExecutionReport(string OrderId, string Symbol, double FillPrice, decimal FillQuantity, double Commission, MarketTime Time);

/// <summary>
/// 表示持仓变更事件。
/// </summary>
/// <param name="Symbol">品种代码。</param>
/// <param name="NewQuantity">调整后的新持仓数量。</param>
/// <param name="AveragePrice">当前持仓的平均成本价（可选）。</param>
/// <param name="Time">变更发生的时间。</param>
public record PositionEvent(string Symbol, decimal NewQuantity, double AveragePrice, MarketTime Time);

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
    public MarketTime Timestamp { get; init; }

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
