using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Analysis;
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
        Assert.IsTrue(report.Portfolio.TotalReturn > 0.0);
        Assert.IsNotNull(report.Portfolio.BenchmarkReturn);
        Assert.IsNotNull(report.Portfolio.Beta);

        // Level 2: 资本利用
        Assert.IsTrue(report.Capital.AveragePositionRatio > 0.0);
        Assert.IsTrue(report.Capital.CashDrag > 0.0);

        // Level 3: 交易归因
        Assert.AreEqual(1, report.Trades.TotalTrades);
        Assert.AreEqual(1.0, report.Trades.WinRate);
        Assert.IsNotNull(report.Trades.ExitTiming);

        // Level 4: 摩擦成本
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
        Assert.AreEqual(0.05, report.Portfolio.TotalReturn, 1e-4);
        Assert.IsNull(report.Portfolio.BenchmarkReturn);
        Assert.IsNull(report.Portfolio.Alpha);
        Assert.IsNull(report.Portfolio.Beta);
        Assert.IsNull(report.Portfolio.InformationRatio);
    }
}
