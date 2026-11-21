using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;

namespace CarrotBackTesting.Net.UnitTest.Engine;

/// <summary>
/// 订单时效性（TimeInForce）契约、日切撤单与归一化流转测试。
/// </summary>
[TestClass]
public class TimeInForceTests
{
    private static ITimeseriesRegistry CreateMockTimeseries(string[] symbols, MarketTime[] dates, double[,] closePrices)
    {
        var metadata = new DynamicMarketMetadata(symbols, dates);
        int rows = dates.Length;
        int cols = symbols.Length;

        var closeMat = new ChunkMatrix2D<double>(width: cols, pageSize: 4096, initialRowCount: rows);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                closeMat[r, c] = closePrices[r, c];

        var matrices = new Dictionary<string, IReadOnlyMatrix2D<double>>(StringComparer.OrdinalIgnoreCase)
        {
            [StandardFields.Close] = closeMat
        };

        return new SimulatedExchangeGatewayTests.MultiMatrixTimeseriesRegistry(metadata, matrices);
    }

    [TestMethod]
    public void OrderBook_CancelDayOrders_ShouldRemoveOnlyDayOrdersAndCleanEmptyBuckets()
    {
        var orderBook = new OrderBook();
        var submitTime = (MarketTime)new DateTime(2025, 1, 2);

        var dayOrder1 = new SimulatedOrder(101L, new OrderRequest("sh.600000", OrderDirection.Buy, 10.0, 100m, timeInForce: TimeInForce.Day), submitTime);
        var dayOrder2 = new SimulatedOrder(102L, new OrderRequest("sh.600001", OrderDirection.Buy, 20.0, 200m, timeInForce: TimeInForce.Day), submitTime);
        var gtcOrder = new SimulatedOrder(103L, new OrderRequest("sh.600000", OrderDirection.Buy, 9.5, 100m, timeInForce: TimeInForce.GoodTilCanceled), submitTime);

        orderBook.Add(dayOrder1);
        orderBook.Add(dayOrder2);
        orderBook.Add(gtcOrder);

        Assert.AreEqual(3, orderBook.Count);

        var canceled = orderBook.CancelDayOrders();

        Assert.AreEqual(2, canceled.Count);
        Assert.AreEqual(1, orderBook.Count);

        Assert.IsFalse(orderBook.TryGet(101L, out _));
        Assert.IsFalse(orderBook.TryGet(102L, out _));
        Assert.IsTrue(orderBook.TryGet(103L, out var remaining));
        Assert.IsNotNull(remaining);
        Assert.AreEqual(TimeInForce.GoodTilCanceled, remaining.TimeInForce);

        // 验证标的桶：sh.600001 桶已清空注销，sh.600000 仅剩 gtcOrder
        var pending600000 = orderBook.GetPendingOrders("sh.600000");
        Assert.IsNotNull(pending600000);
        Assert.AreEqual(1, pending600000.Count);
        Assert.AreEqual(103L, pending600000[0].OrderId);

        var pending600001 = orderBook.GetPendingOrders("sh.600001");
        Assert.IsNull(pending600001);
    }

    [TestMethod]
    public void Broker_SubmitOrder_WhenTimeInForceNull_ShouldNormalizeToDefaultTimeInForce()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            DefaultTimeInForce = TimeInForce.Day
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);

        var date = new DateTime(2025, 1, 2);
        gateway.OnMarketData("sh.600000", date, new BarSnapshot(close: 15.0));
        gateway.OnMarketData("sh.600001", date, new BarSnapshot(close: 15.0));
        gateway.OnMarketData("sh.600002", date, new BarSnapshot(close: 15.0));

        // 1. 默认配置为 Day，请求未指定时效
        var req1 = new OrderRequest("sh.600000", OrderDirection.Buy, 10.0, 100m);
        broker.SubmitOrder(req1);

        var activeOrders = broker.SaveState().ActiveOrders;
        Assert.AreEqual(1, activeOrders.Count);
        Assert.AreEqual(TimeInForce.Day, activeOrders[0].Request.TimeInForce);

        // 2. 修改配置为 GTC，验证未指定时效的新订单自动归一化为 GTC
        config.DefaultTimeInForce = TimeInForce.GoodTilCanceled;
        var req2 = new OrderRequest("sh.600001", OrderDirection.Buy, 10.0, 100m);
        broker.SubmitOrder(req2);

        activeOrders = broker.SaveState().ActiveOrders;
        var ord2 = FindOrder(activeOrders, "sh.600001");
        Assert.IsNotNull(ord2);
        Assert.AreEqual(TimeInForce.GoodTilCanceled, ord2.Request.TimeInForce);

        // 3. 显式指定时效时，优先级高于全局默认配置
        var req3 = new OrderRequest("sh.600002", OrderDirection.Buy, 10.0, 100m, timeInForce: TimeInForce.Day);
        broker.SubmitOrder(req3);

        activeOrders = broker.SaveState().ActiveOrders;
        var ord3 = FindOrder(activeOrders, "sh.600002");
        Assert.IsNotNull(ord3);
        Assert.AreEqual(TimeInForce.Day, ord3.Request.TimeInForce);
    }

    [TestMethod]
    public void Broker_Facade_BuySell_ShouldPassTimeInForceCorrectly()
    {
        var config = new BrokerConfig { InitialCash = 100_000.0 };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);

        gateway.OnMarketData("sh.600000", new DateTime(2025, 1, 2), new BarSnapshot(close: 15.0));

        long buyId = broker.Buy("sh.600000", 100m, price: 9.0, type: OrderType.Limit, timeInForce: TimeInForce.GoodTilCanceled);
        var activeOrders = broker.SaveState().ActiveOrders;
        var buyOrder = FindOrderById(activeOrders, buyId);

        Assert.IsNotNull(buyOrder);
        Assert.AreEqual(TimeInForce.GoodTilCanceled, buyOrder.Request.TimeInForce);
    }

    [TestMethod]
    public void Step_MixedOrders_ShouldCancelDayAndPreserveGtcThenFillOnNextDay()
    {
        var symbols = new[] { "sh.600000" };
        var dates = new[]
        {
            (MarketTime)new DateTime(2025, 1, 2),
            (MarketTime)new DateTime(2025, 1, 3),
            (MarketTime)new DateTime(2025, 1, 6)
        };
        // Day 1 收盘 10.0，Day 2 收盘 10.5，Day 3 收盘 8.5
        var closePrices = new double[,] { { 10.0 }, { 10.5 }, { 8.5 } };
        var timeseries = CreateMockTimeseries(symbols, dates, closePrices);

        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            Slippage = 0.0,
            FeeRate = 0.0
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);

        // Day 1
        gateway.Step(dates[0], timeseries, 0);
        ((IBrokerEngineDriver)broker).Step(dates[0]);

        // 同时挂出两笔限价 9.0 买单：一笔 Day，一笔 GTC
        long dayOrderId = broker.Buy("sh.600000", 1000m, price: 9.0, type: OrderType.Limit, timeInForce: TimeInForce.Day);
        long gtcOrderId = broker.Buy("sh.600000", 1000m, price: 9.0, type: OrderType.Limit, timeInForce: TimeInForce.GoodTilCanceled);

        Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(dayOrderId));
        Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(gtcOrderId));
        Assert.AreEqual(18000.0, broker.FrozenCash, 1e-4);

        // Day 2 (跨日推进，当日价格 10.5 未触碰限价 9.0)
        gateway.Step(dates[1], timeseries, 1);
        ((IBrokerEngineDriver)broker).Step(dates[1]);

        // 验证：Day 挂单被撤销解冻，GTC 挂单继续保留
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(dayOrderId));
        Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(gtcOrderId));
        Assert.AreEqual(9000.0, broker.FrozenCash, 1e-4);

        // Day 3 (价格跌至 8.5，触碰 GTC 挂单的 9.0 限价)
        gateway.Step(dates[2], timeseries, 2);
        ((IBrokerEngineDriver)broker).Step(dates[2]);

        // 验证：GTC 挂单成功撮合成交，冻结资金释放为持仓
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(gtcOrderId));
        Assert.AreEqual(0.0, broker.FrozenCash);
        Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void Broker_SubmitOrder_ShouldPropagateNormalizedTimeInForceToGatewaySnapshot()
    {
        var config = new BrokerConfig { DefaultTimeInForce = TimeInForce.GoodTilCanceled };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        gateway.OnMarketData("sh.600000", new DateTime(2025, 1, 2), new BarSnapshot(close: 10.0));

        // 通过 Broker 提交未显式指定时效的订单
        broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, 9.5, 100m));

        // 验证网关接收到的订单已由 Broker 权威单点归一化，且快照存取无损
        var snapshot = gateway.SaveState();
        Assert.AreEqual(1, snapshot.PendingOrders.Count);
        Assert.AreEqual(TimeInForce.GoodTilCanceled, snapshot.PendingOrders[0].Request.TimeInForce);
    }

    private static BrokerOrderSnapshot? FindOrder(IReadOnlyList<BrokerOrderSnapshot> list, string symbol)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Request.Symbol == symbol) return list[i];
        }
        return null;
    }

    private static BrokerOrderSnapshot? FindOrderById(IReadOnlyList<BrokerOrderSnapshot> list, long orderId)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].OrderId == orderId) return list[i];
        }
        return null;
    }
}
