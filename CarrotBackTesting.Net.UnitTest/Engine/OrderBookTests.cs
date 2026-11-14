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
        var order = new SimulatedOrder("ord-1", req, DateTime.UtcNow);

        book.Add(order);
        Assert.AreEqual(1, book.Count);
        Assert.IsTrue(book.ActiveSymbols.Contains("sh.600000"));

        bool removed = book.Remove("ord-1", out var removedOrder);
        Assert.IsTrue(removed);
        Assert.IsNotNull(removedOrder);
        Assert.AreEqual("ord-1", removedOrder.OrderId);
        Assert.AreEqual(0, book.Count);
        Assert.IsFalse(book.ActiveSymbols.Contains("sh.600000"), "桶空时必须自动注销键，杜绝内存泄漏");
    }

    [TestMethod]
    public void FIFO_MultipleOrdersSameSymbol_ShouldPreserveSequence()
    {
        var book = new OrderBook();
        var req1 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 100m);
        var req2 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.1, 200m);
        var req3 = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.2, 300m);

        book.Add(new SimulatedOrder("ord-1", req1, DateTime.UtcNow));
        book.Add(new SimulatedOrder("ord-2", req2, DateTime.UtcNow));
        book.Add(new SimulatedOrder("ord-3", req3, DateTime.UtcNow));

        var pending = book.GetPendingOrders("sh.600000");
        Assert.IsNotNull(pending);
        Assert.AreEqual(3, pending.Count);
        Assert.AreEqual("ord-1", pending[0].OrderId);
        Assert.AreEqual("ord-2", pending[1].OrderId);
        Assert.AreEqual("ord-3", pending[2].OrderId);

        // 移除中间的订单
        book.Remove("ord-2", out _);
        pending = book.GetPendingOrders("sh.600000");
        Assert.IsNotNull(pending);
        Assert.AreEqual(2, pending.Count);
        Assert.AreEqual("ord-1", pending[0].OrderId);
        Assert.AreEqual("ord-3", pending[1].OrderId);
    }

    [TestMethod]
    public void TryGet_ExistingAndNonExistingOrder_ShouldReturnExpectedResult()
    {
        var book = new OrderBook();
        var req = new OrderRequest("sz.000001", OrderDirection.Sell, OrderType.Limit, 15.0, 500m);
        var order = new SimulatedOrder("ord-100", req, DateTime.UtcNow);

        book.Add(order);

        Assert.IsTrue(book.TryGet("ord-100", out var found));
        Assert.IsNotNull(found);
        Assert.AreEqual("ord-100", found.OrderId);
        Assert.AreEqual(15.0, found.Request.Price);

        Assert.IsFalse(book.TryGet("non-existent", out var notFound));
        Assert.IsNull(notFound);
    }

    [TestMethod]
    public void Clear_ShouldEmptyAllBucketsAndOrders()
    {
        var book = new OrderBook();
        book.Add(new SimulatedOrder("o-1", new OrderRequest("s1", OrderDirection.Buy, OrderType.Limit, 1.0, 10m), DateTime.UtcNow));
        book.Add(new SimulatedOrder("o-2", new OrderRequest("s2", OrderDirection.Sell, OrderType.Limit, 2.0, 20m), DateTime.UtcNow));

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
        bool removed = book.Remove("no-such-order", out var order);
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
        var o1 = new SimulatedOrder("o1", new OrderRequest("A", OrderDirection.Buy, OrderType.Limit, 10, 100m), DateTime.UtcNow);
        var o2 = new SimulatedOrder("o2", new OrderRequest("B", OrderDirection.Buy, OrderType.Limit, 20, 200m), DateTime.UtcNow);
        var o3 = new SimulatedOrder("o3", new OrderRequest("A", OrderDirection.Sell, OrderType.Limit, 11, 100m), DateTime.UtcNow);

        book.Add(o1);
        book.Add(o2);
        book.Add(o3);

        Assert.AreEqual(3, book.Count);
        Assert.AreEqual(2, book.GetPendingOrders("A")!.Count);
        Assert.AreEqual(1, book.GetPendingOrders("B")!.Count);

        // 移除 B 的订单 -> B 桶应当被注销
        book.Remove("o2", out _);
        Assert.AreEqual(2, book.Count);
        Assert.IsNull(book.GetPendingOrders("B"));
        Assert.IsTrue(book.ActiveSymbols.SequenceEqual(new[] { "A" }));

        // 移除 A 的所有订单 -> A 桶被注销
        book.Remove("o1", out _);
        book.Remove("o3", out _);
        Assert.AreEqual(0, book.Count);
        Assert.IsNull(book.GetPendingOrders("A"));
        Assert.AreEqual(0, book.ActiveSymbols.Count());
    }
}
