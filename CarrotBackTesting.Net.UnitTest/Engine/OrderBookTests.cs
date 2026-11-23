using System;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Engine.Exchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class OrderBookTests
{
    [TestMethod]
    public void AddAndRemove_SingleOrder_ShouldMaintainCountAndBucket()
    {
        var book = new OrderBook();
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 100m);
        var order = new SimulatedOrder(1L, req, DateTime.UtcNow);

        book.Add(order);
        Assert.AreEqual(1, book.Count);
        Assert.IsTrue(book.ActiveSymbols.Contains("sh.600000"));

        bool removed = book.Remove(1L, out var removedOrder);
        Assert.IsTrue(removed);
        Assert.IsNotNull(removedOrder);
        Assert.AreEqual(1L, removedOrder.OrderId);
        Assert.AreEqual(0, book.Count);
        Assert.IsFalse(book.ActiveSymbols.Contains("sh.600000"), "桶空时必须自动注销键，杜绝内存泄漏");
    }

    [TestMethod]
    public void FIFO_MultipleOrdersSameSymbol_ShouldPreserveSequence()
    {
        var book = new OrderBook();
        // 同价买单：测试纯粹的同价格先来后到 FIFO 时间优先
        var req1 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 100m);
        var req2 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 200m);
        var req3 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 300m);

        book.Add(new SimulatedOrder(1L, req1, DateTime.UtcNow));
        book.Add(new SimulatedOrder(2L, req2, DateTime.UtcNow.AddMilliseconds(1)));
        book.Add(new SimulatedOrder(3L, req3, DateTime.UtcNow.AddMilliseconds(2)));

        var pending = book.GetPendingOrders("sh.600000");
        Assert.IsNotNull(pending);
        Assert.AreEqual(3, pending.Count);
        Assert.AreEqual(1L, pending[0].OrderId);
        Assert.AreEqual(2L, pending[1].OrderId);
        Assert.AreEqual(3L, pending[2].OrderId);

        // 移除中间的订单
        book.Remove(2L, out _);
        pending = book.GetPendingOrders("sh.600000");
        Assert.IsNotNull(pending);
        Assert.AreEqual(2, pending.Count);
        Assert.AreEqual(1L, pending[0].OrderId);
        Assert.AreEqual(3L, pending[1].OrderId);
    }

    [TestMethod]
    public void PriceTimePriority_Bids_HigherPriceMustBePrioritizedOverEarlierTime()
    {
        var book = new OrderBook();
        // 买单时间价格优先：出价更高者（10.2 > 10.1 > 10.0）必须排在更早提交的低价单之前
        var reqLow = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 100m);
        var reqMid = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.1, 200m);
        var reqHigh = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.2, 300m);

        book.Add(new SimulatedOrder(1L, reqLow, DateTime.UtcNow));
        book.Add(new SimulatedOrder(2L, reqMid, DateTime.UtcNow.AddMilliseconds(1)));
        book.Add(new SimulatedOrder(3L, reqHigh, DateTime.UtcNow.AddMilliseconds(2)));

        var pending = book.GetPendingOrders("sh.600000");
        Assert.IsNotNull(pending);
        Assert.AreEqual(3, pending.Count);
        Assert.AreEqual(3L, pending[0].OrderId, "出价 10.2 元的买单必须排在第 0 位（价格最高最优先）");
        Assert.AreEqual(2L, pending[1].OrderId, "出价 10.1 元的买单必须排在第 1 位");
        Assert.AreEqual(1L, pending[2].OrderId, "出价 10.0 元的买单必须排在第 2 位（出价最低最靠后）");
    }

    [TestMethod]
    public void PriceTimePriority_Asks_LowerPriceMustBePrioritizedOverEarlierTime()
    {
        var book = new OrderBook();
        // 卖单时间价格优先：售价更低者（10.0 < 10.1 < 10.2）必须排在更早提交的高价单之前
        var reqHigh = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.2, 100m);
        var reqMid = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.1, 200m);
        var reqLow = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.0, 300m);

        book.Add(new SimulatedOrder(1L, reqHigh, DateTime.UtcNow));
        book.Add(new SimulatedOrder(2L, reqMid, DateTime.UtcNow.AddMilliseconds(1)));
        book.Add(new SimulatedOrder(3L, reqLow, DateTime.UtcNow.AddMilliseconds(2)));

        var pending = book.GetPendingOrders("sh.600000");
        Assert.IsNotNull(pending);
        Assert.AreEqual(3, pending.Count);
        Assert.AreEqual(3L, pending[0].OrderId, "售价 10.0 元的卖单必须排在第 0 位（售价最低最优先）");
        Assert.AreEqual(2L, pending[1].OrderId, "售价 10.1 元的卖单必须排在第 1 位");
        Assert.AreEqual(1L, pending[2].OrderId, "售价 10.2 元的卖单必须排在第 2 位（售价最高最靠后）");
    }

    [TestMethod]
    public void TryGet_ExistingAndNonExistingOrder_ShouldReturnExpectedResult()
    {
        var book = new OrderBook();
        var req = new OrderRequest("sz.000001", OrderDirection.Sell, OrderType.Limit, 15.0, 500m);
        var order = new SimulatedOrder(100L, req, DateTime.UtcNow);

        book.Add(order);

        Assert.IsTrue(book.TryGet(100L, out var found));
        Assert.IsNotNull(found);
        Assert.AreEqual(100L, found.OrderId);
        Assert.AreEqual(15.0, found.Request.Price);

        Assert.IsFalse(book.TryGet(999L, out var notFound));
        Assert.IsNull(notFound);
    }

    [TestMethod]
    public void Clear_ShouldEmptyAllBucketsAndOrders()
    {
        var book = new OrderBook();
        book.Add(new SimulatedOrder(1L, new OrderRequest("s1", OrderDirection.Buy, OrderType.Limit, 1.0, 10m), DateTime.UtcNow));
        book.Add(new SimulatedOrder(2L, new OrderRequest("s2", OrderDirection.Sell, OrderType.Limit, 2.0, 20m), DateTime.UtcNow));

        Assert.AreEqual(2, book.Count);
        Assert.AreEqual(2, book.ActiveSymbols.Count());

        book.Clear();

        Assert.AreEqual(0, book.Count);
        Assert.AreEqual(0, book.ActiveSymbols.Count());
        Assert.IsNull(book.GetPendingOrders("s1"));
        Assert.IsNull(book.GetPendingOrders("s2"));
    }

    [TestMethod]
    public void Remove_NonExistentOrder_ShouldReturnFalse()
    {
        var book = new OrderBook();
        bool removed = book.Remove(999L, out var order);
        Assert.IsFalse(removed);
        Assert.IsNull(order);
    }

    [TestMethod]
    public void GetPendingOrders_NonExistentSymbol_ShouldReturnNull()
    {
        var book = new OrderBook();
        var result = book.GetPendingOrders("no-symbol");
        Assert.IsNull(result);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Add_NullOrder_ShouldThrowArgumentNullException()
    {
        var book = new OrderBook();
        book.Add(null!);
    }

    [TestMethod]
    public void MultipleSymbols_IndependentFIFOAndEmptyBucketCleanup()
    {
        var book = new OrderBook();
        var o1 = new SimulatedOrder(1L, new OrderRequest("A", OrderDirection.Buy, OrderType.Limit, 10, 100m), DateTime.UtcNow);
        var o2 = new SimulatedOrder(2L, new OrderRequest("B", OrderDirection.Buy, OrderType.Limit, 20, 200m), DateTime.UtcNow);
        var o3 = new SimulatedOrder(3L, new OrderRequest("A", OrderDirection.Sell, OrderType.Limit, 11, 100m), DateTime.UtcNow);

        book.Add(o1);
        book.Add(o2);
        book.Add(o3);

        Assert.AreEqual(3, book.Count);
        Assert.AreEqual(2, book.GetPendingOrders("A")!.Count);
        Assert.AreEqual(1, book.GetPendingOrders("B")!.Count);

        // 移除 B 的订单 -> B 桶应当被注销
        book.Remove(2L, out _);
        Assert.AreEqual(2, book.Count);
        Assert.IsNull(book.GetPendingOrders("B"));
        Assert.IsTrue(book.ActiveSymbols.SequenceEqual(new[] { "A" }));

        // 移除 A 的所有订单 -> A 桶被注销
        book.Remove(1L, out _);
        book.Remove(3L, out _);
        Assert.AreEqual(0, book.Count);
        Assert.IsNull(book.GetPendingOrders("A"));
        Assert.AreEqual(0, book.ActiveSymbols.Count());
    }
}
