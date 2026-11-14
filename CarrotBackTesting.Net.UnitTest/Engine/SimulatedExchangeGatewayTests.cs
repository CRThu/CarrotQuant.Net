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
        var now = DateTime.UtcNow;
        gateway.OnMarketData("sh.600000", 10.0, now);

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
    public void OnMarketData_ShouldMatchPendingLimitOrders()
    {
        var config = new BrokerConfig { Slippage = 0.0, FeeRate = 0.0 };
        var gateway = new SimulatedExchangeGateway(config);
        var time1 = new DateTime(2025, 1, 6, 9, 30, 0);
        gateway.OnMarketData("sh.600000", 10.0, time1);

        // 提交限价 9.5 买单
        gateway.SendOrder("limit-1", new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 1000m));

        ExecutionReport? report = null;
        gateway.OnExecution += r => report = r;

        // 行情下探到 9.4 -> 触发撮合
        var time2 = new DateTime(2025, 1, 6, 9, 35, 0);
        gateway.OnMarketData("sh.600000", 9.4, time2);

        Assert.IsNotNull(report);
        Assert.AreEqual("limit-1", report.OrderId);
        Assert.AreEqual(9.5, report.FillPrice, "Maker 买单成交在委托限价 9.5");
        Assert.AreEqual(1000m, report.FillQuantity);
        Assert.AreEqual(time2, report.Time, "成交时间戳必须与行情时间戳精确一致");
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
        gateway.OnMarketData("sh.600000", 10.0, new DateTime(2025, 1, 1));

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

    [TestMethod]
    public void OnMarketData_WithBar_TriggeringLimitCheck_ShouldRejectPendingOrder()
    {
        var config = new BrokerConfig
        {
            Slippage = 0.0,
            FeeRate = 0.0,
            CheckLimit = true,
            OnCheckLimit = (req, bar) => false // 模拟触发跌停拦截
        };
        var gateway = new SimulatedExchangeGateway(config);
        var time1 = new DateTime(2025, 1, 6, 9, 30, 0);
        gateway.OnMarketData("sh.600000", 10.0, time1);

        gateway.SendOrder("limit-reject", new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 100m));

        OrderStatus? finalStatus = null;
        gateway.OnOrderStateChanged += (id, s) =>
        {
            if (id == "limit-reject") finalStatus = s;
        };
        bool executed = false;
        gateway.OnExecution += _ => executed = true;

        var bar = CreateBar(10.0, 10.2, 9.0, 9.2, 5000.0);
        var time2 = new DateTime(2025, 1, 6, 9, 31, 0);
        gateway.OnMarketData("sh.600000", 9.2, time2, bar);

        Assert.AreEqual(OrderStatus.Rejected, finalStatus);
        Assert.IsFalse(executed, "被涨跌停拦截的挂单绝不能产生成交回报");
    }

    [TestMethod]
    public void OnMarketData_WithBar_VolumeLimitExceeded_ShouldRejectPendingOrder()
    {
        var config = new BrokerConfig
        {
            Slippage = 0.0,
            FeeRate = 0.0,
            VolumeLimit = 0.1 // 单笔不能超过 Bar 成交量的 10%
        };
        var gateway = new SimulatedExchangeGateway(config);
        gateway.OnMarketData("sh.600000", 10.0, new DateTime(2025, 1, 6, 9, 30, 0));

        // 委托 200 股
        gateway.SendOrder("limit-vol-exceeded", new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 200m));

        OrderStatus? finalStatus = null;
        gateway.OnOrderStateChanged += (id, s) =>
        {
            if (id == "limit-vol-exceeded") finalStatus = s;
        };

        // 当前 Bar 成交量 1000 股，10% 上限为 100 股 < 200 股
        var bar = CreateBar(10.0, 10.0, 9.4, 9.4, 1000.0);
        gateway.OnMarketData("sh.600000", 9.4, new DateTime(2025, 1, 6, 9, 35, 0), bar);

        Assert.AreEqual(OrderStatus.Rejected, finalStatus, "超过流动性上限必须拒单");
    }

    [TestMethod]
    public void OnMarketData_InvalidInputs_ShouldBeSafelyIgnored()
    {
        var gateway = new SimulatedExchangeGateway(new BrokerConfig());
        gateway.OnMarketData("sh.600000", 10.0, DateTime.UtcNow);

        // 非法价格不应更新
        gateway.OnMarketData("sh.600000", -5.0, DateTime.UtcNow);
        Assert.AreEqual(10.0, gateway.GetMarketPrice("sh.600000"));

        gateway.OnMarketData("sh.600000", double.NaN, DateTime.UtcNow);
        Assert.AreEqual(10.0, gateway.GetMarketPrice("sh.600000"));

        gateway.OnMarketData("sh.600000", double.PositiveInfinity, DateTime.UtcNow);
        Assert.AreEqual(10.0, gateway.GetMarketPrice("sh.600000"));

        gateway.OnMarketData("   ", 20.0, DateTime.UtcNow);
        Assert.AreEqual(0.0, gateway.GetMarketPrice("   "));
    }

    private static IDataRow CreateBar(double open, double high, double low, double close, double volume)
    {
        var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = 0,
            ["high"] = 1,
            ["low"] = 2,
            ["close"] = 3,
            ["volume"] = 4
        };

        var values = new[]
        {
            open.ToString(System.Globalization.CultureInfo.InvariantCulture),
            high.ToString(System.Globalization.CultureInfo.InvariantCulture),
            low.ToString(System.Globalization.CultureInfo.InvariantCulture),
            close.ToString(System.Globalization.CultureInfo.InvariantCulture),
            volume.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        return new DataRow(values, cols);
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
