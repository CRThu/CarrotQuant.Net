using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Strategy;
using CarrotBackTesting.Net.UnitTest.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Integration;

/// <summary>
/// 高级策略编排与跨模态端到端集成测试（Advanced Strategy Pipeline & Multi-Modal Integration Tests）。
/// 包含：
/// 1. 生产-消费模式与强类型黑板解耦（AlphaProducer -> ExecutionStrategy -> SignalAnalyzer）；
/// 2. 标的静态维表倒排选股与异构事件流驱动（东财概念维表选股 + 除权因子推送）；
/// 3. Roslyn 外部 C# 源码动态编译与 YAML 声明式会话一键驱动；
/// 4. 多标的横向截面多因子排序与组合再平衡调仓（Cross-Sectional Rebalancing）；
/// 5. 多核并发策略管线（ParallelPipeline.Concurrent = true）与黑板并发线程安全。
/// </summary>
[TestClass]
public class AdvancedStrategyPipelineIntegrationTests
{
    private string _csvRoot = "";

    [TestInitialize]
    public void Setup()
    {
        _csvRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
    }

    #region 1. 生产-消费模式与强类型黑板解耦

    public record AlphaScoreSignal(string Symbol, double Score, DateTime Time);

    private class AlphaProducerStrategy : IStrategy
    {
        public string Name => "AlphaProducer";
        public int Lookback => 20;

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            var symbols = context.Timeseries.Metadata.Symbols;
            for (int i = 0; i < symbols.Count; i++)
            {
                double close = context.GetClose(i);
                if (double.IsNaN(close) || close <= 0.0) continue;

                double score = Math.Sin(context.CurrentIndex + i);
                var signal = new AlphaScoreSignal(symbols[i], score, context.CurrentTime);
                context.Market.SetState(signal, $"alpha.{symbols[i]}");

                if (score > 0.5)
                {
                    context.RecordSignal(symbols[i], score, "AlphaScore", "Bullish");
                }
            }
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    private class ExecutionStrategy : IStrategy
    {
        public string Name => "ExecutionStrategy";
        public int Lookback => 5;
        public int OrdersPlaced { get; private set; }

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            var symbols = context.Timeseries.Metadata.Symbols;
            for (int i = 0; i < symbols.Count; i++)
            {
                string sym = symbols[i];
                if (context.Market.TryGetState<AlphaScoreSignal>(out var signal, out _, $"alpha.{sym}") && signal != null)
                {
                    if (signal.Score > 0.8)
                    {
                        double close = context.GetClose(i);
                        if (!double.IsNaN(close) && close > 0)
                        {
                            var orderId = context.Broker.SubmitOrder(new OrderRequest(sym, OrderDirection.Buy, close, 500m));
                            if (!string.IsNullOrEmpty(orderId)) OrdersPlaced++;
                        }
                    }
                }
            }
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void BlackboardDecoupledPipeline_AlphaProducerAndExecution_EndToEnd()
    {
        var dataConfig = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = new List<TimeseriesConfig>
            {
                new()
                {
                    TableId = "ashare.kline.1d.raw.baostock",
                    Fields = new List<string> { "close", "volume" },
                    Cache = false
                }
            }
        };

        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 7, 31);
        var (timeseries, events, fields) = DataLayerFactory.Create(dataConfig, startDate, endDate);

        try
        {
            var brokerConfig = new BrokerConfig { InitialCash = 1000000.0, FeeRate = 0.0002 };
            var simExchange = new SimulatedExchangeGateway(brokerConfig);
            var broker = new Broker(simExchange, brokerConfig);

            var alpha = new AlphaProducerStrategy();
            var exec = new ExecutionStrategy();
            var pipeline = StrategyPipeline.Sequential().Add(alpha).Add(exec);

            var engineConfig = new EngineConfig { Mode = "backtest", StartDate = startDate, EndDate = endDate };
            using var engine = new BacktestingEngine(
                timeseries,
                broker,
                pipeline,
                events: events,
                fields: fields,
                config: engineConfig,
                simExchange: simExchange);
            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            var result = engine.GetResult();
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Signals.Count > 0, "回测应该沉淀出 Alpha 信号记录");
            Assert.IsTrue(exec.OrdersPlaced > 0, "执行策略应该通过黑板消费信号并下单");

            var analysisConfig = new AnalysisConfig();
            var analysisEngine = AnalysisEngineFactory.Create(analysisConfig);
            var report = analysisEngine.Analyze(result, analysisConfig, timeseries);

            Assert.IsNotNull(report);
            Assert.IsNotNull(report.Signals, "SignalStatistics 应该有效产出");
            Assert.IsTrue(report.Signals.TotalSignals > 0, "信号分析器应分析实际记录的信号");
        }
        finally
        {
            (timeseries as IDisposable)?.Dispose();
        }
    }

    #endregion

    #region 2. 静态维表倒排选股与异构事件流驱动

    private class EventAndFieldStrategy : IStrategy
    {
        public string Name => "EventAndFieldStrategy";
        public int Lookback => 20;
        public int EventsReceived { get; private set; }
        public List<string> SelectedSymbols { get; } = new();

        public void OnStart(IEngineContext context)
        {
            if (context.Fields.HasTable("concept"))
            {
                var table = context.Fields["concept"];
                foreach (var sym in table.Keys)
                {
                    SelectedSymbols.Add(sym);
                    if (SelectedSymbols.Count >= 5) break;
                }
            }
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events)
        {
            if (events.Count > 0)
            {
                EventsReceived += events.Count;
            }
        }

        public void OnUpdate(IEngineContext context) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void EventDrivenAndFieldScreening_EndToEnd()
    {
        var dataConfig = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = new List<TimeseriesConfig>
            {
                new()
                {
                    TableId = "ashare.kline.1d.raw.baostock",
                    Fields = new List<string> { "close" },
                    Cache = false
                }
            },
            Events = new List<EventStreamConfig>
            {
                new()
                {
                    Name = "adjustments",
                    TableId = "ashare.adj_factor.baostock"
                }
            },
            Fields = new List<FieldTableConfig>
            {
                new()
                {
                    Name = "concept",
                    TableId = "ashare.concept.eastmoney"
                }
            }
        };

        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 7, 31);
        var (timeseries, events, fields) = DataLayerFactory.Create(dataConfig, startDate, endDate);

        try
        {
            var brokerConfig = new BrokerConfig { InitialCash = 1000000.0 };
            var simExchange = new SimulatedExchangeGateway(brokerConfig);
            var broker = new Broker(simExchange, brokerConfig);

            var strategy = new EventAndFieldStrategy();
            var pipeline = StrategyPipeline.Sequential().Add(strategy);

            var engineConfig = new EngineConfig { Mode = "backtest", StartDate = startDate, EndDate = endDate };
            using var engine = new BacktestingEngine(
                timeseries,
                broker,
                pipeline,
                events: events,
                fields: fields,
                config: engineConfig,
                simExchange: simExchange);
            engine.Run();

            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            Assert.IsTrue(strategy.SelectedSymbols.Count > 0, "策略应通过静态维表成功圈选监控股票池");
            Assert.IsTrue(strategy.EventsReceived > 0, "策略应成功接收到回测区间推送的除权除息等异构事件");
        }
        finally
        {
            (timeseries as IDisposable)?.Dispose();
        }
    }

    #endregion

    #region 3. Roslyn 外部 C# 源码动态编译端到端

    [TestMethod]
    public void RoslynDynamicSourceStrategy_FromYamlConfig_EndToEnd()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "CarrotQuant_RoslynE2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string strategyCodePath = Path.Combine(tempDir, "DemoDynamicStrategy.cs");
            File.WriteAllText(strategyCodePath, """
            using System;
            using System.Collections.Generic;
            using CarrotBacktesting.NET.Abstraction.Data;
            using CarrotBacktesting.NET.Abstraction.Engine;
            using CarrotBacktesting.NET.Abstraction.Strategy;

            namespace CustomUserStrategies
            {
                public class DynamicMomentumStrategy : IStrategy
                {
                    public string Name => "DynamicMomentum";
                    public int Lookback => 10;
                    private bool _ordered = false;

                    public void OnStart(IEngineContext context) { }
                    public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
                    public void OnStop(IEngineContext context) { }

                    public void OnUpdate(IEngineContext context)
                    {
                        if (!_ordered && context.Timeseries.Metadata.Symbols.Count > 0)
                        {
                            string symbol = context.Timeseries.Metadata.Symbols[0];
                            double close = context.GetClose(0);
                            if (close > 0.0 && !double.IsNaN(close))
                            {
                                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, close, 1000m));
                                _ordered = true;
                            }
                        }
                    }
                }
            }
            """);

            string yamlPath = Path.Combine(tempDir, "session.yaml");
            string strategyNormalizedPath = strategyCodePath.Replace("\\", "/");
            string csvRootNormalized = _csvRoot.Replace("\\", "/");

            File.WriteAllText(yamlPath, $"""
            engine:
              mode: backtest
              start_date: "2024-01-02 00:00:00"
              end_date: "2024-01-31 00:00:00"
            broker:
              initial_cash: 500000.0
              fee_rate: 0.0003
            data:
              root: "{csvRootNormalized}"
              timeseries:
                - table_id: ashare.kline.1d.raw.baostock
                  fields:
                    - close
                    - volume
                  cache: false
            pipeline:
              mode: sequential
              pipeline:
                - source: "{strategyNormalizedPath}"
            analysis:
              analyzers:
                - type: TradeAnalyzer
            """);

            var report = BacktestSession.Run(yamlPath, export: false);

            Assert.IsNotNull(report);
            Assert.IsNotNull(report.Facts);
            Assert.IsTrue(report.Facts.DailySnapshots.Count > 0);
            Assert.IsTrue(report.Facts.Settlements.Count > 0, "Roslyn 动态编译策略应成功执行撮合并产生交割单");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    #endregion

    #region 4. 多标的横向截面多因子排序与组合再平衡调仓

    /// <summary>
    /// 截面动量再平衡策略：
    /// 每个时间步横向比较所有标的的当期收益率（动量得分）；
    /// 选出最高分的 Top-1 标的；若当前持有非 Top-1 标的，则清仓旧标的并买入新标的。
    /// </summary>
    private class CrossSectionalMomentumStrategy : IStrategy
    {
        public string Name => "CrossSectionalMomentumStrategy";
        public int Lookback => 5;
        public int RebalanceCount { get; private set; }
        private string _currentlyHeld = "";

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            var symbols = context.Timeseries.Metadata.Symbols;
            if (symbols.Count == 0) return;

            string bestSymbol = "";
            double highestScore = double.MinValue;

            for (int i = 0; i < symbols.Count; i++)
            {
                double close = context.GetClose(i);
                if (double.IsNaN(close) || close <= 0.0) continue;

                // 简单的截面动量打分：随时间和标的索引变动
                double score = Math.Cos(context.CurrentIndex * 0.5 + i);
                if (score > highestScore)
                {
                    highestScore = score;
                    bestSymbol = symbols[i];
                }
            }

            if (!string.IsNullOrEmpty(bestSymbol) && bestSymbol != _currentlyHeld)
            {
                // 先卖出旧标的（若已有持仓）
                if (!string.IsNullOrEmpty(_currentlyHeld))
                {
                    decimal pos = context.GetAvailablePosition(_currentlyHeld);
                    if (pos > 0m)
                    {
                        double oldClose = context.GetClose(_currentlyHeld);
                        context.Broker.SubmitOrder(new OrderRequest(_currentlyHeld, OrderDirection.Sell, oldClose, pos));
                    }
                }

                // 买入新的最佳标的
                double newClose = context.GetClose(bestSymbol);
                if (!double.IsNaN(newClose) && newClose > 0.0)
                {
                    var orderId = context.Broker.SubmitOrder(new OrderRequest(bestSymbol, OrderDirection.Buy, newClose, 1000m));
                    if (!string.IsNullOrEmpty(orderId))
                    {
                        _currentlyHeld = bestSymbol;
                        RebalanceCount++;
                    }
                }
            }
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void CrossSectionalRankingAndRebalancing_EndToEnd()
    {
        // 构造包含 3 只股票的确定性时序数据
        var symbols = new[] { "SH.600000", "SZ.000001", "SZ.000002" };
        var dates = new[]
        {
            new DateTime(2024, 1, 2),
            new DateTime(2024, 1, 3),
            new DateTime(2024, 1, 4),
            new DateTime(2024, 1, 5)
        };

        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        // 3 只标的在 4 天内的价格走势
        var closeData = new double[,]
        {
            { 10.0, 20.0, 15.0 },
            { 10.5, 19.5, 15.8 },
            { 11.0, 21.0, 15.2 },
            { 10.8, 20.5, 16.0 }
        };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        var brokerConfig = new BrokerConfig { InitialCash = 1000000.0, FeeRate = 0.0003 };
        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new CrossSectionalMomentumStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        var result = engine.GetResult();
        Assert.IsNotNull(result);
        Assert.IsTrue(strategy.RebalanceCount >= 1, "截面再平衡策略应至少执行一次调仓换股");
        Assert.IsTrue(result.Settlements.Count >= 1, "回测主循环应沉淀调仓产生的成交记录");
    }

    #endregion

    #region 5. 多核并发策略管线（ParallelPipeline）与黑板线程安全

    private class ConcurrentAlphaWorkerStrategy : IStrategy
    {
        public string Name { get; }
        public int Lookback => 5;
        public int ExecutedSteps { get; private set; }

        public ConcurrentAlphaWorkerStrategy(string name)
        {
            Name = name;
        }

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            // 向全局黑板并发读写状态，验证线程安全性
            string key = $"{Name}.Step_{context.CurrentIndex}";
            context.Market.SetState(context.CurrentIndex * 100, key);

            bool hasState = context.Market.HasState<int>(key);
            if (hasState)
            {
                ExecutedSteps++;
            }
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void ParallelPipeline_ConcurrentExecution_BlackboardThreadSafety_EndToEnd()
    {
        var symbols = new[] { "SH.600000" };
        var dates = new[]
        {
            new DateTime(2024, 1, 2),
            new DateTime(2024, 1, 3),
            new DateTime(2024, 1, 4),
            new DateTime(2024, 1, 5),
            new DateTime(2024, 1, 8)
        };
        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);
        tsRegistry.SetMatrix("close", new MockMatrix(new double[,] { { 10.0 }, { 10.0 }, { 10.0 }, { 10.0 }, { 10.0 } }));

        var brokerConfig = new BrokerConfig { InitialCash = 1000000.0 };
        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        // 创建 4 个并发 Worker，开启多核并行加速 (Concurrent = true)
        var workers = new[]
        {
            new ConcurrentAlphaWorkerStrategy("WorkerA"),
            new ConcurrentAlphaWorkerStrategy("WorkerB"),
            new ConcurrentAlphaWorkerStrategy("WorkerC"),
            new ConcurrentAlphaWorkerStrategy("WorkerD")
        };

        var parallelPipeline = new ParallelPipeline { Concurrent = true };
        foreach (var w in workers)
        {
            parallelPipeline.Add(w);
        }

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            parallelPipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        foreach (var worker in workers)
        {
            Assert.AreEqual(dates.Length, worker.ExecutedSteps, $"{worker.Name} 并发执行步数必须与总交易日数量完全一致");
        }
    }

    #endregion

    #region 6. 异常行情鲁棒性：新股上市前与中途停牌断档（NaN 哨兵容错）

    private class NaNAnomalyHandlingStrategy : IStrategy
    {
        public string Name => "NaNAnomalyHandlingStrategy";
        public int Lookback => 5;
        public int BuyOrdersPlaced { get; private set; }
        public int NaNSkippedCount { get; private set; }

        public void OnStart(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            var symbols = context.Timeseries.Metadata.Symbols;
            for (int i = 0; i < symbols.Count; i++)
            {
                double close = context.GetClose(i);
                if (double.IsNaN(close) || close <= 0.0)
                {
                    NaNSkippedCount++;
                    continue; // 遇到未上市或停牌股票，安全跳过
                }

                // 遇到复牌有效股票且尚未持有，尝试买入
                if (context.GetPosition(symbols[i]) == 0m)
                {
                    var orderId = context.Broker.SubmitOrder(new OrderRequest(symbols[i], OrderDirection.Buy, close, 500m));
                    if (!string.IsNullOrEmpty(orderId))
                    {
                        BuyOrdersPlaced++;
                    }
                }
            }
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
        public void OnStop(IEngineContext context) { }
    }

    [TestMethod]
    public void MarketDataAnomaly_SuspensionAndIPO_NaNHandling_EndToEnd()
    {
        var symbols = new[] { "NORMAL.01", "SUSPEND.02" };
        var dates = new[]
        {
            new DateTime(2024, 1, 2), // Day 0: SUSPEND 为 NaN (新股未上市)
            new DateTime(2024, 1, 3), // Day 1: SUSPEND 为 NaN (中途停牌)
            new DateTime(2024, 1, 4), // Day 2: SUSPEND 复牌为 10.0
            new DateTime(2024, 1, 5)  // Day 3: SUSPEND 为 11.0
        };

        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[,]
        {
            { 10.0, double.NaN },
            { 10.5, double.NaN },
            { 11.0, 10.0 },
            { 11.5, 11.0 }
        };
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));

        var brokerConfig = new BrokerConfig { InitialCash = 1000000.0, FeeRate = 0.0003 };
        var simExchange = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(simExchange, brokerConfig);

        var strategy = new NaNAnomalyHandlingStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var engineConfig = new EngineConfig
        {
            Mode = "backtest",
            StartDate = dates[0],
            EndDate = dates[^1]
        };

        using var engine = new BacktestingEngine(
            tsRegistry,
            broker,
            pipeline,
            config: engineConfig,
            simExchange: simExchange);

        engine.Run();

        Assert.AreEqual(EngineStatus.Completed, engine.Status);
        Assert.IsTrue(strategy.NaNSkippedCount >= 2, "策略应准确识别并安全跳过停牌和未上市期间的 NaN 哨兵");
        Assert.IsTrue(strategy.BuyOrdersPlaced >= 2, "正常股票与复牌股票均应成功触发下单");
        var result = engine.GetResult();
        Assert.IsNotNull(result);
        Assert.IsTrue(result.DailySnapshots.Count == dates.Length, "存在停牌标的时，每日资产快照仍应连续平滑产生");
        Assert.IsTrue(broker.GetPosition("SUSPEND.02") > 0m, "复牌标的在复牌日后应成功建仓持有");
    }

    #endregion
}
