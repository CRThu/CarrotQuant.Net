namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 通用的交易所网关接口（路由层）。
/// 面向 Broker 暴露，屏蔽了回测模拟器和真实交易所网络 API 的差异。
/// </summary>
public interface IExchangeGateway
{
    // --- 1. 市场数据查询 ---
    /// <summary>
    /// 获取指定资产当前的订单簿（深度快照）。
    /// </summary>
    Orderbook GetOrderbook(string symbol);

    // --- 2. 订单指令下发 ---
    /// <summary>
    /// 向交易所发送新订单。
    /// </summary>
    void SendOrder(string orderId, OrderRequest request);

    /// <summary>
    /// 向交易所发送撤单请求。
    /// </summary>
    void CancelOrder(string orderId);
    
    // --- 3. 异步事件回调 (推送层) ---
    /// <summary>
    /// 当订单发生实际成交（部分或全部）时触发。
    /// 包含成交价格、数量、手续费等物理交割信息。
    /// </summary>
    event Action<ExecutionReport> OnExecution;

    /// <summary>
    /// 当订单的生命周期状态发生变化时触发（如：已提交、被拒绝、已撤销）。
    /// </summary>
    event Action<string, OrderStatus> OnOrderStateChanged;
}
