using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
using CarrotBacktesting.NET.Indicators;
using CarrotBacktesting.NET.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Integration;

/// <summary>
/// 全系统跨模块端到端集成测试（End-to-End Full Pipeline Integration Tests）。
/// 真实打通：物理存储 -> 数据层矩阵 -> 流式指标计算 -> 规则策略管线 -> 经纪商撮合与两阶段账本 -> 事实记账器 -> 绩效归因引擎 -> 报表导出。
/// </summary>
[TestClass]
public class EndToEndBacktestIntegrationTests
{
    private string _csvRoot = "";
    private string _parquetRoot = "";

    [TestInitialize]
    public void Setup()
    {
        _csvRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
        _parquetRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "parquet");
    }

    /// <summary>
    /// 双均线交叉动量交易策略，在 OnStart 中声明指标池，在 OnUpdate 中进行信号决策与下单。
    /// </summary>
    private class DualMovingAverageStrategy : RuleStrategyBase
    {
        public override string Name => "DualMAStrategy";
        public override int Lookback => 20;

        private IndicatorPool<Sma>? _smaFastPool;
        private IndicatorPool<Sma>? _smaSlowPool;
        public int TotalTradesGenerated { get; private set; }

        public override void OnStart(IEngineContext context)
        {
            base.OnStart(context);
            // 声明 5 日与 10 日 SMA 指标池
            _smaFastPool = context.Indicators.GetOrAdd<Sma>("SMA_5", () => new Sma(5));
            _smaSlowPool = context.Indicators.GetOrAdd<Sma>("SMA_10", () => new Sma(10));
        }

        protected override IEnumerable<string> GetSymbols(IEngineContext context)
        {
            return context.Timeseries.Metadata.Symbols;
        }

        protected override void CheckEntry(IEngineContext context, string symbol)
        {
            if (_smaFastPool == null || _smaSlowPool == null) return;

            int symIdx = context.Timeseries.Metadata.GetSymbolIndex(symbol);
            if (symIdx < 0) return;

            double fast = _smaFastPool[symIdx].Current;
            double slow = _smaSlowPool[symIdx].Current;

            if (double.IsNaN(fast) || double.IsNaN(slow)) return;

            // 金叉：快线上穿慢线且当前无持仓，买入 1000 股
            if (fast > slow)
            {
                double close = context.GetClose(symIdx);
                if (!double.IsNaN(close) && close > 0)
                {
                    var req = new OrderRequest(symbol, OrderDirection.Buy, close, 1000m);
                    var orderId = context.Broker.SubmitOrder(req);
                    if (!string.IsNullOrEmpty(orderId))
                    {
                        TotalTradesGenerated++;
                    }
                }
            }
        }

        protected override void CheckExit(IEngineContext context, string symbol, decimal position)
        {
            if (_smaFastPool == null || _smaSlowPool == null) return;

            int symIdx = context.Timeseries.Metadata.GetSymbolIndex(symbol);
            if (symIdx < 0) return;

            double fast = _smaFastPool[symIdx].Current;
            double slow = _smaSlowPool[symIdx].Current;

            if (double.IsNaN(fast) || double.IsNaN(slow)) return;

            // 死叉：快线下穿慢线且有持仓，清仓卖出
            if (fast < slow && position > 0m)
            {
                double close = context.GetClose(symIdx);
                if (!double.IsNaN(close) && close > 0)
                {
                    var req = new OrderRequest(symbol, OrderDirection.Sell, close, position);
                    var orderId = context.Broker.SubmitOrder(req);
                    if (!string.IsNullOrEmpty(orderId))
                    {
                        TotalTradesGenerated++;
                    }
                }
            }
        }
    }

    [TestMethod]
    public void FullPipeline_FromCsvStorage_ToAnalysisReport_EndToEnd()
    {
        // 1. 数据层装配
        var dataConfig = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = new List<TimeseriesConfig>
            {
                new()
                {
                    TableId = "ashare.kline.1d.raw.baostock",
                    Fields = new List<string> { "open", "high", "low", "close", "volume" },
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
        var endDate = new DateTime(2023, 6, 30);
        var (timeseries, events, fields) = DataLayerFactory.Create(dataConfig, startDate, endDate);

        try
        {
            Assert.IsTrue(timeseries.Metadata.Symbols.Count > 0, "标的代码池不应为空");
            Assert.IsTrue(timeseries.Metadata.TradeDates.Count > 0, "交易日网格不应为空");

            // 2. 经纪商与交易所装配
            var brokerConfig = new BrokerConfig
            {
                InitialCash = 1000000.0,
                FeeRate = 0.0003,
                Slippage = 0.0001
            };
            var simExchange = new SimulatedExchangeGateway(brokerConfig);
            var broker = new Broker(simExchange, brokerConfig);

            // 3. 策略管线装配
            var strategy = new DualMovingAverageStrategy();
            var pipeline = StrategyPipeline.Sequential().Add(strategy);

            // 4. 引擎装配与运行
            var engineConfig = new EngineConfig
            {
                Mode = "backtest",
                StartDate = startDate,
                EndDate = endDate
            };
            using var engine = new BacktestingEngine(
                timeseries,
                broker,
                pipeline,
                events: events,
                fields: fields,
                config: engineConfig,
                simExchange: simExchange);

            engine.Run();

            // 5. 校验运行结果
            Assert.AreEqual(EngineStatus.Completed, engine.Status);
            var result = engine.GetResult();
            Assert.IsNotNull(result);
            Assert.IsTrue(result.DailySnapshots.Count > 0, "回测应该产生每日快照记录");
            Assert.IsTrue(result.EquityCurve.Count > 0, "回测应该产生时间步净值曲线");
            Assert.IsTrue(strategy.TotalTradesGenerated > 0, "策略应在回测期间产生交易信号并提交订单");
            Assert.IsTrue(result.Settlements.Count > 0, "仿真撮合应该产生实际交割单记录");

            // 6. 分析引擎调度与多分析器验证
            var analysisConfig = new AnalysisConfig();
            var analysisEngine = AnalysisEngineFactory.Create(analysisConfig);
            var report = analysisEngine.Analyze(result, analysisConfig, timeseries);

            Assert.IsNotNull(report);
            Assert.IsNotNull(report.Portfolio, "PortfolioStatistics 必须有效产出");
            Assert.IsNotNull(report.Trades, "TradeStatistics 必须有效产出");
            Assert.IsNotNull(report.Cost, "CostStatistics 必须有效产出");

            // 验证关键指标真实业务断言
            Assert.IsTrue(report.Trades.TotalTrades > 0, "交易统计应包含完成的交易笔数");
            Assert.IsTrue(report.Cost.TotalCommission > 0.0, "实际成交应发生手续费损耗扣除");
            Assert.IsTrue(report.Portfolio.MaxDrawdown >= 0.0 && report.Portfolio.MaxDrawdown <= 1.0);
        }
        finally
        {
            (timeseries as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public async Task FullPipeline_FromParquetStorage_WithAsyncSession_EndToEnd()
    {
        // 验证基于 Parquet 数据源与 BacktestSession.RunAndAnalyzeAsync 的端到端异步集成
        var parquetRoot = _parquetRoot.Replace("\\", "/");
        string tempYaml = Path.Combine(Path.GetTempPath(), "e2e_session_" + Guid.NewGuid().ToString("N") + ".yaml");

        try
        {
            File.WriteAllText(tempYaml, $"""
            engine:
              mode: backtest
              start_date: "2024-01-02 00:00:00"
              end_date: "2024-03-29 00:00:00"
            broker:
              initial_cash: 500000.0
              fee_rate: 0.0003
              slippage: 0.0001
            data:
              root: "{parquetRoot}"
              timeseries:
                - table_id: ashare.kline.1d.raw.baostock
                  fields:
                    - open
                    - high
                    - low
                    - close
                    - volume
                  cache: false
            analysis:
              analyzers:
                - type: PortfolioAnalyzer
                - type: TradeAnalyzer
                - type: CostAnalyzer
            """);

            var strategy = new DualMovingAverageStrategy();
            using var session = BacktestSession.Load(tempYaml, strategy);
            Assert.IsNotNull(session);

            // 异步执行全流程
            var report = await session.RunAndAnalyzeAsync(export: false);

            Assert.IsNotNull(report);
            Assert.IsNotNull(session.Result);
            Assert.AreEqual(EngineStatus.Completed, session.Engine.Status);
            Assert.AreEqual(500000.0, session.Config.Broker.InitialCash);
            Assert.IsTrue(session.Result.DailySnapshots.Count > 0);
            Assert.IsTrue(session.Result.EquityCurve.Count > 0);
            Assert.IsTrue(strategy.TotalTradesGenerated > 0, "基于 Parquet 运行的策略应产生交易意图");
            Assert.IsTrue(session.Result.Settlements.Count > 0, "回测应产生交割记录");
            Assert.IsNotNull(report.Portfolio);
            Assert.IsNotNull(report.Trades);
            Assert.IsNotNull(report.Cost);
            Assert.IsTrue(report.Trades.TotalTrades > 0, "交易统计应包含交易完成记录");
            Assert.IsTrue(report.Cost.TotalCommission > 0.0, "手续费应被正确计提");
        }
        finally
        {
            if (File.Exists(tempYaml))
            {
                File.Delete(tempYaml);
            }
        }
    }

    #region 高级使用路径端到端集成：黑板解耦、事件/维表驱动与 Roslyn 动态源码策略

    public record AlphaScoreState(double Score, string Symbol);

    private class AlphaProducerStrategy : IStrategy
    {
        public string Name => "AlphaProducer";
        public int Lookback => 20;

        public void OnUpdate(IEngineContext context)
        {
            foreach (var symbol in context.Timeseries.Metadata.Symbols)
            {
                double close = context.GetClose(symbol);
                if (!double.IsNaN(close) && close > 0)
                {
                    // 模拟生成 Alpha 信号评分
                    double score = close > 10.0 ? 0.8 : 0.2;
                    context.Market.SetState(new AlphaScoreState(score, symbol), $"Score_{symbol}");
                    context.RecordSignal(symbol, score, "momentum_alpha", "close_threshold");
                }
            }
        }
    }

    private class ExecutionStrategy : IStrategy
    {
        public string Name => "ExecutionStrategy";
        public int Lookback => 20;
        public int OrdersPlaced { get; private set; }

        public void OnUpdate(IEngineContext context)
        {
            foreach (var symbol in context.Timeseries.Metadata.Symbols)
            {
                var state = context.Market.GetState<AlphaScoreState>($"Score_{symbol}");
                if (state != null && state.Score >= 0.7)
                {
                    var pos = context.GetPosition(symbol);
                    if (pos == 0m)
                    {
                        double close = context.GetClose(symbol);
                        var req = new OrderRequest(symbol, OrderDirection.Buy, close, 500m);
                        if (!string.IsNullOrEmpty(context.Broker.SubmitOrder(req)))
                        {
                            OrdersPlaced++;
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void FullPipeline_BlackboardDecoupledPipeline_WithSignalAnalyzer_EndToEnd()
    {
        var dataConfig = new DataConfig
        {
            Root = _csvRoot,
            Timeseries = new List<TimeseriesConfig>
            {
                new()
                {
                    TableId = "ashare.kline.1d.raw.baostock",
                    Fields = new List<string> { "open", "close", "volume" },
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

            // 串行编排 Alpha 生产策略与仓位执行策略（黑板解耦模式）
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

            // 驱动包含 SignalAnalyzer 的分析引擎
            var analysisConfig = new AnalysisConfig();
            var analysisEngine = AnalysisEngineFactory.Create(analysisConfig);
            var report = analysisEngine.Analyze(result, analysisConfig, timeseries);

            Assert.IsNotNull(report.Signals, "SignalStatistics 应该有效产出");
            Assert.IsTrue(report.Signals.TotalSignals > 0, "信号分析器应分析实际记录的信号");
        }
        finally
        {
            (timeseries as IDisposable)?.Dispose();
        }
    }

    private class EventAndFieldStrategy : IStrategy
    {
        public string Name => "EventAndFieldStrategy";
        public int Lookback => 20;
        public int EventsReceived { get; private set; }
        public List<string> SelectedSymbols { get; } = new();

        public void OnStart(IEngineContext context)
        {
            // 通过静态维表 Keys 动态圈选监控池
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
    }

    [TestMethod]
    public void FullPipeline_EventDrivenAndFieldScreening_EndToEnd()
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
            },
            Events = new List<EventStreamConfig>
            {
                new()
                {
                    Name = "adj_factor",
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
            Assert.IsTrue(strategy.SelectedSymbols.Count > 0, "策略应通过 Fields 维表成功圈选标的池");
            Assert.IsTrue(strategy.EventsReceived > 0, "策略应在回测循环中通过 OnEvent 成功接收异构离散事件流");
        }
        finally
        {
            (timeseries as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public void FullPipeline_RoslynSourceStrategy_FromYamlConfig_EndToEnd()
    {
        var csvRoot = _csvRoot.Replace("\\", "/");
        string tempDir = Path.Combine(Path.GetTempPath(), "roslyn_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // 写入独立的动态 C# 策略源码文件
            string strategyCodePath = Path.Combine(tempDir, "DynamicTestStrategy.cs");
            File.WriteAllText(strategyCodePath, """
            using System;
            using CarrotBacktesting.NET.Abstraction.Engine;
            using CarrotBacktesting.NET.Abstraction.Strategy;

            public class DynamicTestStrategy : IStrategy
            {
                public string Name => "DynamicRoslynStrategy";
                public int Lookback => 1;
                public int Counter { get; private set; }

                public void OnUpdate(IEngineContext context)
                {
                    Counter++;
                    var symbols = context.Timeseries.Metadata.Symbols;
                    if (symbols.Count > 0)
                    {
                        var symbol = symbols[0];
                        double close = context.GetClose(symbol);
                        if (!double.IsNaN(close) && close > 0 && context.GetPosition(symbol) == 0m)
                        {
                            context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, close, 1000m));
                        }
                    }
                }
            }
            """);

            // 写入引用该源码策略的 YAML 配置
            string yamlPath = Path.Combine(tempDir, "session.yaml");
            string strategyNormalizedPath = strategyCodePath.Replace("\\", "/");
            File.WriteAllText(yamlPath, $"""
            engine:
              mode: backtest
              start_date: "2024-01-02 00:00:00"
              end_date: "2024-01-31 00:00:00"
            broker:
              initial_cash: 500000.0
              fee_rate: 0.0003
            data:
              root: "{csvRoot}"
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

            // 直接通过静态 BacktestSession.Run 入口一键驱动
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
}
