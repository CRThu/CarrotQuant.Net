namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义经纪商（Broker）接口。
/// 面向策略层暴露，负责执行风控校验、订单路由、资金管理以及维护持仓状态。
/// 统一单一内核，支持回测模拟网关（SimulatedExchangeGateway）与实盘网关（CtpGateway）一致性切换。
/// </summary>
public interface IBroker
{
    /// <summary>
    /// 获取当前账户的可提可用现金金额。
    /// </summary>
    /// <returns>可用现金金额。</returns>
    double GetCash();

    /// <summary>
    /// 获取当前账户可用现金。
    /// </summary>
    double AvailableCash => GetCash();

    /// <summary>
    /// 获取当前账户的总动态资产权益（可用现金 + 持仓浮动市值/占用保证金 + 未实现盈亏）。
    /// </summary>
    /// <returns>账户总权益。</returns>
    double GetTotalEquity();

    /// <summary>
    /// 获取指定品种的当前持仓数量。
    /// </summary>
    /// <param name="symbol">品种代码。</param>
    /// <returns>持仓数量（正数为多头，负数为虚拟合约空头，0 为无持仓）。</returns>
    decimal GetPosition(string symbol);

    /// <summary>
    /// 获取指定品种的当前可用持仓数量（受 T+N 结算约束，卖出/平仓时受此数量限制）。
    /// </summary>
    /// <param name="symbol">品种代码。</param>
    /// <returns>可用持仓数量。</returns>
    decimal GetAvailablePosition(string symbol);

    /// <summary>
    /// 获取指定品种的加权平均持仓成本价（无持仓时返回 0.0）。
    /// </summary>
    /// <param name="symbol">品种代码。</param>
    /// <returns>加权平均持仓成本价。</returns>
    double GetPositionCost(string symbol);

    /// <summary>
    /// 获取当前账户中所有活跃持仓的只读快照字典。
    /// </summary>
    /// <returns>代码与持仓数量的映射字典。</returns>
    IReadOnlyDictionary<string, decimal> GetPositions();

    /// <summary>
    /// 提交一个新的订单请求。
    /// </summary>
    /// <param name="request">订单请求参数。</param>
    /// <returns>由经纪商系统内部生成的唯一订单 ID。</returns>
    string SubmitOrder(OrderRequest request);

    /// <summary>
    /// 便捷提交买入订单（默认以市价单形式发出）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="quantity">买入数量（股数/张数）。</param>
    /// <param name="price">委托价格（市价单缺省为 0.0）。</param>
    /// <param name="type">订单类型（缺省为市价单）。</param>
    /// <param name="clientOrderId">自定义客户端订单标识（可选）。</param>
    /// <param name="orderTag">自定义订单业务标签（可选）。</param>
    /// <param name="timeInForce">订单时效性（可选，缺省由配置 DefaultTimeInForce 确定）。</param>
    /// <returns>分配的唯一订单标识 ID。</returns>
    string Buy(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "", TimeInForce? timeInForce = null)
        => SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, type, price, quantity, orderTag, timeInForce) { ClientOrderId = clientOrderId ?? string.Empty });

    /// <summary>
    /// 便捷提交卖出订单（默认以市价单形式发出）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="quantity">卖出数量（股数/张数）。</param>
    /// <param name="price">委托价格（市价单缺省为 0.0）。</param>
    /// <param name="type">订单类型（缺省为市价单）。</param>
    /// <param name="clientOrderId">自定义客户端订单标识（可选）。</param>
    /// <param name="orderTag">自定义订单业务标签（可选）。</param>
    /// <param name="timeInForce">订单时效性（可选，缺省由配置 DefaultTimeInForce 确定）。</param>
    /// <returns>分配的唯一订单标识 ID。</returns>
    string Sell(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "", TimeInForce? timeInForce = null)
        => SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, type, price, quantity, orderTag, timeInForce) { ClientOrderId = clientOrderId ?? string.Empty });

    /// <summary>
    /// 便捷全额平仓指定标的持仓。
    /// 若当前无持仓则返回空字符串，不产生任何订单。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="price">委托价格（市价单缺省为 0.0）。</param>
    /// <param name="type">订单类型（缺省为市价单）。</param>
    /// <param name="clientOrderId">自定义客户端订单标识（可选）。</param>
    /// <param name="orderTag">自定义订单业务标签（可选）。</param>
    /// <param name="timeInForce">订单时效性（可选，缺省由配置 DefaultTimeInForce 确定）。</param>
    /// <returns>分配的唯一订单标识 ID；若无持仓则返回 string.Empty。</returns>
    string ClosePosition(string symbol, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "", TimeInForce? timeInForce = null)
    {
        var pos = GetPosition(symbol);
        if (pos == 0m) return string.Empty;
        var dir = pos > 0m ? OrderDirection.Sell : OrderDirection.Buy;
        return SubmitOrder(new OrderRequest(symbol, dir, type, price, Math.Abs(pos), orderTag, timeInForce) { ClientOrderId = clientOrderId ?? string.Empty });
    }

    /// <summary>
    /// 撤销指定的订单。
    /// </summary>
    /// <param name="orderId">要撤销的订单 ID。</param>
    void CancelOrder(string orderId);

    /// <summary>
    /// 获取指定订单的当前状态。
    /// </summary>
    /// <param name="orderId">订单 ID。</param>
    /// <returns>订单当前的 <see cref="OrderStatus"/>。</returns>
    OrderStatus GetOrderStatus(string orderId);

    /// <summary>
    /// 当订单的生命周期状态发生变化时触发（例如：提交成功、被拒绝、撤销成功）。
    /// </summary>
    event Action<string, OrderStatus> OnOrderStatusChanged;

    /// <summary>
    /// 当订单发生实际成交（部分成交或全部成交）时触发。
    /// </summary>
    event Action<ExecutionReport> OnTrade;

    /// <summary>
    /// 当账户持仓数量发生变化时触发。
    /// </summary>
    event Action<PositionEvent>? OnPositionChanged;
}

/// <summary>
/// 经纪商引擎驱动与基础设施特权契约。
/// 仅面向回测引擎驱动层与实盘柜台对账层，承担时钟步进推进与状态权威同步职责。
/// 严格物理隔离于策略层之外，防止策略越权干扰日切结算与资产状态。
/// </summary>
public interface IBrokerEngineDriver : IBroker
{
    /// <summary>
    /// 推进经纪商物理时钟时间步，驱动日内精确时间戳对齐与多时区自动跨日清算（如 T+N 解锁冻结持仓、按日盯市等）。
    /// </summary>
    /// <param name="currentTime">当前全局物理时间戳。</param>
    void Step(MarketTime currentTime);

    /// <summary>
    /// 实盘断线重连或定时对账时，由交易所权威数据原子同步本地账户状态（回测模式无需调用）。
    /// </summary>
    /// <param name="cash">权威可用资金。</param>
    /// <param name="positions">权威持仓快照。</param>
    void SyncState(double cash, IReadOnlyDictionary<string, decimal> positions);
}
