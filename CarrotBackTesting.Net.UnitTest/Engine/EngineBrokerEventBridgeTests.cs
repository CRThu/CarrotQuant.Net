using System;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine
{
    [TestClass]
    public class EngineBrokerEventBridgeTests
    {
        private class DummyBroker : IBroker
        {
            public double GetCash() => 100000.0;
            public double GetTotalEquity() => GetCash();
            public decimal GetPosition(string symbol) => 0m;
            public decimal GetAvailablePosition(string symbol) => GetPosition(symbol);
            public double GetPositionCost(string symbol) => 0.0;
            public IReadOnlyDictionary<string, decimal> GetPositions() => new Dictionary<string, decimal>();
            public long SubmitOrder(OrderRequest request) => 1L;
            public void CancelOrder(long orderId) { }
            public OrderStatus GetOrderStatus(long orderId) => OrderStatus.Filled;

            public event Action<long, OrderStatus>? OnOrderStatusChanged;
            public event Action<ExecutionReport>? OnTrade;
            public event Action<PositionEvent>? OnPositionChanged;
            public event Action<OrderEvent>? OnOrderEvent;

            public void SimulateOrderStatus(long orderId, OrderStatus status)
            {
                OnOrderStatusChanged?.Invoke(orderId, status);
            }

            public void SimulateTrade(ExecutionReport report)
            {
                OnTrade?.Invoke(report);
            }

            public void SimulatePosition(PositionEvent e)
            {
                OnPositionChanged?.Invoke(e);
            }

            public void SimulateOrderEvent(OrderEvent e)
            {
                OnOrderEvent?.Invoke(e);
            }
        }

        private class DummyStrategy : IStrategy
        {
            public string Name => "DummyStrategy";
            public long? LastOrderId { get; private set; }
            public OrderStatus? LastStatus { get; private set; }
            public ExecutionReport? LastTrade { get; private set; }

            public void OnUpdate(IEngineContext context) { }

            public void OnOrderStatusChanged(IEngineContext context, long orderId, OrderStatus status)
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

            long? receivedOrderId = null;
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
            broker.SimulateOrderStatus(100L, OrderStatus.Submitted);
            Assert.AreEqual(100L, receivedOrderId);
            Assert.AreEqual(OrderStatus.Submitted, receivedStatus);

            // 触发成交回报
            var report = new ExecutionReport(100L, "sh.600000", 10.5, 1000m, 5.0, DateTime.UtcNow);
            broker.SimulateTrade(report);
            Assert.IsNotNull(receivedReport);
            Assert.AreEqual(10.5, receivedReport.FillPrice);
            Assert.AreEqual(1000m, receivedReport.FillQuantity);

            PositionEvent? receivedPosition = null;
            broker.OnPositionChanged += p => receivedPosition = p;
            var posEvent = new PositionEvent("sh.600000", 1000m, 10.5, DateTime.UtcNow);
            broker.SimulatePosition(posEvent);
            Assert.IsNotNull(receivedPosition);
            Assert.AreEqual(1000m, receivedPosition.NewQuantity);

            // 测试策略回调调用
            strategy.OnOrderStatusChanged(null!, 200L, OrderStatus.Filled);
            Assert.AreEqual(200L, strategy.LastOrderId);
            Assert.AreEqual(OrderStatus.Filled, strategy.LastStatus);

            strategy.OnTrade(null!, report);
            Assert.AreEqual(report, strategy.LastTrade);
        }

        [TestMethod]
        public void TradeExecutionDetails_Properties_ShouldBeCorrect()
        {
            var report = new ExecutionReport(1L, "sh.600000", 10.5, 500m, 5.0, DateTime.UtcNow);
            var details = new TradeExecutionDetails(report, OrderDirection.Buy, 1.0, "TestTag");
            Assert.AreEqual(500m, details.Report.FillQuantity);
            Assert.AreEqual(10.5, details.Report.FillPrice);
            Assert.AreEqual(OrderDirection.Buy, details.Direction);
            Assert.AreEqual("TestTag", details.OrderTag);
        }

        [TestMethod]
        public void Broker_EncapsulatesGateway_OnlyAccessibleViaIExchangeGatewayProvider()
        {
            var sim = new CarrotBacktesting.NET.Engine.Exchange.SimulatedExchangeGateway(new CarrotBacktesting.NET.Config.BrokerConfig());
            var broker = new CarrotBacktesting.NET.Engine.Broker.Broker(sim, new CarrotBacktesting.NET.Config.BrokerConfig());

            // 验证 Broker.Gateway 为显式接口实现，策略面向的 IBroker 契约无法直接访问底层网关
            Assert.IsTrue(broker is IExchangeGatewayProvider);
            var provider = (IExchangeGatewayProvider)broker;
            Assert.AreSame(sim, provider.Gateway);
        }
    }
}

