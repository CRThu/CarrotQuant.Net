using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class BacktestingEngineRecorderIntegrationTests
{
    private class SimpleMetadata : IMarketMetadata
    {
        public IReadOnlyList<MarketTime> TradeDates { get; }
        public IReadOnlyList<string> Symbols { get; }
        public IReadOnlyList<string> FieldNames { get; } = new[] { "close", "open", "high", "low", "volume" };

        public SimpleMetadata(IReadOnlyList<MarketTime> dates, IReadOnlyList<string> symbols)
        {
            TradeDates = dates;
            Symbols = symbols;
        }

        public SimpleMetadata(IReadOnlyList<DateTime> dates, IReadOnlyList<string> symbols)
            : this(dates.Select(d => (MarketTime)d).ToList(), symbols)
        {
        }

        public int GetSymbolIndex(string symbol)
        {
            for (int i = 0; i < Symbols.Count; i++) if (Symbols[i] == symbol) return i;
            return -1;
        }

        public int GetDateIndex(MarketTime date)
        {
            for (int i = 0; i < TradeDates.Count; i++) if (TradeDates[i] == date) return i;
            return -1;
        }
        public Type GetFieldType(string fieldName) => typeof(double);
    }

    private class MockTimeseriesRegistry : ITimeseriesRegistry
    {
        public IMarketMetadata Metadata { get; }
        public List<int> SteppedRows { get; } = new();
        private readonly Dictionary<string, object> _matrices = new(StringComparer.OrdinalIgnoreCase);

        public MockTimeseriesRegistry(IMarketMetadata metadata)
        {
            Metadata = metadata;
        }

        public void SetMatrix<T>(string fieldName, IReadOnlyMatrix2D<T> matrix) where T : unmanaged
        {
            _matrices[fieldName] = matrix;
        }

        public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
        {
            if (_matrices.TryGetValue(fieldName, out var m)) return (IReadOnlyMatrix2D<T>)m;
            return null!;
        }
        public bool HasField(string fieldName) => _matrices.ContainsKey(fieldName);
        public void StepTo(int currentLogicalRow) => SteppedRows.Add(currentLogicalRow);
        public int RegisterTable(string tableName) => 0;
        public int GetTableIndex(string tableName) => 0;
        public string GetTableName(int tableIndex) => string.Empty;
    }

    private class TradingAndSignalingStrategy : IStrategy
    {
        public string Name => "TradingAndSignalingStrategy";
        private int _step = 0;

        public void OnStart(IEngineContext context) { }
        public void OnStop(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            _step++;

            if (_step == 1)
            {
                // 第 1 步：买入 100 股
                context.Broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 100m)
                {
                    OrderTag = "BuySignal"
                });
            }
            else if (_step == 2)
            {
                // 第 2 步：打点信号
                context.RecordSignal("sh.600000", 0.88, "Alpha", "MomentumPeak");
            }
            else if (_step == 3)
            {
                // 第 3 步：卖出平仓
                context.Broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 12.0, 100m)
                {
                    OrderTag = "TakeProfit"
                });
            }
        }
    }

    private class BuyAndHoldStrategy : IStrategy
    {
        public string Name => "BuyAndHoldStrategy";
        private bool _bought = false;

        public void OnStart(IEngineContext context) { }
        public void OnStop(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            if (!_bought)
            {
                _bought = true;
                context.Broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 500m)
                {
                    OrderTag = "HoldForever"
                });
            }
        }
    }

    [TestMethod]
    public void BacktestingEngine_TradingRun_ShouldSinkTradesSnapshotsAndSignals()
    {
        var dates = new[]
        {
            new DateTime(2024, 1, 2, 9, 30, 0),
            new DateTime(2024, 1, 2, 9, 31, 0),
            new DateTime(2024, 1, 3, 9, 30, 0),
            new DateTime(2024, 1, 3, 9, 31, 0)
        };
        var symbols = new[] { "sh.600000" };
        var metadata = new SimpleMetadata(dates, symbols);
        var ts = new MockTimeseriesRegistry(metadata);
        using var closeMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 1024);
        closeMatrix.SetElement(0, 0, 10.0);
        closeMatrix.SetElement(1, 0, 10.0);
        closeMatrix.SetElement(2, 0, 12.0);
        closeMatrix.SetElement(3, 0, 12.0);
        ts.SetMatrix("close", closeMatrix);

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100000.0,
            FeeRate = 0.0001,
            MinFee = 0.0,
            Slippage = 0.0
        };
        var gateway = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(gateway, brokerConfig);

        // 设置仿真交易所最新价格
        gateway.OnMarketData("sh.600000", 10.0, dates[0]);

        var pipeline = StrategyPipeline.Sequential().Add(new TradingAndSignalingStrategy());
        var recorder = new TradingRecorder();
        var monitor = new ConsoleMonitor(showProgress: false);

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: new EngineConfig(),
            monitor: monitor,
            simExchange: gateway,
            recorder: recorder);

        // 运行回测
        engine.Run();

        // 提取权威结果集
        var result = engine.GetResult();

        // 1. 资产步进曲线覆盖全量 4 个时间步
        Assert.AreEqual(4, result.EquityCurve.Count);

        // 2. 日终快照沉淀 (2024-01-02 与 2024-01-03)
        Assert.AreEqual(2, result.DailySnapshots.Count);

        // 3. 产生 1 笔平仓闭环交易
        Assert.AreEqual(1, result.ClosedTrades.Count);
        var trade = result.ClosedTrades[0];
        Assert.AreEqual("sh.600000", trade.Symbol);
        Assert.AreEqual(TradeSide.Long, trade.Side);
        Assert.AreEqual(100m, trade.Quantity);
        Assert.AreEqual(10.0, trade.EntryPrice);
        Assert.AreEqual(12.0, trade.ExitPrice);
        Assert.AreEqual("BuySignal", trade.EntryTag);
        Assert.AreEqual("TakeProfit", trade.ExitTag);
        Assert.IsTrue(trade.RealizedPnL > 190.0); // 100 股 * 2 元 - 手续费

        // 4. 信号打点沉淀
        Assert.AreEqual(1, result.Signals.Count);
        Assert.AreEqual("MomentumPeak", result.Signals[0].Reason);

        // 5. 终点持仓已清空
        Assert.AreEqual(0, result.OpenPositions.Count);

        // 6. 交割单沉淀 (2 笔：买入与卖出交割单)
        Assert.AreEqual(2, result.Settlements.Count);
        Assert.AreEqual(OrderDirection.Buy, result.Settlements[0].Direction);
        Assert.AreEqual(100m, result.Settlements[0].Quantity);
        Assert.AreEqual(10.0, result.Settlements[0].Price);
        Assert.AreEqual(1000.0, result.Settlements[0].Turnover);
        Assert.AreEqual("BuySignal", result.Settlements[0].OrderTag);

        Assert.AreEqual(OrderDirection.Sell, result.Settlements[1].Direction);
        Assert.AreEqual(100m, result.Settlements[1].Quantity);
        Assert.AreEqual(12.0, result.Settlements[1].Price);
        Assert.AreEqual(1200.0, result.Settlements[1].Turnover);
        Assert.AreEqual("TakeProfit", result.Settlements[1].OrderTag);
        Assert.IsTrue(result.Settlements[1].RealizedPnL > 190.0);
    }

    [TestMethod]
    public void BacktestingEngine_UnclosedPositions_ShouldBeCapturedInOpenPositions()
    {
        var dates = new[]
        {
            new DateTime(2024, 1, 2, 9, 30, 0),
            new DateTime(2024, 1, 3, 9, 30, 0)
        };
        var symbols = new[] { "sh.600000" };
        var metadata = new SimpleMetadata(dates, symbols);
        var ts = new MockTimeseriesRegistry(metadata);
        using var closeMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 1024);
        closeMatrix.SetElement(0, 0, 10.0);
        closeMatrix.SetElement(1, 0, 10.0);
        closeMatrix.SetElement(2, 0, 12.0);
        closeMatrix.SetElement(3, 0, 12.0);
        ts.SetMatrix("close", closeMatrix);

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100000.0,
            FeeRate = 0.0,
            Slippage = 0.0
        };
        var gateway = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(gateway, brokerConfig);
        gateway.OnMarketData("sh.600000", 10.0, dates[0]);

        var pipeline = StrategyPipeline.Sequential().Add(new BuyAndHoldStrategy());
        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: new EngineConfig(),
            simExchange: gateway);

        engine.Run();

        var result = engine.GetResult();

        // 买入后未平仓，已平仓列表为 0
        Assert.AreEqual(0, result.ClosedTrades.Count);

        // 但 OpenPositions 必须精准捕获 500 股未平仓头寸，彻底消除幸存者偏差！
        Assert.AreEqual(1, result.OpenPositions.Count);
        var openPos = result.OpenPositions[0];
        Assert.AreEqual("sh.600000", openPos.Symbol);
        Assert.AreEqual(500m, openPos.Quantity);
        Assert.AreEqual(10.0, openPos.CostPrice);
        Assert.AreEqual(5000.0, openPos.MarketValue);

        // 交割单沉淀 (1 笔买入交割单)
        Assert.AreEqual(1, result.Settlements.Count);
        Assert.AreEqual(OrderDirection.Buy, result.Settlements[0].Direction);
        Assert.AreEqual(500m, result.Settlements[0].Quantity);
        Assert.AreEqual(10.0, result.Settlements[0].Price);
        Assert.AreEqual(5000.0, result.Settlements[0].Turnover);
    }
}
