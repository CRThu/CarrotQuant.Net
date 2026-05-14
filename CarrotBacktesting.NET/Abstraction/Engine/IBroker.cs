namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 经纪商接口：面向策略层暴露。负责资金风控、订单路由及状态维护。
/// </summary>
public interface IBroker
{
    // --- 1. 资产与仓位查询 (Read) ---
    double GetCash();
    decimal GetPosition(string symbol);

    // --- 2. 订单管理 (Command) ---
    // 策略提交订单给 Broker，Broker 会返回一个内部生成的 OrderId
    string SubmitOrder(OrderRequest request);
    void CancelOrder(string orderId);
    OrderStatus GetOrderStatus(string orderId);

    // --- 3. 异步事件回调 (Events) ---
    // 策略可以订阅这些事件，以便在订单成交或状态改变时执行特定逻辑
    event Action<ExecutionReport> OnOrderStatusChanged;
    event Action<string, OrderStatus> OnTrade;
}
