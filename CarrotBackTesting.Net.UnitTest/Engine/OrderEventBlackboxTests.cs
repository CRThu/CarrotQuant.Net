using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine.Broker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

/// <summary>
/// 订单黑匣子事件与全流程审计回归测试套件。
/// 验证订单生命周期各个阶段动作事件（OrderEvent）的派发精度与原因溯源凭证。
/// </summary>
[TestClass]
public class OrderEventBlackboxTests
{
    private sealed class MockRejectingGateway : IExchangeGateway
    {
        public event Action<ExecutionReport>? OnExecution;
        public event Action<OrderStateReport>? OnOrderStateChanged;

        public void SendOrder(string orderId, OrderRequest request)
        {
            if (request.Price >= 20.0)
            {
                // 模拟网关涨跌停封板拦截
                OnOrderStateChanged?.Invoke(new OrderStateReport(
                    orderId, OrderStatus.Rejected, DateTime.UtcNow,
                    request.Symbol, "Price exceeds upper price limit: 20.0 >= 11.0", request.ClientOrderId, request.Quantity));
            }
            else
            {
                OnOrderStateChanged?.Invoke(new OrderStateReport(
                    orderId, OrderStatus.Submitted, DateTime.UtcNow,
                    request.Symbol, ClientOrderId: request.ClientOrderId, LeavesQuantity: request.Quantity));
            }
        }

        public void CancelOrder(string orderId)
        {
            OnOrderStateChanged?.Invoke(new OrderStateReport(orderId, OrderStatus.Canceled, DateTime.UtcNow, Reason: "Canceled by client"));
        }

        public void PushExecution(ExecutionReport report) => OnExecution?.Invoke(report);
        public void PushStateReport(OrderStateReport report) => OnOrderStateChanged?.Invoke(report);
    }

    [TestMethod]
    public void OrderEvent_PreFreezeRejected_ShouldEmitRejectedEventWithReason()
    {
        // 场景：可用资金 1,000 元，尝试委托买入 100,000 元，预冻结失败被拒
        var gateway = new MockRejectingGateway();
        var config = new BrokerConfig { InitialCash = 1000.0 };
        var broker = new Broker(gateway, config);
        broker.Step(new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc));

        var events = new List<OrderEvent>();
        broker.OnOrderEvent += ev => events.Add(ev);

        string orderId = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 10000m));

        Assert.AreEqual(1, events.Count);
        var ev = events[0];
        Assert.AreEqual(orderId, ev.OrderId);
        Assert.AreEqual(OrderEventType.Rejected, ev.EventType);
        Assert.AreEqual(OrderStatus.Rejected, ev.Status);
        Assert.IsTrue(ev.Reason.Contains("Insufficient"), $"拒单原因应明确包含资金不足说明，实际为: '{ev.Reason}'");
        Assert.AreEqual(10000m, ev.LeavesQuantity);
        Assert.AreEqual(0m, ev.FillQuantity);
        Assert.AreEqual(0m, ev.CumQuantity);
    }

    [TestMethod]
    public void OrderEvent_GatewayLimitRejected_ShouldPropagateGatewayReasonAccurately()
    {
        // 场景：委托价格超过涨跌停限制，被网关拒单，断言 OrderEvent 准确捕获网关原因
        var gateway = new MockRejectingGateway();
        var config = new BrokerConfig { InitialCash = 100000.0 };
        var broker = new Broker(gateway, config);
        broker.Step(new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc));

        var events = new List<OrderEvent>();
        broker.OnOrderEvent += ev => events.Add(ev);

        string orderId = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 25.0, 100m));

        // 预期收到 1 次 Submitted，1 次 Rejected
        Assert.AreEqual(2, events.Count);
        Assert.AreEqual(OrderEventType.Submitted, events[0].EventType);
        Assert.AreEqual(OrderEventType.Rejected, events[1].EventType);
        Assert.AreEqual(OrderStatus.Rejected, events[1].Status);
        Assert.IsTrue(events[1].Reason.Contains("Price exceeds upper price limit"), $"必须准确透传网关拒绝原因，实际为: '{events[1].Reason}'");
    }

    [TestMethod]
    public void OrderEvent_SubmittedFillCanceled_ShouldEmitCompleteAuditTrail()
    {
        // 场景：订单经历 Submitted -> 部分成交 Fill -> 撤销剩余 Canceled
        var gateway = new MockRejectingGateway();
        var config = new BrokerConfig { InitialCash = 100000.0 };
        var broker = new Broker(gateway, config);
        var time0 = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
        broker.Step(time0);

        var events = new List<OrderEvent>();
        broker.OnOrderEvent += ev => events.Add(ev);

        string orderId = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 500m, "TestTag"));

        // 1. Submitted
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(OrderEventType.Submitted, events[0].EventType);
        Assert.AreEqual(500m, events[0].LeavesQuantity);
        Assert.AreEqual("TestTag", events[0].OrderTag);

        // 2. 部分成交 200 股
        var time1 = time0.AddMinutes(1);
        gateway.PushExecution(new ExecutionReport(
            orderId, "sh.600000", 10.0, 200m, 5.0, time1,
            CumQuantity: 200m, LeavesQuantity: 300m, Role: LiquidityRole.Maker));

        Assert.AreEqual(2, events.Count);
        var fillEv = events[1];
        Assert.AreEqual(OrderEventType.Fill, fillEv.EventType);
        Assert.AreEqual(OrderStatus.PartialFilled, fillEv.Status);
        Assert.AreEqual(200m, fillEv.FillQuantity);
        Assert.AreEqual(200m, fillEv.CumQuantity);
        Assert.AreEqual(300m, fillEv.LeavesQuantity);
        Assert.AreEqual(10.0, fillEv.FillPrice);
        Assert.AreEqual(LiquidityRole.Maker, fillEv.Role);

        // 3. 撤单剩余 300 股
        broker.CancelOrder(orderId);

        Assert.AreEqual(3, events.Count);
        var cancelEv = events[2];
        Assert.AreEqual(OrderEventType.Canceled, cancelEv.EventType);
        Assert.AreEqual(OrderStatus.Canceled, cancelEv.Status);
        Assert.AreEqual(0m, cancelEv.FillQuantity);
        Assert.AreEqual(200m, cancelEv.CumQuantity);
        Assert.AreEqual(300m, cancelEv.LeavesQuantity);
        Assert.AreEqual("Canceled by client", cancelEv.Reason);
    }

    [TestMethod]
    public void OrderEvent_MultiplePartialFills_ShouldDistinguishFillQuantityAndCumQuantity()
    {
        // 场景：多次分笔成交（100 -> 150 -> 250 全额成交），严格验证当笔成交量与累计成交量的准确独立性
        var gateway = new MockRejectingGateway();
        var broker = new Broker(gateway, new BrokerConfig { InitialCash = 100000.0 });
        var time0 = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
        broker.Step(time0);

        var events = new List<OrderEvent>();
        broker.OnOrderEvent += ev => events.Add(ev);

        string orderId = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 500m));

        // 第 1 笔：成交 100
        gateway.PushExecution(new ExecutionReport(orderId, "sh.600000", 10.0, 100m, 1.0, time0.AddMinutes(1),
            CumQuantity: 100m, LeavesQuantity: 400m, Role: LiquidityRole.Maker));
        // 第 2 笔：成交 150
        gateway.PushExecution(new ExecutionReport(orderId, "sh.600000", 10.1, 150m, 1.5, time0.AddMinutes(2),
            CumQuantity: 250m, LeavesQuantity: 250m, Role: LiquidityRole.Maker));
        // 第 3 笔：成交 250（全部成交）
        gateway.PushExecution(new ExecutionReport(orderId, "sh.600000", 10.2, 250m, 2.5, time0.AddMinutes(3),
            CumQuantity: 500m, LeavesQuantity: 0m, Role: LiquidityRole.Maker));

        Assert.AreEqual(4, events.Count); // Submitted + 3 Fill

        var fill1 = events[1];
        Assert.AreEqual(100m, fill1.FillQuantity);
        Assert.AreEqual(100m, fill1.CumQuantity);
        Assert.AreEqual(400m, fill1.LeavesQuantity);
        Assert.AreEqual(10.0, fill1.FillPrice);

        var fill2 = events[2];
        Assert.AreEqual(150m, fill2.FillQuantity);
        Assert.AreEqual(250m, fill2.CumQuantity);
        Assert.AreEqual(250m, fill2.LeavesQuantity);
        Assert.AreEqual(10.1, fill2.FillPrice);

        var fill3 = events[3];
        Assert.AreEqual(250m, fill3.FillQuantity);
        Assert.AreEqual(500m, fill3.CumQuantity);
        Assert.AreEqual(0m, fill3.LeavesQuantity);
        Assert.AreEqual(10.2, fill3.FillPrice);
        Assert.AreEqual(OrderStatus.Filled, fill3.Status);
    }

    [TestMethod]
    public void Broker_ConservationViolation_ShouldThrowInvalidOperationException()
    {
        // 验证分笔成交守恒不变量被破坏时，抛出 InvalidOperationException 强类型异常
        var gateway = new MockRejectingGateway();
        var broker = new Broker(gateway, new BrokerConfig { InitialCash = 100000.0 });
        broker.Step(new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc));

        string orderId = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 500m));

        // 故意传入破坏守恒的报告：CumQty(200) + LeavesQty(400) = 600 != 500
        var invalidReport = new ExecutionReport(
            orderId, "sh.600000", 10.0, 200m, 5.0, DateTime.UtcNow,
            CumQuantity: 200m, LeavesQuantity: 400m, Role: LiquidityRole.Taker);

        Assert.ThrowsException<InvalidOperationException>(() => gateway.PushExecution(invalidReport));
    }

    [TestMethod]
    public void OrderStateReport_FullFields_PropagatesAccuratelyToOrderEvent()
    {
        // 场景：网关推送包含物理发生时间戳与剩余量的 OrderStateReport，验证 Broker 精准透传至 OrderEvent
        var gateway = new MockRejectingGateway();
        var broker = new Broker(gateway, new BrokerConfig { InitialCash = 100000.0 });
        var stepTime = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
        broker.Step(stepTime);

        var events = new List<OrderEvent>();
        broker.OnOrderEvent += ev => events.Add(ev);

        string orderId = broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 500m, "CustomTag"));

        // 模拟网关在 09:30:15 产生拒单回报（携带精确物理时间戳与剩余挂单量）
        var physicalTime = new DateTime(2025, 1, 15, 9, 30, 15, DateTimeKind.Utc);
        var stateReport = new OrderStateReport(
            orderId,
            OrderStatus.Rejected,
            physicalTime,
            Symbol: "sh.600000",
            Reason: "Exceeded daily price fluctuation band",
            ClientOrderId: "client-ord-123",
            LeavesQuantity: 500m,
            ErrorCode: 1001);

        gateway.PushStateReport(stateReport);

        Assert.AreEqual(2, events.Count); // Submitted + Rejected
        var rejectEv = events[1];
        Assert.AreEqual(OrderEventType.Rejected, rejectEv.EventType);
        Assert.AreEqual(OrderStatus.Rejected, rejectEv.Status);
        MarketTime expectedTime = physicalTime;
        Assert.AreEqual(expectedTime, rejectEv.Time, "OrderEvent 时间戳必须忠实对齐 OrderStateReport 的权威物理时间");
        Assert.AreEqual(500m, rejectEv.LeavesQuantity);
        Assert.AreEqual(0m, rejectEv.FillQuantity);
        Assert.AreEqual("Exceeded daily price fluctuation band", rejectEv.Reason);
        Assert.AreEqual("CustomTag", rejectEv.OrderTag);
    }
}
