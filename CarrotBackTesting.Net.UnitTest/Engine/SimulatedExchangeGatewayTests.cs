using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Exchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class SimulatedExchangeGatewayTests
{
    [TestMethod]
    public void SendOrder_MarketBuy_ShouldFillImmediatelyAndDispatchOnExecution()
    {
        var config = new BrokerConfig { Slippage = 0.0, FeeRate = 0.0 };
        var gateway = new SimulatedExchangeGateway(config);

        ExecutionReport? report = null;
        gateway.OnExecution += r => report = r;

        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
        gateway.SendOrder("ord-1", req);

        Assert.IsNotNull(report);
        Assert.AreEqual("ord-1", report.OrderId);
        Assert.AreEqual("sh.600000", report.Symbol);
        Assert.AreEqual(10.0, report.FillPrice);
        Assert.AreEqual(1000m, report.FillQuantity);
        Assert.AreEqual(10.0, gateway.GetMarketPrice("sh.600000"));
    }

    [TestMethod]
    public void SendOrder_UnfilledLimitOrder_ShouldEnqueueAndDispatchSubmitted()
    {
        var config = new BrokerConfig();
        var gateway = new SimulatedExchangeGateway(config);
        gateway.UpdateMarketPrice("sh.600000", 10.0);

        string? stateOrderId = null;
        OrderStatus? stateStatus = null;
        gateway.OnOrderStateChanged += (id, s) =>
        {
            stateOrderId = id;
            stateStatus = s;
        };

        // 市价 10.0，提交 9.0 买单 -> 未触碰，进入挂单簿
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 500m);
        gateway.SendOrder("limit-1", req);

        Assert.AreEqual("limit-1", stateOrderId);
        Assert.AreEqual(OrderStatus.Submitted, stateStatus);

        // 撤单
        gateway.CancelOrder("limit-1");
        Assert.AreEqual(OrderStatus.Canceled, stateStatus);
    }

    [TestMethod]
    public void UpdateMarketPrice_ShouldMatchPendingLimitOrders()
    {
        var config = new BrokerConfig { Slippage = 0.0, FeeRate = 0.0 };
        var gateway = new SimulatedExchangeGateway(config);
        gateway.UpdateMarketPrice("sh.600000", 10.0);

        // 提交限价 9.5 买单
        gateway.SendOrder("limit-1", new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 1000m));

        ExecutionReport? report = null;
        gateway.OnExecution += r => report = r;

        // 行情下探到 9.4 -> 触发撮合
        gateway.UpdateMarketPrice("sh.600000", 9.4);

        Assert.IsNotNull(report);
        Assert.AreEqual("limit-1", report.OrderId);
        Assert.AreEqual(9.5, report.FillPrice, "Maker 买单成交在委托限价 9.5");
        Assert.AreEqual(1000m, report.FillQuantity);
    }

    [TestMethod]
    public void SetCurrentTime_ShouldStampExecutionReportCorrectly()
    {
        var config = new BrokerConfig();
        var gateway = new SimulatedExchangeGateway(config);
        var customTime = new DateTime(2025, 6, 1, 14, 30, 0);

        gateway.SetCurrentTime(customTime);

        ExecutionReport? report = null;
        gateway.OnExecution += r => report = r;

        gateway.SendOrder("ord-time", new OrderRequest("s", OrderDirection.Buy, OrderType.Market, 10.0, 100m));

        Assert.IsNotNull(report);
        Assert.AreEqual(customTime, report.Time);
    }

    [TestMethod]
    public void CancelOrder_NonExistent_ShouldNotThrowOrFireEvent()
    {
        var config = new BrokerConfig();
        var gateway = new SimulatedExchangeGateway(config);

        bool fired = false;
        gateway.OnOrderStateChanged += (_, _) => fired = true;

        gateway.CancelOrder("non-existent-order");
        Assert.IsFalse(fired);
    }

    [TestMethod]
    public void Step_EmptyOrderBook_ShouldReturnQuickly()
    {
        var gateway = new SimulatedExchangeGateway(new BrokerConfig());
        var dates = new List<DateTime> { DateTime.UtcNow };
        var metadata = new DynamicMarketMetadata(new[] { "s" }, dates);
        using var matrix = new ChunkMatrix2D<double>(1, 1024, 1);
        var registry = new DummyTimeseriesRegistry(matrix, metadata);

        // 挂单簿为空时直接安全返回
        gateway.Step(dates[0], registry, 0);
    }

    [TestMethod]
    public void Step_WithPendingOrder_ShouldMatchFromCloseMatrix()
    {
        var config = new BrokerConfig { Slippage = 0.0, FeeRate = 0.0 };
        var gateway = new SimulatedExchangeGateway(config);
        gateway.UpdateMarketPrice("sh.600000", 10.0);

        // 挂单 9.5 买入
        gateway.SendOrder("step-1", new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 100m));

        // 构造 close 矩阵：第 0 行价格为 9.3 元
        using var matrix = new ChunkMatrix2D<double>(1, 1024, 1);
        matrix[0, 0] = 9.3;

        var dates = new List<DateTime> { new DateTime(2025, 1, 2) };
        var metadata = new DynamicMarketMetadata(new[] { "sh.600000" }, dates);
        var registry = new DummyTimeseriesRegistry(matrix, metadata);

        ExecutionReport? report = null;
        gateway.OnExecution += r => report = r;

        gateway.Step(dates[0], registry, 0);

        Assert.IsNotNull(report);
        Assert.AreEqual("step-1", report.OrderId);
        Assert.AreEqual(9.5, report.FillPrice);
        Assert.AreEqual(100m, report.FillQuantity);
    }

    private class DummyTimeseriesRegistry : ITimeseriesRegistry
    {
        private readonly IReadOnlyMatrix2D<double> _matrix;
        private readonly IMarketMetadata _metadata;

        public DummyTimeseriesRegistry(IReadOnlyMatrix2D<double> matrix, IMarketMetadata metadata)
        {
            _matrix = matrix;
            _metadata = metadata;
        }

        public IMarketMetadata Metadata => _metadata;
        public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged => (IReadOnlyMatrix2D<T>)_matrix;
        public bool HasField(string fieldName) => true;
        public IReadOnlyList<T> GetSeries<T>(string fieldName) where T : unmanaged => throw new NotImplementedException();
        public void StepTo(int index) { }
        public bool IsUpdated(int tableIndex) => false;
        public bool IsUpdated(string tableName) => false;
        public int GetTableIndex(string tableName) => 0;
    }
}
