namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义通用交易所网关接口。
/// 该接口位于路由层，负责屏蔽回测环境模拟器与实盘环境交易所柜台的物理差异。
/// 保持职责单一与接口纯粹，严禁包含任何回测专有的时钟步进或行情注入方法。
/// </summary>
public interface IExchangeGateway
{
    /// <summary>
    /// 向交易所或柜台下发新订单。
    /// </summary>
    /// <param name="orderId">由系统生成的唯一订单 ID。</param>
    /// <param name="request">订单详细请求参数。</param>
    void SendOrder(long orderId, OrderRequest request);

    /// <summary>
    /// 向交易所或柜台发送撤单请求。
    /// </summary>
    /// <param name="orderId">要撤销的订单 ID。</param>
    void CancelOrder(long orderId);
    
    /// <summary>
    /// 当订单发生实际成交（部分成交或全部成交）时触发。
    /// 包含成交价格、数量、手续费等物理交割信息。
    /// </summary>
    event Action<ExecutionReport> OnExecution;

    /// <summary>
    /// 当订单的生命周期状态发生变化时触发（如：已提交排队、被拒绝、已撤销）。
    /// 包含权威的状态变更报告，携带时间戳、原因、剩余排队量等完整凭证。
    /// </summary>
    event Action<OrderStateReport> OnOrderStateChanged;
}
