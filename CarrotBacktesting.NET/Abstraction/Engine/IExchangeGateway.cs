namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义通用交易所网关接口。
/// 该接口位于路由层，负责屏蔽回测环境模拟器与实盘环境交易所 API 的差异。
/// </summary>
public interface IExchangeGateway
{
    /// <summary>
    /// 获取指定资产当前的订单簿（深度快照）。
    /// </summary>
    /// <param name="symbol">资产代码。</param>
    /// <returns>包含买卖档位的 <see cref="Orderbook"/> 对象。</returns>
    Orderbook GetOrderbook(string symbol);

    /// <summary>
    /// 向交易所或网关下发新订单。
    /// </summary>
    /// <param name="orderId">由系统生成的唯一订单 ID。</param>
    /// <param name="request">订单详细请求参数。</param>
    void SendOrder(string orderId, OrderRequest request);

    /// <summary>
    /// 向交易所或网关发送撤单请求。
    /// </summary>
    /// <param name="orderId">要撤销的订单 ID。</param>
    void CancelOrder(string orderId);
    
    /// <summary>
    /// 当订单发生实际成交（部分成交或全部成交）时触发。
    /// 包含成交价格、数量、手续费等物理交割信息。
    /// </summary>
    event Action<ExecutionReport> OnExecution;

    /// <summary>
    /// 当订单的生命周期状态发生变化时触发（如：已提交、被拒绝、已撤销）。
    /// </summary>
    event Action<string, OrderStatus> OnOrderStateChanged;
}
