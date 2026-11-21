namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 策略决策与更新时点。
/// </summary>
public enum StrategyUpdateTiming : byte
{
    /// <summary>
    /// 收盘更新（默认推荐，绝大多数策略适用）。
    /// 当前时间步 Bar 完全走完时驱动 OnUpdate，策略可读取当期完整 OHLCV 与收盘指标。
    /// </summary>
    OnClose = 0,

    /// <summary>
    /// 开盘更新（开盘跳空、集合竞价选股、日内算法拆单策略）。
    /// 在当前时间步 Bar 刚开盘时驱动 OnUpdate，策略可读取当期 Open 与历史已完结 Bar。
    /// </summary>
    OnOpen = 1
}

/// <summary>
/// 撮合成交价格基准（回测环境与经纪商执行规则配置）。
/// </summary>
public enum ExecutionPriceType : byte
{
    /// <summary>
    /// 开盘价成交（默认推荐，对齐国际主流框架标准）。
    /// OnOpen 模式下以当期 Open 撮合；OnClose 模式下以次日 Open 撮合。
    /// </summary>
    Open = 0,

    /// <summary>
    /// 收盘价成交。
    /// 假设尾盘 14:55 决策并参与收盘集合竞价，以当期 Close 撮合。
    /// </summary>
    Close = 1,

    /// <summary>
    /// 全日成交量加权均价成交（大资金机构回测标准，行业通用缩写：VWAP）。
    /// OnOpen 模式下以当期 VWAP 拆单；OnClose 模式下以次日 VWAP 拆单。
    /// 契约约束：若数据源缺失 vwap 且无法通过 amount/volume 计算，显式抛出强类型异常，坚决杜绝隐式降级伪实现！
    /// </summary>
    Vwap = 2,

    /// <summary>
    /// 全日均价成交 (Open + High + Low + Close) / 4。
    /// 供用户主动显式配置的均价拆单基准，严禁作为 VWAP 的隐式兜底替身。
    /// </summary>
    Average = 3
}

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
    /// 唯一订单标识符（0 表示未分配，由 Broker 自动原子自增生成；>0 为外部显式指定用于幂等去重）。
    /// </summary>
    public long OrderId { get; init; } = 0;

    /// <summary>
    /// 条件单触发价（止损单/止盈单有效）。
    /// </summary>
    public double StopPrice { get; init; } = 0.0;

    /// <summary>
    /// 互斥订单组标识（OCO Bracket 组号，0 表示独立订单非 OCO 组）。
    /// </summary>
    public long OcoGroupId { get; init; } = 0;
}

/// <summary>
/// 成交流动性角色标识。
/// </summary>
public enum LiquidityRole : byte
{
    /// <summary>
    /// 未定义或非撮合角色。
    /// </summary>
    None = 0,

    /// <summary>
    /// 被动挂单方（Maker / 提供流动性）。
    /// 在挂单簿中排队被动撮合，承担零滑点，享受开盘跳空价格改善。对齐 FIX Tag 851 = 1。
    /// </summary>
    Maker = 1,

    /// <summary>
    /// 主动吃单方（Taker / 消耗流动性）。
    /// 市价单或穿透限价单即时成交，承担物理滑点成本。对齐 FIX Tag 851 = 2。
    /// </summary>
    Taker = 2
}

/// <summary>
/// 表示一笔成交的执行回报，包含物理成交数据与生命周期进度凭证。
/// </summary>
/// <param name="OrderId">原始订单 ID。</param>
/// <param name="Symbol">成交品种代码。</param>
/// <param name="FillPrice">实际成交价格。</param>
/// <param name="FillQuantity">本笔实际成交数量（FIX Tag 32 LastQty）。</param>
/// <param name="Commission">该笔成交产生的手续费。</param>
/// <param name="Time">成交时间。</param>
/// <param name="CumQuantity">该订单累计已成交数量（FIX Tag 14 CumQty）。</param>
/// <param name="LeavesQuantity">该订单在挂单簿中剩余排队数量（FIX Tag 151 LeavesQty）。</param>
/// <param name="Role">成交流动性角色标识（FIX Tag 851）。</param>
/// <param name="Slippage">该笔成交产生的实际滑点损耗金额。</param>
public record ExecutionReport(
    long OrderId,
    string Symbol,
    double FillPrice,
    decimal FillQuantity,
    double Commission,
    MarketTime Time,
    decimal CumQuantity = 0m,
    decimal LeavesQuantity = 0m,
    LiquidityRole Role = LiquidityRole.None,
    double Slippage = 0.0);

/// <summary>
/// 表示网关或交易所订单生命周期状态变更的权威报告。
/// 对齐 FIX 协议状态回报模型，用于精准传递报单确认、撤单、拒单及排队状态。
/// </summary>
/// <param name="OrderId">系统或券商内部订单标识符（对齐 FIX Tag 37 OrderID）。</param>
/// <param name="Status">目标生命周期状态（对齐 FIX Tag 39 OrdStatus）。</param>
/// <param name="Time">状态变更发生的时间戳（对齐 FIX Tag 60 TransactTime）。</param>
/// <param name="Symbol">交易品种代码（对齐 FIX Tag 55 Symbol）。</param>
/// <param name="Reason">状态变更原因说明（如废单、拒单或撤单原因，对齐 FIX Tag 58 Text）。</param>
/// <param name="LeavesQuantity">订单在挂单簿中剩余排队数量（对齐 FIX Tag 151 LeavesQty）。</param>
/// <param name="ErrorCode">网关或交易所原始数字错误代码（对齐 FIX Tag 103 OrdRejReason）。</param>
public record OrderStateReport(
    long OrderId,
    OrderStatus Status,
    MarketTime Time,
    string Symbol = "",
    string Reason = "",
    decimal LeavesQuantity = 0m,
    int ErrorCode = 0);

/// <summary>
/// 表示持仓变更事件。
/// </summary>
/// <param name="Symbol">品种代码。</param>
/// <param name="NewQuantity">调整后的新持仓数量。</param>
/// <param name="AveragePrice">当前持仓的平均成本价（可选）。</param>
/// <param name="Time">变更发生的时间。</param>
public record PositionEvent(string Symbol, decimal NewQuantity, double AveragePrice, MarketTime Time);

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
/// 订单基础执行类型（对齐 FIX Protocol Tag 40 标准）。
/// </summary>
public enum OrderType : byte
{ 
    /// <summary>
    /// 市价单：以当前市场最优价格立即成交（Taker）。对齐 FIX Tag 40 = 1。
    /// </summary>
    Market = 0, 

    /// <summary>
    /// 限价单：以指定价格或更优价格成交（未触碰时在挂单簿排队，Maker）。对齐 FIX Tag 40 = 2。
    /// </summary>
    Limit = 1,

    /// <summary>
    /// 止损市价单：市场行情跌穿/涨破 StopPrice 时触发，转为市价单打出（Taker）。
    /// 行业缩写：STOP，对齐 FIX Tag 40 = 3 (Stop / Stop Loss)。
    /// </summary>
    Stop = 2,

    /// <summary>
    /// 止盈市价单：市场行情冲高/回调触碰 StopPrice 时触发，转为市价单打出（Taker）。
    /// 行业缩写：TP，对齐 FIX Tag 40 = J (Market If Touched，缩写 MIT)。
    /// </summary>
    TakeProfit = 3,

    /// <summary>
    /// 止损限价单：触发后转为限价单挂单排队（Maker）。对齐 FIX Tag 40 = 4。
    /// </summary>
    StopLimit = 4,

    /// <summary>
    /// 止盈限价单：触发后转为限价单挂单排队（Maker）。对齐 FIX Tag 40 = K (Limit If Touched，缩写 LIT)。
    /// </summary>
    TakeProfitLimit = 5
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
