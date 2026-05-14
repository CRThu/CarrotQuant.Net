namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义经纪商（Broker）接口。
/// 面向策略层暴露，负责执行风控校验、订单路由、资金管理以及维护持仓状态。
/// </summary>
public interface IBroker
{
    /// <summary>
    /// 获取当前账户的可提可用现金金额。
    /// </summary>
    /// <returns>可用现金金额。</returns>
    double GetCash();

    /// <summary>
    /// 获取指定品种的当前持仓数量。
    /// </summary>
    /// <param name="symbol">品种代码。</param>
    /// <returns>持仓数量（正数为多头，负数为虚拟合约空头，0 为无持仓）。</returns>
    decimal GetPosition(string symbol);

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
    /// 当订单的生命周期状态发生变化时触发（例如：提交成功、被拒绝、撤销成功）。
    /// </summary>
    event Action<ExecutionReport> OnOrderStatusChanged;

    /// <summary>
    /// 当订单发生实际成交（部分成交或全部成交）时触发。
    /// </summary>
    event Action<string, OrderStatus> OnTrade;
}
