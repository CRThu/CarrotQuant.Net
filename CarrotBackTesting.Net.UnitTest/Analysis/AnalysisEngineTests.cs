using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Analysis.Analyzers;
using CarrotBacktesting.NET.Analysis.Exporters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class AnalysisEngineTests
{
    [TestMethod]
    public void Analyze_FullTradingResult_IntegratesAllFourLayersAndFormatsDashboard()
    {
        var t0 = new DateTime(2024, 1, 1);
        var dates = new List<DateTime>();
        for (int i = 0; i < 10; i++) dates.Add(t0.AddDays(i));

        var symbols = new[] { "sh.600000", "sh.000300" };
        var metadata = new MockMetadata(symbols, dates);

        // 股票价格逐步上升，基准指数平缓微涨
        var prices = new double[10, 2]
        {
            { 10.0, 3000.0 },
            { 11.0, 3010.0 },
            { 12.0, 3020.0 },
            { 11.5, 3015.0 },
            { 13.0, 3030.0 },
            { 14.0, 3040.0 },
            { 13.5, 3035.0 },
            { 15.0, 3050.0 },
            { 16.0, 3060.0 },
            { 17.0, 3070.0 }
        };

        var timeseries = new MockTimeseriesRegistry(metadata);
        timeseries.SetMatrix("close", new MockMatrix(prices));

        // 1. 构造净值曲线与快照
        var equityCurve = new List<EquityPoint>();
        var dailySnapshots = new List<DailySnapshot>();

        for (int i = 0; i < 10; i++)
        {
            double eq = 100000.0 + i * 5000.0;
            var pt = new EquityPoint(dates[i], eq, 50000.0, 0.0, 10000.0, eq - 50000.0, i * 2000.0);
            equityCurve.Add(pt);
            dailySnapshots.Add(new DailySnapshot(dates[i], eq, 50000.0, 0.0, 10000.0, eq - 50000.0, i * 2000.0, Array.Empty<PositionSnapshot>()));
        }

        // 2. 构造交割单与平仓单
        var settlements = new List<SettlementRecord>
        {
            new(dates[0], "sh.600000", "ord-1", OrderDirection.Buy, 10.0, 1000m, 10000.0, 5.0, 0.0, 90000.0, "TrendFollow"),
            new(dates[4], "sh.600000", "ord-2", OrderDirection.Sell, 13.0, 1000m, 13000.0, 6.5, 3000.0, 102993.5, "TrendFollow")
        };

        var closedTrades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, dates[0], 10.0, dates[4], 13.0, 1000m, 1.0, 3000.0, 0.30, 11.5, 2.0, 4, "TrendFollow", "TargetExit")
        };

        // 3. 构造策略信号打点
        var signals = new List<SignalRecord>
        {
            new(dates[0], "sh.600000", 1.0, "TrendSignals", "Breakout")
        };

        var tradingResult = new TradingResult(
            settlements: settlements,
            closedTrades: closedTrades,
            openPositions: Array.Empty<PositionSnapshot>(),
            equityCurve: equityCurve,
            dailySnapshots: dailySnapshots,
            signals: signals
        );

        var engine = new AnalysisEngine();
        var options = new AnalysisOptions
        {
            BenchmarkSymbol = "sh.000300",
            ExitTimingSteps = 4,
            SignalHorizonSteps = 4
        };

        // 4. 执行分析
        var report = engine.Analyze(tradingResult, options, timeseries);

        // 5. 逐层断言
        Assert.IsNotNull(report);
        Assert.AreEqual(tradingResult, report.Facts);

        // Level 1: 组合收益与基准
        Assert.IsNotNull(report.Portfolio);
        Assert.IsTrue(report.Portfolio.TotalReturn > 0.0);
        Assert.IsNotNull(report.Portfolio.BenchmarkReturn);
        Assert.IsNotNull(report.Portfolio.Beta);

        // Level 2: 资本利用
        Assert.IsNotNull(report.Capital);
        Assert.IsTrue(report.Capital.AveragePositionRatio > 0.0);
        Assert.IsTrue(report.Capital.CashDrag > 0.0);

        // Level 3: 交易归因
        Assert.IsNotNull(report.Trades);
        Assert.AreEqual(1, report.Trades.TotalTrades);
        Assert.AreEqual(1.0, report.Trades.WinRate);
        Assert.IsNotNull(report.Trades.ExitTiming);

        // Level 4: 摩擦成本
        Assert.IsNotNull(report.Cost);
        Assert.AreEqual(11.5, report.Cost.TotalCommission, 1e-4);
        Assert.AreEqual(2.0, report.Cost.TotalSlippage, 1e-4);

        // 信号评估轨 (V3 升级)
        Assert.IsNotNull(report.Signals);
        Assert.AreEqual(1, report.Signals.TotalSignals);

        // 6. 控制台报表导出器测试 (全模块渲染)
        var writer = new StringWriter();
        var testConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer)
        });

        var exporter = new ConsoleExporter(new ConsoleExporterOptions(), testConsole);
        exporter.Export(report);
        string dashboard = writer.ToString();

        Assert.IsFalse(string.IsNullOrWhiteSpace(dashboard));
        Assert.IsTrue(dashboard.Contains("CarrotQuant 量化回测与实盘绩效归因报告"));
        Assert.IsTrue(dashboard.Contains("[1] 组合净值与风险收益统计"));
        Assert.IsTrue(dashboard.Contains("[2] 资本利用与敞口分析"));
        Assert.IsTrue(dashboard.Contains("[3] 交易行为与执行归因"));
        Assert.IsTrue(dashboard.Contains("[4] 现实损耗与交易成本"));
        Assert.IsTrue(dashboard.Contains("[5] 独立 Alpha 信号前向收益评估"));

        // 7. 模块属性开关过滤测试 (仅导出组合与交易模块，关闭资本、成本与信号)
        var filteredWriter = new StringWriter();
        var filteredConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(filteredWriter)
        });
        var filteredExporter = new ConsoleExporter(new ConsoleExporterOptions
        {
            ShowCapital = false,
            ShowCost = false,
            ShowSignals = false
        }, filteredConsole);
        filteredExporter.Export(report);
        string filteredOutput = filteredWriter.ToString();

        Assert.IsTrue(filteredOutput.Contains("[1] 组合净值与风险收益统计"));
        Assert.IsTrue(filteredOutput.Contains("[3] 交易行为与执行归因"));
        Assert.IsFalse(filteredOutput.Contains("[2] 资本利用与敞口分析"));
        Assert.IsFalse(filteredOutput.Contains("[4] 现实损耗与交易成本"));
        Assert.IsFalse(filteredOutput.Contains("[5] 独立 Alpha 信号前向收益评估"));

        // 8. SummaryOnly 预设测试
        var summaryWriter = new StringWriter();
        var summaryConsole = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(summaryWriter) });
        var summaryExporter = new ConsoleExporter(ConsoleExporterOptions.SummaryOnly, summaryConsole);
        summaryExporter.Export(report);
        string summaryOutput = summaryWriter.ToString();
        Assert.IsTrue(summaryOutput.Contains("[1] 组合净值与风险收益统计"));
        Assert.IsFalse(summaryOutput.Contains("逐月收益率分布"));
        Assert.IsFalse(summaryOutput.Contains("卖点时机反思走势"));
    }

    [TestMethod]
    public void Export_WithSaveHtml_GeneratesHtmlFileOnDisk()
    {
        var t0 = new DateTime(2024, 1, 1);
        var equityCurve = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0),
            new(t0.AddDays(1), 105000.0, 105000.0, 0.0, 0.0, 0.0, 5000.0)
        };
        var result = new TradingResult
        {
            EquityCurve = equityCurve
        };
        var engine = new AnalysisEngine();
        var report = engine.Analyze(result);

        string tempHtml = Path.Combine(Path.GetTempPath(), $"test_report_{Guid.NewGuid():N}.html");
        try
        {
            var writer = new StringWriter();
            var testConsole = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
            var exporter = new ConsoleExporter(new ConsoleExporterOptions
            {
                SaveHtml = true,
                HtmlOutputPath = tempHtml
            }, testConsole);

            exporter.Export(report);

            Assert.IsTrue(File.Exists(tempHtml));
            string htmlContent = File.ReadAllText(tempHtml);
            Assert.IsTrue(htmlContent.Contains("<body") || htmlContent.Contains("<style>") || htmlContent.Contains("CarrotQuant"));
        }
        finally
        {
            if (File.Exists(tempHtml))
            {
                File.Delete(tempHtml);
            }
        }
    }

    [TestMethod]
    public void Analyze_UnknownBenchmarkSymbol_GracefullyLeavesBenchmarkMetricsNull()
    {
        var t0 = new DateTime(2024, 1, 1);
        var equityCurve = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0),
            new(t0.AddDays(1), 105000.0, 105000.0, 0.0, 0.0, 0.0, 5000.0)
        };

        var tradingResult = new TradingResult(
            settlements: Array.Empty<SettlementRecord>(),
            closedTrades: Array.Empty<Trade>(),
            openPositions: Array.Empty<PositionSnapshot>(),
            equityCurve: equityCurve,
            dailySnapshots: Array.Empty<DailySnapshot>(),
            signals: Array.Empty<SignalRecord>()
        );

        var engine = new AnalysisEngine();
        // 设置一个不存在的基准代码
        var options = new AnalysisOptions
        {
            BenchmarkSymbol = "non_existent_index"
        };

        var report = engine.Analyze(tradingResult, options, null);

        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Portfolio);
        Assert.AreEqual(0.05, report.Portfolio.TotalReturn, 1e-4);
        Assert.IsNull(report.Portfolio.BenchmarkReturn);
        Assert.IsNull(report.Portfolio.Alpha);
        Assert.IsNull(report.Portfolio.Beta);
        Assert.IsNull(report.Portfolio.InformationRatio);
    }

    private sealed record MockOmegaResult(double OmegaRatio, string Description) : IAnalysisResult
    {
        public string Name => "OmegaAnalyzer";
        public string Title => "自定义 Omega 评估";

        public IEnumerable<MetricItem> GetMetrics()
        {
            yield return new("Omega 比率 (Omega Ratio)", $"{OmegaRatio:F2}", "自定义比率");
            yield return new("指标说明 (Description)", Description, "元数据");
        }
    }

    private sealed class MockOmegaAnalyzer : AnalyzerBase<MockOmegaResult>
    {
        public override string Name => "OmegaAnalyzer";

        public override MockOmegaResult? Analyze(TradingResult facts, AnalysisOptions options, CarrotBacktesting.NET.Abstraction.Data.ITimeseriesRegistry? timeseries)
        {
            double totalPnl = 0;
            for (int i = 0; i < facts.ClosedTrades.Count; i++)
            {
                totalPnl += facts.ClosedTrades[i].RealizedPnL;
            }
            return new MockOmegaResult(totalPnl > 0 ? 1.85 : 0.85, "Custom Omega evaluation");
        }
    }

    [TestMethod]
    public void Pipeline_CustomAnalyzerRegistered_ExecutesAndPushesArtifactToReport()
    {
        var t0 = new DateTime(2024, 1, 1);
        var trades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, t0, 10.0, t0.AddDays(1), 12.0, 1000m, 1.0, 2000.0, 0.20, 5.0, 1.0, 1, "Tag1", "Tag1")
        };
        var result = new TradingResult(
            Array.Empty<SettlementRecord>(),
            trades,
            Array.Empty<PositionSnapshot>(),
            Array.Empty<EquityPoint>(),
            Array.Empty<DailySnapshot>(),
            Array.Empty<SignalRecord>()
        );

        var analyzer = new MockOmegaAnalyzer();
        var engine = new AnalysisEngine(Array.Empty<IAnalyzer>());
        engine.RegisterAnalyzer(analyzer);

        Assert.AreEqual(1, engine.Analyzers.Count);
        Assert.AreSame(analyzer, engine.Analyzers[0]);

        var report = engine.Analyze(result);
        Assert.IsNotNull(report);
        Assert.AreEqual(1, report.Results.Count);
        Assert.IsNotNull(report["OmegaAnalyzer"]);

        var resultObj = report.Get<MockOmegaResult>("OmegaAnalyzer");
        Assert.IsNotNull(resultObj);
        Assert.AreEqual(1.85, resultObj.OmegaRatio);
        Assert.AreEqual("Custom Omega evaluation", resultObj.Description);

        // 验证直接通过 Type 读取 (O(1))
        var resultByType = report.Get<MockOmegaResult>();
        Assert.AreSame(resultObj, resultByType);

        // 验证大小写不敏感匹配
        var artifactCaseInsensitive = report.Get<MockOmegaResult>("omegaanalyzer");
        Assert.IsNotNull(artifactCaseInsensitive);

        // 验证不存在的名称返回 null
        var nonExistent = report.Get<MockOmegaResult>("NonExistent");
        Assert.IsNull(nonExistent);

        // 验证 ConsoleExporter 可以输出自描述指标
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
        var exporter = new ConsoleExporter(new ConsoleExporterOptions(), console);
        exporter.Export(report);
        string output = writer.ToString();

        Assert.IsTrue(output.Contains("自定义 Omega 评估"));
        Assert.IsTrue(output.Contains("Omega 比率"));
        Assert.IsTrue(output.Contains("1.85"));
    }

    [TestMethod]
    public void Pipeline_DefaultEngine_ContainsFiveCoreAnalyzers()
    {
        var engine = new AnalysisEngine();
        Assert.AreEqual(5, engine.Analyzers.Count);
        Assert.IsTrue(engine.Analyzers.Any(a => a is PortfolioAnalyzer));
        Assert.IsTrue(engine.Analyzers.Any(a => a is CapitalAnalyzer));
        Assert.IsTrue(engine.Analyzers.Any(a => a is TradeAnalyzer));
        Assert.IsTrue(engine.Analyzers.Any(a => a is CostAnalyzer));
        Assert.IsTrue(engine.Analyzers.Any(a => a is SignalAnalyzer));
    }

    [TestMethod]
    public void Pipeline_RemoveAnalyzer_PrunesExecutionAndReportArtifact()
    {
        var t0 = new DateTime(2023, 1, 1);
        var trades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, t0, 10.0, t0.AddDays(1), 12.0, 1000m, 1.0, 2000.0, 0.20, 5.0, 1.0, 1, "Tag1", "Tag1")
        };
        var equity = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0),
            new(t0.AddDays(1), 102000.0, 100000.0, 0.0, 0.0, 2000.0, 2000.0)
        };
        var result = new TradingResult(
            Array.Empty<SettlementRecord>(),
            trades,
            Array.Empty<PositionSnapshot>(),
            equity,
            Array.Empty<DailySnapshot>(),
            Array.Empty<SignalRecord>()
        );

        var engine = new AnalysisEngine();
        engine.Remove<SignalAnalyzer>();

        Assert.AreEqual(4, engine.Analyzers.Count);
        Assert.IsFalse(engine.Analyzers.Any(a => a is SignalAnalyzer));

        var report = engine.Analyze(result);
        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Portfolio);
        Assert.IsNotNull(report.Trades);
        Assert.IsNull(report.Signals); // SignalAnalyzer 被移除，报告中信号工件为 null
    }

    [TestMethod]
    public void Pipeline_AddCustomAnalyzerToDefault_AppendsSuccessfully()
    {
        var analyzer = new MockOmegaAnalyzer();
        var engine = new AnalysisEngine();
        engine.Add(analyzer);

        Assert.AreEqual(6, engine.Analyzers.Count);
        Assert.AreSame(analyzer, engine.Analyzers[5]);
    }

    [TestMethod]
    public void Analyze_EmptyTradingResult_ReturnsGracefulReportAndExportsCleanly()
    {
        var result = new TradingResult(
            Array.Empty<SettlementRecord>(),
            Array.Empty<Trade>(),
            Array.Empty<PositionSnapshot>(),
            Array.Empty<EquityPoint>(),
            Array.Empty<DailySnapshot>(),
            Array.Empty<SignalRecord>()
        );

        var engine = new AnalysisEngine();
        var report = engine.Analyze(result);

        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Portfolio);
        Assert.AreEqual(0.0, report.Portfolio.TotalReturn);
        Assert.IsNotNull(report.Capital);
        Assert.AreEqual(0.0, report.Capital.AveragePositionRatio);
        Assert.IsNotNull(report.Trades);
        Assert.AreEqual(0, report.Trades.TotalTrades);
        Assert.IsNotNull(report.Cost);
        Assert.AreEqual(0.0, report.Cost.TotalFrictionCost);
        Assert.IsNull(report.Signals);

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
        var exporter = new ConsoleExporter(new ConsoleExporterOptions(), console);
        exporter.Export(report);
        string output = writer.ToString();

        Assert.IsTrue(output.Contains("CarrotQuant 量化回测与实盘绩效归因报告"));
        Assert.IsTrue(output.Contains("[1] 组合净值与风险收益统计"));
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Analyze_NullResult_ThrowsArgumentNullException()
    {
        var engine = new AnalysisEngine();
        engine.Analyze(null!);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Add_NullAnalyzer_ThrowsArgumentNullException()
    {
        var engine = new AnalysisEngine();
        engine.Add(null!);
    }

    [TestMethod]
    public void UnifiedInterface_AllCoreAndCustomResults_ExposeSelfDescribingMetricsAndIndexers()
    {
        var t0 = new DateTime(2024, 1, 1);
        var trades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, t0, 10.0, t0.AddDays(1), 12.0, 1000m, 1.0, 2000.0, 0.20, 5.0, 1.0, 1, "Tag1", "Tag1")
        };
        var equity = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0),
            new(t0.AddDays(1), 102000.0, 100000.0, 0.0, 0.0, 2000.0, 2000.0)
        };
        var settlements = new List<SettlementRecord>
        {
            new(t0, "sh.600000", "ord-1", OrderDirection.Buy, 10.0, 1000m, 10000.0, 5.0, 0.0, 90000.0, "Tag1")
        };
        var result = new TradingResult(
            settlements,
            trades,
            Array.Empty<PositionSnapshot>(),
            equity,
            Array.Empty<DailySnapshot>(),
            Array.Empty<SignalRecord>()
        );

        var engine = new AnalysisEngine();
        engine.Add(new MockOmegaAnalyzer());

        var report = engine.Analyze(result);
        Assert.IsNotNull(report);

        // 1. 验证统一索引器支持大小写不敏感检索
        Assert.IsNotNull(report["portfolio"]);
        Assert.IsNotNull(report["PORTFOLIO"]);
        Assert.IsNotNull(report["capital"]);
        Assert.IsNotNull(report["trades"]);
        Assert.IsNotNull(report["cost"]);
        Assert.IsNotNull(report["OmegaAnalyzer"]);
        Assert.IsNull(report["non_existent"]);

        // 2. 验证统一 Has<T>() 探针
        Assert.IsTrue(report.Has<PortfolioStatistics>());
        Assert.IsTrue(report.Has<CapitalStatistics>());
        Assert.IsTrue(report.Has<TradeStatistics>());
        Assert.IsTrue(report.Has<CostStatistics>());
        Assert.IsTrue(report.Has<MockOmegaResult>());
        Assert.IsFalse(report.Has<SignalStatistics>()); // 事实账本无信号，SignalStatistics 为 null

        // 3. 验证统一 Results 序列与自描述指标流
        Assert.AreEqual(5, report.Results.Count); // Portfolio, Capital, Trades, Cost, MockOmega
        foreach (var res in report.Results)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(res.Name));
            Assert.IsFalse(string.IsNullOrWhiteSpace(res.Title));

            var metrics = res.GetMetrics().ToList();
            Assert.IsTrue(metrics.Count > 0, $"{res.Name} 的自描述指标列表不应为空");
            foreach (var m in metrics)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(m.Label));
                Assert.IsFalse(string.IsNullOrWhiteSpace(m.Value));
            }
        }
    }

    [TestMethod]
    public void VerticalSlice_DirectAnalyzerCalculate_MatchesStaticFactoryEquivalence()
    {
        var options = new AnalysisOptions();
        var time0 = new DateTime(2023, 1, 1);
        var time1 = new DateTime(2023, 1, 2);

        var equityCurve = new List<EquityPoint>
        {
            new(time0, 100000, 100000, 0, 0, 0, 0),
            new(time1, 105000, 105000, 0, 0, 0, 0)
        };
        var snapshots = new List<DailySnapshot>
        {
            new(time0, 100000, 100000, 0, 0, 0, 0, Array.Empty<PositionSnapshot>()),
            new(time1, 105000, 105000, 0, 0, 0, 0, Array.Empty<PositionSnapshot>())
        };
        var trades = new List<Trade>
        {
            new("600000.SH", TradeSide.Long, time0, 10.0, time1, 11.0, 1000m, 1.0, 1000.0, 0.10, 5.0, 5.0, 1, "TagA", "TagB")
        };
        var settlements = new List<SettlementRecord>
        {
            new(time0, "600000.SH", "ord-1", OrderDirection.Buy, 10.0, 1000m, 10000.0, 5.0, 0.0, 90000.0, "TagA"),
            new(time1, "600000.SH", "ord-2", OrderDirection.Sell, 11.0, 1000m, 11000.0, 5.5, 0.0, 100994.5, "TagB")
        };

        // 1. PortfolioAnalyzer.Calculate
        var port1 = PortfolioAnalyzer.Calculate(equityCurve, snapshots, options);
        Assert.IsNotNull(port1);

        // 2. CapitalAnalyzer.Calculate
        var cap1 = CapitalAnalyzer.Calculate(snapshots, equityCurve, settlements, options);
        Assert.IsNotNull(cap1);

        // 3. TradeAnalyzer.Calculate
        var trd1 = TradeAnalyzer.Calculate(trades, options);
        Assert.IsNotNull(trd1);
        Assert.AreEqual(1, trd1.TotalTrades);

        // 4. CostAnalyzer.Calculate
        var cst1 = CostAnalyzer.Calculate(settlements, trades);
        Assert.IsNotNull(cst1);

        // 5. SignalAnalyzer.Calculate (空信号守恒)
        var sig1 = SignalAnalyzer.Calculate(Array.Empty<SignalRecord>(), null, options);
        Assert.IsNull(sig1);

        // 6. 独立 Analyzer 实例方法直接执行验证
        var pAnalyzer = new PortfolioAnalyzer();
        var facts = new TradingResult(settlements, trades, Array.Empty<PositionSnapshot>(), equityCurve, snapshots, Array.Empty<SignalRecord>());
        var directPort = pAnalyzer.Analyze(facts, options, null);
        Assert.IsNotNull(directPort);
        Assert.AreEqual(port1.TotalReturn, directPort.TotalReturn, 1e-9);
    }

    [TestMethod]
    public void Export_WithoutBenchmark_DoesNotRenderBenchmarkRow()
    {
        var t0 = new DateTime(2024, 1, 1);
        var equityCurve = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0),
            new(t0.AddDays(1), 105000.0, 105000.0, 0.0, 0.0, 0.0, 5000.0)
        };
        var result = new TradingResult
        {
            EquityCurve = equityCurve
        };

        var engine = new AnalysisEngine();
        // 未配置基准 (BenchmarkSymbol == null)
        var report = engine.Analyze(result, new AnalysisOptions { BenchmarkSymbol = null });

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
        var exporter = new ConsoleExporter(new ConsoleExporterOptions(), console);
        exporter.Export(report);

        string output = writer.ToString();

        // 验证绝对不包含基准与 Alpha、Beta 行
        Assert.IsFalse(output.Contains("基准累计收益 (Benchmark)"));
        Assert.IsFalse(output.Contains("系统贝塔 (Beta)"));
        Assert.IsFalse(output.Contains("詹森阿尔法 (Alpha)"));
        Assert.IsFalse(output.Contains("信息比率 (Information Ratio)"));
    }
}
