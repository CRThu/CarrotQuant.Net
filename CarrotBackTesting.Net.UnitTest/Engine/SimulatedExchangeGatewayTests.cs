using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Broker;
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
        gateway.SendOrder(1L, req);

        Assert.IsNotNull(report);
        Assert.AreEqual(1L, report.OrderId);
        Assert.AreEqual("sh.600000", report.Symbol);
        Assert.AreEqual(10.0, report.FillPrice);
        Assert.AreEqual(1000m, report.FillQuantity);
        Assert.AreEqual(10.0, gateway.GetBar("sh.600000").Close);
    }

    [TestMethod]
    public void SendOrder_WithSlippage_ShouldNotPollutePublicMarketPrice()
    {
        // 配置买入正滑点 1% (0.01)
        var config = new BrokerConfig
        {
            Slippage = 0.01
        };
        var gateway = new SimulatedExchangeGateway(config);
        ExecutionReport? report = null;
        gateway.OnExecution += r => report = r;

        // 基准市价 100.0，买入 100 股
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 100.0, 100m);
        gateway.SendOrder(1L, req);

        Assert.IsNotNull(report);
        // 成交价包含 1% 滑点：100.0 * (1 + 0.01) = 101.0
        Assert.AreEqual(101.0, report.FillPrice, 1e-6, "成交回报应准确计入 1% 滑点");

        // 关键断言：公允市场价格必须保持 100.0，绝对不可被 101.0 的滑点成交价反向污染
        double fairPrice = gateway.GetBar("sh.600000").Close;
        Assert.AreEqual(100.0, fairPrice, 1e-6, "公允市场价格必须与订单执行滑点价物理隔离，严禁被成交滑点污染");
    }

    [TestMethod]
    public void SendOrder_UnfilledLimitOrder_ShouldEnqueueAndDispatchSubmitted()
    {
        var config = new BrokerConfig();
        var gateway = new SimulatedExchangeGateway(config);
        var now = DateTime.UtcNow;
        gateway.OnMarketData("sh.600000", now, new BarSnapshot(close: 10.0));

        long stateOrderId = 0;
        OrderStatus? stateStatus = null;
        gateway.OnOrderStateChanged += rep =>
        {
            stateOrderId = rep.OrderId;
            stateStatus = rep.Status;
        };

        // 市价 10.0，提交 9.0 买单 -> 未触碰，进入挂单簿
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 500m);
        gateway.SendOrder(101L, req);

        Assert.AreEqual(101L, stateOrderId);
        Assert.AreEqual(OrderStatus.Submitted, stateStatus);

        // 撤单
        gateway.CancelOrder(101L);
        Assert.AreEqual(OrderStatus.Canceled, stateStatus);
    }

    [TestMethod]
    public void OnMarketData_ShouldMatchPendingLimitOrders()
    {
        var config = new BrokerConfig { Slippage = 0.0, FeeRate = 0.0 };
        var gateway = new SimulatedExchangeGateway(config);
        var time1 = new DateTime(2025, 1, 6, 9, 30, 0);
        gateway.OnMarketData("sh.600000", time1, new BarSnapshot(close: 10.0));

        // 提交限价 9.5 买单
        gateway.SendOrder(101L, new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 1000m));

        ExecutionReport? report = null;
        gateway.OnExecution += r => report = r;

        // 行情下探到 9.4 -> 触发撮合
        var time2 = new DateTime(2025, 1, 6, 9, 35, 0);
        gateway.OnMarketData("sh.600000", time2, new BarSnapshot(close: 9.4));

        Assert.IsNotNull(report);
        Assert.AreEqual(101L, report.OrderId);
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

        gateway.SendOrder(1L, new OrderRequest("s", OrderDirection.Buy, OrderType.Market, 10.0, 100m));

        Assert.IsNotNull(report);
        Assert.AreEqual(customTime, report.Time);
    }

    [TestMethod]
    public void CancelOrder_NonExistent_ShouldNotThrowOrFireEvent()
    {
        var config = new BrokerConfig();
        var gateway = new SimulatedExchangeGateway(config);

        bool fired = false;
        gateway.OnOrderStateChanged += _ => fired = true;

        gateway.CancelOrder(999L);
        Assert.IsFalse(fired);
    }

    [TestMethod]
    public void Step_EmptyOrderBook_ShouldReturnQuickly()
    {
        var gateway = new SimulatedExchangeGateway(new BrokerConfig());
        var dates = new List<MarketTime> { MarketTime.UtcNow };
        var metadata = new DynamicMarketMetadata(new[] { "s" }, dates);
        using var matrix = new ChunkMatrix2D<double>(1, 1024, 1);
        var registry = new DummyTimeseriesRegistry(matrix, metadata);

        bool reportFired = false;
        gateway.OnExecution += _ => reportFired = true;

        // 挂单簿为空时直接安全返回，且不派发任何成交回报
        gateway.Step(dates[0], registry, 0);

        Assert.IsFalse(reportFired, "空挂单簿在时间步推进时不应触发任何成交回报");
    }

    [TestMethod]
    public void Step_WithPendingOrder_ShouldMatchFromCloseMatrix()
    {
        var config = new BrokerConfig { Slippage = 0.0, FeeRate = 0.0 };
        var gateway = new SimulatedExchangeGateway(config);
        gateway.OnMarketData("sh.600000", new DateTime(2025, 1, 2), new BarSnapshot(close: 10.0));

        // 挂单 9.5 买入
        gateway.SendOrder(101L, new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 100m));

        // 构造 close 矩阵：第 0 行价格为 9.3 元
        using var matrix = new ChunkMatrix2D<double>(1, 1024, 1);
        matrix[0, 0] = 9.3;

        var dates = new List<MarketTime> { new MarketTime(2025, 1, 2) };
        var metadata = new DynamicMarketMetadata(new[] { "sh.600000" }, dates);
        var registry = new DummyTimeseriesRegistry(matrix, metadata);

        ExecutionReport? report = null;
        gateway.OnExecution += r => report = r;

        gateway.Step(dates[0], registry, 0);

        Assert.IsNotNull(report);
        Assert.AreEqual(101L, report.OrderId);
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
        gateway.OnMarketData("sh.600000", time1, new BarSnapshot(close: 10.0));

        gateway.SendOrder(101L, new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 100m));

        OrderStatus? finalStatus = null;
        string? rejectReason = null;
        gateway.OnOrderStateChanged += rep =>
        {
            if (rep.OrderId == 101L)
            {
                finalStatus = rep.Status;
                rejectReason = rep.Reason;
            }
        };
        bool executed = false;
        gateway.OnExecution += _ => executed = true;

        var bar = new BarSnapshot(10.0, 10.2, 9.0, 9.2, 5000.0);
        var time2 = new DateTime(2025, 1, 6, 9, 31, 0);
        gateway.OnMarketData("sh.600000", time2, in bar);

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
        gateway.OnMarketData("sh.600000", new DateTime(2025, 1, 6, 9, 30, 0), new BarSnapshot(close: 10.0));

        // 委托 200 股
        gateway.SendOrder(101L, new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 200m));

        OrderStatus? finalStatus = null;
        gateway.OnOrderStateChanged += rep =>
        {
            if (rep.OrderId == 101L) finalStatus = rep.Status;
        };

        // 当前 Bar 成交量 1000 股，10% 上限为 100 股 < 200 股
        var bar = new BarSnapshot(10.0, 10.0, 9.4, 9.4, 1000.0);
        gateway.OnMarketData("sh.600000", new DateTime(2025, 1, 6, 9, 35, 0), in bar);

        Assert.AreEqual(OrderStatus.Rejected, finalStatus, "超过流动性上限必须拒单");
    }

    [TestMethod]
    public void OnMarketData_InvalidInputs_ShouldBeSafelyIgnored()
    {
        var gateway = new SimulatedExchangeGateway(new BrokerConfig());
        gateway.OnMarketData("sh.600000", DateTime.UtcNow, new BarSnapshot(close: 10.0));

        // 非法价格不应更新
        gateway.OnMarketData("sh.600000", DateTime.UtcNow, new BarSnapshot(close: -5.0));
        Assert.AreEqual(10.0, gateway.GetBar("sh.600000").Close);

        gateway.OnMarketData("sh.600000", DateTime.UtcNow, new BarSnapshot(close: double.NaN));
        Assert.AreEqual(10.0, gateway.GetBar("sh.600000").Close);

        gateway.OnMarketData("sh.600000", DateTime.UtcNow, new BarSnapshot(close: double.PositiveInfinity));
        Assert.AreEqual(10.0, gateway.GetBar("sh.600000").Close);

        gateway.OnMarketData("   ", DateTime.UtcNow, new BarSnapshot(close: 20.0));
        Assert.IsFalse(gateway.GetBar("   ").IsValid);
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
        public bool HasField(string fieldName) => string.Equals(fieldName, "close", StringComparison.OrdinalIgnoreCase);
        public IReadOnlyList<T> GetSeries<T>(string fieldName) where T : unmanaged => throw new NotImplementedException();
        public void StepTo(int index) { }
        public bool IsUpdated(int tableIndex) => false;
        public bool IsUpdated(string tableName) => false;
        public int GetTableIndex(string tableName) => 0;
    }

    #region 停牌拦截与日切撤单测试 (Suspended Guard & Day Order)

    internal class MultiMatrixTimeseriesRegistry : ITimeseriesRegistry
    {
        private readonly Dictionary<string, IReadOnlyMatrix2D<double>> _matrices;
        public IMarketMetadata Metadata { get; }

        public MultiMatrixTimeseriesRegistry(IMarketMetadata metadata, Dictionary<string, IReadOnlyMatrix2D<double>> matrices)
        {
            Metadata = metadata;
            _matrices = matrices;
        }

        public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
        {
            if (_matrices.TryGetValue(fieldName, out var mat))
            {
                return (IReadOnlyMatrix2D<T>)mat;
            }
            throw new KeyNotFoundException($"Field '{fieldName}' not found.");
        }

        public bool HasField(string fieldName) => _matrices.ContainsKey(fieldName);
        public IReadOnlyList<T> GetSeries<T>(string fieldName) where T : unmanaged => throw new NotImplementedException();
        public void StepTo(int index) { }
        public bool IsUpdated(int tableIndex) => false;
        public bool IsUpdated(string tableName) => false;
        public int GetTableIndex(string tableName) => 0;
    }

    private static ITimeseriesRegistry CreateMockTimeseries(
        string[] symbols,
        MarketTime[] dates,
        double[,] closePrices)
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

        return new MultiMatrixTimeseriesRegistry(metadata, matrices);
    }

    [TestMethod]
    public void SendOrder_SuspendedOrUnlistedSymbol_ShouldRejectAtGateAndUnfreeze()
    {
        var symbols = new[] { "sh.600000", "sh.600001" };
        var dates = new[] { (MarketTime)new DateTime(2025, 1, 2) };
        // sh.600000 正常交易 (10.0)，sh.600001 全天停牌 (NaN)
        var closePrices = new double[,] { { 10.0, double.NaN } };

        var timeseries = CreateMockTimeseries(symbols, dates, closePrices);
        var config = new BrokerConfig { InitialCash = 100_000.0 };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);

        // 推进网关时步绑定时序
        gateway.Step(dates[0], timeseries, 0);

        // 对停牌标的 sh.600001 提交限价买单
        var req = new OrderRequest("sh.600001", OrderDirection.Buy, OrderType.Limit, 15.0, 100m);
        long orderId = broker.SubmitOrder(req);

        // 网关必须在前置守卫拦截拒单，严禁放入挂单簿排队
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(orderId));
        Assert.AreEqual(0.0, broker.FrozenCash);
    }

    [TestMethod]
    public void Step_TimeInForce_DayOrder_ShouldCancelAndUnfreezeAcrossDays()
    {
        var symbols = new[] { "sh.600000" };
        var dates = new[] { (MarketTime)new DateTime(2025, 1, 2), (MarketTime)new DateTime(2025, 1, 3) };
        var closePrices = new double[,] { { 10.0 }, { 10.5 } };
        var timeseries = CreateMockTimeseries(symbols, dates, closePrices);

        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            Slippage = 0.0,
            FeeRate = 0.0,
            DefaultTimeInForce = TimeInForce.Day
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);

        // Day 1
        gateway.Step(dates[0], timeseries, 0);
        ((IBrokerEngineDriver)broker).Step(dates[0]);

        // 提交限价 9.0 买单（默认 Day 时效，未触碰 -> 排队挂单）
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 1000m);
        long orderId = broker.SubmitOrder(req);

        Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
        Assert.AreEqual(9000.0, broker.FrozenCash, 1e-4);

        // Day 2 (跨日推进)
        gateway.Step(dates[1], timeseries, 1);
        ((IBrokerEngineDriver)broker).Step(dates[1]);

        // 验证：Day 挂单在跨日结算时被集中撤销，且 Broker 收到撤单事件后全额解冻资金
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(orderId));
        Assert.AreEqual(0.0, broker.FrozenCash);
    }

    [TestMethod]
    public void Step_TimeInForce_GoodTilCanceled_ShouldPreservePendingOrdersAcrossDays()
    {
        var symbols = new[] { "sh.600000" };
        var dates = new[] { (MarketTime)new DateTime(2025, 1, 2), (MarketTime)new DateTime(2025, 1, 3) };
        var closePrices = new double[,] { { 10.0 }, { 10.5 } };
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

        // 提交显式 GTC 时效的限价 9.0 买单
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, 9.0, 1000m, timeInForce: TimeInForce.GoodTilCanceled);
        long orderId = broker.SubmitOrder(req);

        Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
        Assert.AreEqual(9000.0, broker.FrozenCash, 1e-4);

        // Day 2 (跨日推进)
        gateway.Step(dates[1], timeseries, 1);
        ((IBrokerEngineDriver)broker).Step(dates[1]);

        // 验证：GTC 挂单继续保留在挂单簿排队，资金维持冻结
        Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
        Assert.AreEqual(9000.0, broker.FrozenCash, 1e-4);
    }

    #endregion
}
