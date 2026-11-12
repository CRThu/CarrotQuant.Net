using System;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine
{
    [TestClass]
    public class EngineContractsTests
    {
        private class DummyBroker : IBroker
        {
            public double GetCash() => 100000.0;
            public decimal GetPosition(string symbol) => 0m;
            public string SubmitOrder(OrderRequest request) => "ord-1";
            public void CancelOrder(string orderId) { }
            public OrderStatus GetOrderStatus(string orderId) => OrderStatus.Filled;

            public event Action<string, OrderStatus>? OnOrderStatusChanged;
            public event Action<ExecutionReport>? OnTrade;

            public void SimulateOrderStatus(string orderId, OrderStatus status)
            {
                OnOrderStatusChanged?.Invoke(orderId, status);
            }

            public void SimulateTrade(ExecutionReport report)
            {
                OnTrade?.Invoke(report);
            }
        }

        private class DummyStrategy : IStrategy
        {
            public string Name => "DummyStrategy";
            public string? LastOrderId { get; private set; }
            public OrderStatus? LastStatus { get; private set; }
            public ExecutionReport? LastTrade { get; private set; }

            public void OnUpdate(IEngineContext context) { }

            public void OnOrderStatusChanged(IEngineContext context, string orderId, OrderStatus status)
            {
                LastOrderId = orderId;
                LastStatus = status;
            }

            public void OnTrade(IEngineContext context, ExecutionReport e)
            {
                LastTrade = e;
            }
        }

        [TestMethod]
        public void BrokerAndStrategy_EventsAndCallbacks_ShouldMatchSignatures()
        {
            var broker = new DummyBroker();
            var strategy = new DummyStrategy();

            string? receivedOrderId = null;
            OrderStatus? receivedStatus = null;
            ExecutionReport? receivedReport = null;

            broker.OnOrderStatusChanged += (id, st) =>
            {
                receivedOrderId = id;
                receivedStatus = st;
            };

            broker.OnTrade += r =>
            {
                receivedReport = r;
            };

            // 触发状态改变
            broker.SimulateOrderStatus("ord-100", OrderStatus.Submitted);
            Assert.AreEqual("ord-100", receivedOrderId);
            Assert.AreEqual(OrderStatus.Submitted, receivedStatus);

            // 触发成交回报
            var report = new ExecutionReport("ord-100", "sh.600000", 10.5, 1000m, 5.0, DateTime.UtcNow);
            broker.SimulateTrade(report);
            Assert.IsNotNull(receivedReport);
            Assert.AreEqual(10.5, receivedReport.FillPrice);
            Assert.AreEqual(1000m, receivedReport.FillQuantity);

            // 测试策略回调调用
            strategy.OnOrderStatusChanged(null!, "ord-200", OrderStatus.Filled);
            Assert.AreEqual("ord-200", strategy.LastOrderId);
            Assert.AreEqual(OrderStatus.Filled, strategy.LastStatus);

            strategy.OnTrade(null!, report);
            Assert.AreEqual(report, strategy.LastTrade);
        }

        [TestMethod]
        public void OrderContext_QuantityProperty_ShouldBeDecimal()
        {
            var ctx = new OrderContext("ord-1", "sh.600000", OrderDirection.Buy, 10.5, 500m, OrderStatus.Filled, DateTime.UtcNow);
            Assert.AreEqual(500m, ctx.Quantity);
            Assert.AreEqual(10.5, ctx.Price);
        }
    }
}
