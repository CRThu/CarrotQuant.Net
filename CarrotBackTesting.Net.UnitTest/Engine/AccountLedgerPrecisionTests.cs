using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine.Broker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

/// <summary>
/// 账本资产精度与浮点尾差吸收回归测试套件。
/// 验证多次分笔成交下按比例解冻保证金时的浮点尾差清理，以及平尽持仓边界下的解冻守恒。
/// </summary>
[TestClass]
public class AccountLedgerPrecisionTests
{
    private sealed class DirectCallbackGateway : IExchangeGateway
    {
        public event Action<ExecutionReport>? OnExecution;
        public event Action<OrderStateReport>? OnOrderStateChanged;

        public void SendOrder(string orderId, OrderRequest request) { }
        public void CancelOrder(string orderId) { }

        public void PushExecution(ExecutionReport report) => OnExecution?.Invoke(report);
        public void PushState(string orderId, OrderStatus status, string? reason = null) =>
            OnOrderStateChanged?.Invoke(new OrderStateReport(orderId, status, DateTime.UtcNow, Reason: reason ?? string.Empty));
    }

    [TestMethod]
    public void Precision_MultipleFills_RemainingFrozenCash_AbsorbedCleanly()
    {
        // 场景：买入 300 股，单价 10.0 元，分 3 笔 100 股成交。
        // 100 / 300 产生循环小数，验证第 3 笔全额成交后剩余冻结现金严格归零，无浮点尾差残留。
        var gateway = new DirectCallbackGateway();
        var config = new BrokerConfig
        {
            InitialCash = 100000.0,
            FeeRate = 0.0,
            MinFee = 0.0,
            Slippage = 0.0
        };
        var broker = new Broker(gateway, config);
        var now = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
        broker.Step(now);

        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 300m);
        string orderId = broker.SubmitOrder(req);

        // 初始预冻结: 300 * 10.0 = 3000.0
        Assert.AreEqual(3000.0, broker.FrozenCash, 1e-6);
        Assert.AreEqual(97000.0, broker.AvailableCash, 1e-6);

        // 第 1 笔成交 100 股
        gateway.PushExecution(new ExecutionReport(
            orderId, "sh.600000", 10.0, 100m, 0.0, now,
            CumQuantity: 100m, LeavesQuantity: 200m, Role: LiquidityRole.Maker));
        Assert.AreEqual(OrderStatus.PartialFilled, broker.GetOrderStatus(orderId));
        Assert.IsTrue(broker.FrozenCash > 0.0);

        // 第 2 笔成交 100 股
        gateway.PushExecution(new ExecutionReport(
            orderId, "sh.600000", 10.0, 100m, 0.0, now,
            CumQuantity: 200m, LeavesQuantity: 100m, Role: LiquidityRole.Maker));
        Assert.AreEqual(OrderStatus.PartialFilled, broker.GetOrderStatus(orderId));
        Assert.IsTrue(broker.FrozenCash > 0.0);

        // 第 3 笔成交 100 股 (全额成交，LeavesQty = 0)
        gateway.PushExecution(new ExecutionReport(
            orderId, "sh.600000", 10.0, 100m, 0.0, now,
            CumQuantity: 300m, LeavesQuantity: 0m, Role: LiquidityRole.Maker));

        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(orderId));
        // 断言全额成交后预冻结资金严格为 0.0，浮点残差完全被吸收释放
        Assert.AreEqual(0.0, broker.FrozenCash, "全额成交后账户 FrozenCash 必须完全清零");
        Assert.AreEqual(300m, broker.GetPosition("sh.600000"));
        Assert.AreEqual(97000.0, broker.AvailableCash, 1e-6);
    }

    [TestMethod]
    public void Precision_UnfreezeWhenPositionClosed_ShouldNotProduceNegativeAvailableQuantity()
    {
        // 场景：现货买入 100 股后全额卖出平仓，持仓量归零。
        // 验证平尽状态下若触发多余解冻，可用持仓保持归零，严禁反向冲减出负持仓。
        var gateway = new DirectCallbackGateway();
        var config = new BrokerConfig
        {
            InitialCash = 100000.0,
            FeeRate = 0.0,
            MinFee = 0.0,
            Slippage = 0.0
        };
        var broker = new Broker(gateway, config);
        var now = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
        broker.Step(now);

        // 1. 买入 100 股建立持仓
        string buyId = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 100m));
        gateway.PushExecution(new ExecutionReport(
            buyId, "sh.600000", 10.0, 100m, 0.0, now,
            CumQuantity: 100m, LeavesQuantity: 0m, Role: LiquidityRole.Taker));

        Assert.AreEqual(100m, broker.GetPosition("sh.600000"));
        Assert.AreEqual(100m, broker.GetAvailablePosition("sh.600000"));

        // 2. 卖出平仓 100 股
        string sellId = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 12.0, 100m));
        Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "挂单卖出后可用持仓应被冻结扣减");

        gateway.PushExecution(new ExecutionReport(
            sellId, "sh.600000", 12.0, 100m, 0.0, now,
            CumQuantity: 100m, LeavesQuantity: 0m, Role: LiquidityRole.Maker));

        // 3. 验证平仓后持仓与可用数量
        Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
        Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"), "全额平仓后可用持仓量必须恒等于 0，绝不可为负数");
    }
}
