namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义经纪商（Broker）接口。
/// 面向策略层暴露，负责执行风控校验、订单路由、资金管理以及维护持仓状态。
/// 支持回测模拟（SimulatedBroker）与实盘网关（LivingBroker）无缝切换。
/// </summary>
public interface IBroker
{
    /// <summary>
    /// 获取当前账户的可提可用现金金额。
    /// </summary>
    /// <returns>可用现金金额。</returns>
    double GetCash();

    /// <summary>
    /// 获取当前账户的总动态资产权益（可用现金 + 持仓浮动市值/占用保证金 + 未实现盈亏）。
    /// </summary>
    /// <returns>账户总权益。</returns>
    double GetTotalEquity() => GetCash();

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
    decimal GetAvailablePosition(string symbol) => GetPosition(symbol);

    /// <summary>
    /// 获取当前账户中所有活跃持仓的只读快照字典。
    /// </summary>
    /// <returns>代码与持仓数量的映射字典。</returns>
    IReadOnlyDictionary<string, decimal> GetPositions() => new Dictionary<string, decimal>();

    /// <summary>
    /// 提交一个新的订单请求。
    /// </summary>
    /// <param name="request">订单请求参数。</param>
    /// <returns>由经纪商系统内部生成的唯一订单 ID。</returns>
    string SubmitOrder(OrderRequest request);

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
    /// 推进经纪商物理时钟时间步，驱动日内精确时间戳对齐与多时区自动跨日清算（如 T+N 解锁冻结持仓、按日盯市等）。
    /// </summary>
    /// <param name="currentTime">当前全局物理时间戳。</param>
    void Step(DateTime currentTime) { }

    /// <summary>
    /// 实盘断线重连或定时对账时，由交易所权威数据原子同步本地账户状态（回测模式无需调用）。
    /// </summary>
    /// <param name="cash">权威可用资金。</param>
    /// <param name="positions">权威持仓快照。</param>
    void SyncState(double cash, IReadOnlyDictionary<string, decimal> positions) { }

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
