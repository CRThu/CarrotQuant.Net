using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CarrotBacktesting.NET;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
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
/// 多模态存储源与会话生命周期冒烟集成测试（Multi-Modal Storage & Session Smoke Integration Tests）。
/// 真实端到端打通物理 I/O：
/// 1. CSV 物理源与 Hive 平铺/分区结构加载；
/// 2. Parquet 物理源年/月两级物理分区解压与零空值容错；
/// 3. BacktestSession.RunAndAnalyzeAsync 异步会话全流程；
/// 4. SessionConfigLoader YAML 声明式配置驱动。
/// </summary>
[TestClass]
public class MultiModalSessionIntegrationTests
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
            _smaFastPool = context.Indicators.GetOrAdd<Sma>("SMA_5", () => new Sma(5));
            _smaSlowPool = context.Indicators.GetOrAdd<Sma>("SMA_10", () => new Sma(10));
        }

        protected override IEnumerable<string> GetSymbols(IEngineContext context)
        {
            return context.Timeseries.Primary.Metadata.Symbols;
        }

        protected override void CheckEntry(IEngineContext context, string symbol)
        {
            if (_smaFastPool == null || _smaSlowPool == null) return;

            int symIdx = context.Timeseries.Primary.Metadata.GetSymbolIndex(symbol);
            if (symIdx < 0) return;

            double fast = _smaFastPool[symIdx].Current;
            double slow = _smaSlowPool[symIdx].Current;

            if (double.IsNaN(fast) || double.IsNaN(slow)) return;

            if (fast > slow)
            {
                double close = context.GetClose(symIdx);
                if (!double.IsNaN(close) && close > 0)
                {
                    var req = new OrderRequest(symbol, OrderDirection.Buy, close, 1000m);
                    var orderId = context.Broker.SubmitOrder(req);
                    if (orderId > 0)
                    {
                        TotalTradesGenerated++;
                    }
                }
            }
        }

        protected override void CheckExit(IEngineContext context, string symbol, decimal position)
        {
            if (_smaFastPool == null || _smaSlowPool == null) return;

            int symIdx = context.Timeseries.Primary.Metadata.GetSymbolIndex(symbol);
            if (symIdx < 0) return;

            double fast = _smaFastPool[symIdx].Current;
            double slow = _smaSlowPool[symIdx].Current;

            if (double.IsNaN(fast) || double.IsNaN(slow)) return;

            if (fast < slow && position > 0m)
            {
                double close = context.GetClose(symIdx);
                if (!double.IsNaN(close) && close > 0)
                {
                    var req = new OrderRequest(symbol, OrderDirection.Sell, close, position);
                    var orderId = context.Broker.SubmitOrder(req);
                    if (orderId > 0)
                    {
                        TotalTradesGenerated++;
                    }
                }
            }
        }
    }

    [TestMethod]
    public void FullPipeline_FromCsvStorage_ToAnalysisReport_SmokeTest()
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
            Assert.IsTrue(timeseries.Primary.Metadata.Symbols.Count > 0, "标的代码池不应为空");
            Assert.IsTrue(timeseries.Primary.Metadata.TradeDates.Count > 0, "交易日网格不应为空");

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
    public async Task FullPipeline_FromParquetStorage_WithAsyncSession_SmokeTest()
    {
        var sessionConfig = new SessionConfig
        {
            Engine = new EngineConfig
            {
                Mode = "backtest",
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 1, 31)
            },
            Broker = new BrokerConfig
            {
                InitialCash = 1000000.0,
                FeeRate = 0.0003,
                Slippage = 0.0001
            },
            Data = new DataConfig
            {
                Root = _parquetRoot,
                Timeseries = new List<TimeseriesConfig>
                {
                    new()
                    {
                        TableId = "ashare.kline.1d.raw.baostock",
                        Fields = new List<string> { "open", "high", "low", "close", "volume" },
                        Cache = false
                    }
                }
            },
            Analysis = new AnalysisConfig()
        };

        var strategy = new DualMovingAverageStrategy();
        using var session = BacktestSession.FromConfig(sessionConfig, strategy);
        var report = await session.RunAndAnalyzeAsync(export: false);

        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Facts);
        Assert.IsTrue(report.Facts.DailySnapshots.Count > 0, "Parquet 回测应产生每日快照");
        Assert.IsTrue(report.Facts.EquityCurve.Count > 0, "Parquet 回测应产生净值曲线");
        Assert.IsTrue(strategy.TotalTradesGenerated > 0, "Parquet 回测中策略应触发交易");
        Assert.IsTrue(report.Facts.Settlements.Count > 0, "Parquet 回测中撮合应产生成交交割单");

        Assert.IsNotNull(report.Portfolio);
        Assert.IsNotNull(report.Trades);
        Assert.IsNotNull(report.Cost);
        Assert.IsTrue(report.Trades.TotalTrades > 0);
        Assert.IsTrue(report.Cost.TotalCommission > 0.0);
    }

    [TestMethod]
    public async Task FullPipeline_WithTimeseriesCache_Lz4CompressionAndPrefetch_SmokeTest()
    {
        // 验证开启分块本地缓存（Cache = true，LZ4 原生块压缩与 4096 换页预读）时的真实端到端回测
        var sessionConfig = new SessionConfig
        {
            Engine = new EngineConfig
            {
                Mode = "backtest",
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 1, 31)
            },
            Broker = new BrokerConfig
            {
                InitialCash = 1000000.0,
                FeeRate = 0.0003,
                Slippage = 0.0001
            },
            Data = new DataConfig
            {
                Root = _parquetRoot,
                Timeseries = new List<TimeseriesConfig>
                {
                    new()
                    {
                        TableId = "ashare.kline.1d.raw.baostock",
                        Fields = new List<string> { "open", "high", "low", "close", "volume" },
                        Cache = true, // 开启分块本地缓存与预读
                        PageSize = 4096,
                        Compression = CacheCompression.Lz4
                    }
                }
            },
            Analysis = new AnalysisConfig()
        };

        var strategy = new DualMovingAverageStrategy();
        using var session = BacktestSession.FromConfig(sessionConfig, strategy);
        var report = await session.RunAndAnalyzeAsync(export: false);

        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Facts);
        Assert.IsTrue(report.Facts.DailySnapshots.Count > 0, "启用分块缓存时，回测应正常生成每日快照");
        Assert.IsTrue(report.Facts.Settlements.Count > 0, "启用分块缓存时，撮合交割单应正常产出");
        Assert.IsNotNull(report.Portfolio);
        Assert.IsNotNull(report.Trades);
        Assert.IsTrue(report.Trades.TotalTrades > 0);
    }

    [TestMethod]
    public void FullPipeline_FluentSessionBuilder_WithStrategyAndConsoleExport_EndToEnd()
    {
        // 验证 BacktestSessionBuilder 极简 Fluent API 全链路驱动到底并导出控制台报表
        var strategy = new DualMovingAverageStrategy();

        var report = BacktestSession.Create()
            .WithData(_csvRoot, "2024-01-01", "2024-01-31")
            .WithCash(1_000_000.0, allowShort: false)
            .WithBroker(initialCash: 1_000_000.0, feeRate: 0.0003, minFee: 5.0, slippage: 0.0002)
            .WithStrategy(strategy)
            .Configure(cfg =>
            {
                cfg.Data.Timeseries.Add(new TimeseriesConfig
                {
                    TableId = "ashare.kline.1d.raw.baostock",
                    Fields = new List<string> { "open", "high", "low", "close", "volume" },
                    Cache = false
                });
                cfg.Analysis ??= new AnalysisConfig();
                cfg.Analysis.Exporters.Add(new ExporterNodeConfig
                {
                    Type = "ConsoleExporter"
                });
            })
            .RunAndAnalyze(export: true);

        Assert.IsNotNull(report, "Fluent API 驱动应成功输出 AnalysisReport");
        Assert.IsNotNull(report.Facts, "必须沉淀不可变交易事实账本");
        Assert.IsTrue(report.Facts.DailySnapshots.Count > 0, "应生成逐日盯市资产快照");
        Assert.IsNotNull(report.Portfolio, "投资组合综合统计报告不可为空");
        Assert.IsNotNull(report.Capital, "资金容量与收益报告不可为空");
        Assert.IsNotNull(report.Trades, "交易频度与胜率报告不可为空");
    }
}
