using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Analysis.Analyzers;
using CarrotBacktesting.NET.Analysis.Exporters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class JsonReportExporterTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CarrotQuant_JsonExporterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public void RoundTrip_FullReport_RestoresAllFactsAndStatisticsAccurately()
    {
        // 1. 构造包含完整事实的 TradingResult
        var t0 = new DateTime(2025, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        var t1 = t0.AddDays(1);
        var t2 = t0.AddDays(2);

        var settlements = new List<SettlementRecord>
        {
            new(t0, "sh.600000", 1L, OrderDirection.Buy, 10.5, 1000m, 10500.0, 5.25, 0.0, 89494.75, "TrendAlpha"),
            new(t1, "sh.600000", 2L, OrderDirection.Sell, 11.2, 1000m, 11200.0, 5.60, 700.0, 100689.15, "TrendAlpha")
        };

        var closedTrades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, t0, 10.5, t1, 11.2, 1000m, 1.0, 689.15, 0.0656, 10.85, 2.0, 1, "TrendAlpha", "TakeProfit")
        };

        var openPositions = new List<PositionSnapshot>
        {
            new("sz.000001", TradeSide.Long, 500m, 500m, 0m, 15.0, 15.5, 1.0, 7750.0, 250.0, 1.0, 7750.0, 0.077)
        };

        var equityCurve = new List<EquityPoint>
        {
            new(t0, 100000.0, 89494.75, 0.0, 10500.0, 10500.0, 0.0),
            new(t1, 100689.15, 100689.15, 0.0, 0.0, 0.0, 0.0),
            new(t2, 101000.0, 93250.0, 0.0, 7750.0, 7750.0, 250.0)
        };

        var dailySnapshots = new List<DailySnapshot>
        {
            new(t0.Date, 100000.0, 89494.75, 0.0, 10500.0, 10500.0, 0.0, Array.Empty<PositionSnapshot>()),
            new(t1.Date, 100689.15, 100689.15, 0.0, 0.0, 0.0, 0.0, Array.Empty<PositionSnapshot>()),
            new(t2.Date, 101000.0, 93250.0, 0.0, 7750.0, 7750.0, 250.0, openPositions)
        };

        var signals = new List<SignalRecord>
        {
            new(t0, "sh.600000", 1.0, "TrendBreakout", "EMA_Cross")
        };

        var facts = new TradingResult(settlements, closedTrades, openPositions, equityCurve, dailySnapshots, signals);

        // 2. 构造官方各层统计产物
        var monthlyReturns = new List<MonthlyReturn>
        {
            new(2025, 1, 0.052),
            new(2025, 2, -0.015)
        };

        var portfolio = new PortfolioStatistics(
            TotalReturn: 0.155,
            AnnualizedReturn: 0.182,
            AnnualizedVolatility: 0.125,
            DownsideVolatility: 0.085,
            MaxDrawdown: 0.045,
            MaxDrawdownPeakTime: t0,
            MaxDrawdownTroughTime: t1,
            MaxDrawdownRecoveryTime: t2,
            MaxDrawdownDurationSteps: 2,
            SharpeRatio: 1.85,
            SortinoRatio: 2.14,
            CalmarRatio: 4.04,
            DailyVaR95: 0.018,
            DailyCVaR95: 0.025,
            BenchmarkReturn: 0.082,
            Alpha: 0.073,
            Beta: 0.92,
            InformationRatio: 1.45,
            MonthlyReturns: monthlyReturns
        );

        var capital = new CapitalStatistics(
            AveragePositionRatio: 0.65,
            MaxPositionRatio: 0.95,
            AverageMarginRatio: 0.0,
            MaxMarginRatio: 0.0,
            CashDrag: 0.35,
            AnnualizedTurnover: 4.5
        );

        var exitTiming = new ExitTimingStatistics(
            HorizonSteps: 3,
            ValidTradesCount: 1,
            AverageReturns: new double[] { 0.01, 0.02, -0.005 },
            MedianReturns: new double[] { 0.01, 0.02, -0.005 },
            WinRates: new double[] { 1.0, 1.0, 0.0 }
        );

        var subTradeStat = new TradeStatistics(
            TotalTrades: 1, WinningTrades: 1, LosingTrades: 0, BreakevenTrades: 0,
            WinRate: 1.0, LongTrades: 1, LongWinningTrades: 1, LongWinRate: 1.0,
            ShortTrades: 0, ShortWinningTrades: 0, ShortWinRate: 0.0,
            AverageReturn: 0.0656, MedianReturn: 0.0656, AverageWinReturn: 0.0656, AverageLossReturn: 0.0,
            WinLossRatio: 0.0, ProfitFactor: 0.0, MaxConsecutiveWins: 1, MaxConsecutiveLosses: 0,
            AverageHoldingSteps: 1.0, AverageWinHoldingSteps: 1.0, AverageLossHoldingSteps: 0.0,
            AverageTradeEfficiency: 0.85, MedianTradeEfficiency: 0.85, ExitTiming: null,
            TagBreakdowns: new Dictionary<string, TradeStatistics>()
        );

        var tradeBreakdowns = new Dictionary<string, TradeStatistics>
        {
            { "TrendAlpha", subTradeStat }
        };

        var trades = new TradeStatistics(
            TotalTrades: 1, WinningTrades: 1, LosingTrades: 0, BreakevenTrades: 0,
            WinRate: 1.0, LongTrades: 1, LongWinningTrades: 1, LongWinRate: 1.0,
            ShortTrades: 0, ShortWinningTrades: 0, ShortWinRate: 0.0,
            AverageReturn: 0.0656, MedianReturn: 0.0656, AverageWinReturn: 0.0656, AverageLossReturn: 0.0,
            WinLossRatio: 0.0, ProfitFactor: 0.0, MaxConsecutiveWins: 1, MaxConsecutiveLosses: 0,
            AverageHoldingSteps: 1.0, AverageWinHoldingSteps: 1.0, AverageLossHoldingSteps: 0.0,
            AverageTradeEfficiency: 0.85, MedianTradeEfficiency: 0.85, ExitTiming: exitTiming,
            TagBreakdowns: tradeBreakdowns
        );

        var cost = new CostStatistics(
            TotalCommission: 10.85,
            TotalSlippage: 2.0,
            TotalFrictionCost: 12.85,
            FeeToGrossProfitRatio: 0.0183,
            AverageFrictionPerTrade: 12.85
        );

        var horizons = new List<SignalHorizonStatistics>
        {
            new(1, new SignalSampleStatistics(1, 1.0, 0.03, 0.03, 0.03, 0.0, 0.0),
                   new SignalSampleStatistics(1, 1.0, 0.03, 0.03, 0.03, 0.0, 0.0))
        };

        var signalsStat = new SignalStatistics(
            TotalSignals: 1,
            HorizonSteps: 1,
            Horizons: horizons,
            PeakHorizonStep: 1,
            GroupBreakdowns: new Dictionary<string, SignalStatistics>()
        );

        // 3. 构造第三方自定义扩展指标
        var customResult = new MockCustomResult("liquidity_risk", "流动性冲击分析", new[]
        {
            new MetricItem("平均买卖价差", "0.05%", "冲击风险"),
            new MetricItem("换手冲击系数", "1.12", "冲击风险")
        });

        var originalReport = new AnalysisReport(facts, portfolio, capital, trades, cost, signalsStat, new[] { customResult });

        // 4. 执行持久化
        string filePath = Path.Combine(_tempDir, "full_report.json");
        JsonReportExporter.Save(originalReport, filePath, indented: true);

        Assert.IsTrue(File.Exists(filePath), "持久化文件必须已创建。");

        // 5. 反序列化还原
        var restored = JsonReportExporter.Load(filePath);

        // 6. 验证 Facts 事实守恒
        Assert.AreEqual(originalReport.Facts.Settlements.Count, restored.Facts.Settlements.Count);
        Assert.AreEqual(originalReport.Facts.Settlements[0].OrderId, restored.Facts.Settlements[0].OrderId);
        Assert.AreEqual(originalReport.Facts.Settlements[0].Price, restored.Facts.Settlements[0].Price, 1e-6);
        Assert.AreEqual(originalReport.Facts.Settlements[0].Quantity, restored.Facts.Settlements[0].Quantity);
        Assert.AreEqual(originalReport.Facts.Settlements[0].Direction, restored.Facts.Settlements[0].Direction);
        Assert.AreEqual(originalReport.Facts.Settlements[0].Turnover, restored.Facts.Settlements[0].Turnover, 1e-6);

        Assert.AreEqual(originalReport.Facts.ClosedTrades.Count, restored.Facts.ClosedTrades.Count);
        Assert.AreEqual(originalReport.Facts.ClosedTrades[0].RealizedPnL, restored.Facts.ClosedTrades[0].RealizedPnL, 1e-6);
        Assert.AreEqual(originalReport.Facts.ClosedTrades[0].ReturnRate, restored.Facts.ClosedTrades[0].ReturnRate, 1e-6);
        Assert.AreEqual(originalReport.Facts.ClosedTrades[0].Side, restored.Facts.ClosedTrades[0].Side);
        Assert.AreEqual(originalReport.Facts.ClosedTrades[0].EntryTag, restored.Facts.ClosedTrades[0].EntryTag);
        Assert.AreEqual(originalReport.Facts.ClosedTrades[0].ExitTag, restored.Facts.ClosedTrades[0].ExitTag);

        Assert.AreEqual(originalReport.Facts.EquityCurve.Count, restored.Facts.EquityCurve.Count);
        Assert.AreEqual(originalReport.Facts.EquityCurve[0].TotalEquity, restored.Facts.EquityCurve[0].TotalEquity, 1e-6);

        Assert.AreEqual(originalReport.Facts.DailySnapshots.Count, restored.Facts.DailySnapshots.Count);
        Assert.AreEqual(1, restored.Facts.DailySnapshots[2].Positions.Count);
        Assert.AreEqual("sz.000001", restored.Facts.DailySnapshots[2].Positions[0].Symbol);
        Assert.AreEqual(15.0, restored.Facts.DailySnapshots[2].Positions[0].CostPrice, 1e-6);

        Assert.AreEqual(originalReport.Facts.OpenPositions.Count, restored.Facts.OpenPositions.Count);
        Assert.AreEqual(originalReport.Facts.OpenPositions[0].Symbol, restored.Facts.OpenPositions[0].Symbol);

        // 验证策略决策信号事实守恒
        Assert.AreEqual(originalReport.Facts.Signals.Count, restored.Facts.Signals.Count);
        Assert.AreEqual(originalReport.Facts.Signals[0].Symbol, restored.Facts.Signals[0].Symbol);
        Assert.AreEqual(originalReport.Facts.Signals[0].SignalValue, restored.Facts.Signals[0].SignalValue, 1e-6);
        Assert.AreEqual(originalReport.Facts.Signals[0].SignalGroup, restored.Facts.Signals[0].SignalGroup);
        Assert.AreEqual(originalReport.Facts.Signals[0].Reason, restored.Facts.Signals[0].Reason);

        // 7. 验证五大官方统计守恒
        Assert.IsNotNull(restored.Portfolio);
        Assert.AreEqual(portfolio.TotalReturn, restored.Portfolio.TotalReturn, 1e-6);
        Assert.AreEqual(portfolio.SharpeRatio, restored.Portfolio.SharpeRatio, 1e-6);
        Assert.AreEqual(portfolio.MonthlyReturns.Count, restored.Portfolio.MonthlyReturns.Count);
        Assert.AreEqual(portfolio.MonthlyReturns[0].Year, restored.Portfolio.MonthlyReturns[0].Year);
        Assert.AreEqual(portfolio.MonthlyReturns[0].ReturnRate, restored.Portfolio.MonthlyReturns[0].ReturnRate, 1e-6);

        Assert.IsNotNull(restored.Capital);
        Assert.AreEqual(capital.AveragePositionRatio, restored.Capital.AveragePositionRatio, 1e-6);
        Assert.AreEqual(capital.AnnualizedTurnover, restored.Capital.AnnualizedTurnover, 1e-6);

        Assert.IsNotNull(restored.Trades);
        Assert.AreEqual(trades.TotalTrades, restored.Trades.TotalTrades);
        Assert.AreEqual(trades.WinRate, restored.Trades.WinRate, 1e-6);
        Assert.IsNotNull(restored.Trades.ExitTiming);
        Assert.AreEqual(exitTiming.HorizonSteps, restored.Trades.ExitTiming.HorizonSteps);
        Assert.AreEqual(exitTiming.AverageReturns[0], restored.Trades.ExitTiming.AverageReturns[0], 1e-6);
        Assert.AreEqual(1, restored.Trades.TagBreakdowns.Count);
        Assert.AreEqual(trades.TagBreakdowns["TrendAlpha"].AverageReturn, restored.Trades.TagBreakdowns["TrendAlpha"].AverageReturn, 1e-6);

        Assert.IsNotNull(restored.Cost);
        Assert.AreEqual(cost.TotalCommission, restored.Cost.TotalCommission, 1e-6);
        Assert.AreEqual(cost.TotalFrictionCost, restored.Cost.TotalFrictionCost, 1e-6);

        Assert.IsNotNull(restored.Signals);
        Assert.AreEqual(signalsStat.TotalSignals, restored.Signals.TotalSignals);
        Assert.AreEqual(signalsStat.Horizons.Count, restored.Signals.Horizons.Count);

        // 8. 验证第三方扩展指标守恒
        var restoredExt = restored["liquidity_risk"];
        Assert.IsNotNull(restoredExt, "扩展自描述指标必须能通过名称索引成功检出。");
        Assert.AreEqual("流动性冲击分析", restoredExt.Title);
        var metrics = restoredExt.GetMetrics().ToList();
        Assert.AreEqual(2, metrics.Count);
        Assert.AreEqual("平均买卖价差", metrics[0].Label);
        Assert.AreEqual("0.05%", metrics[0].Value);
    }

    [TestMethod]
    public void RoundTrip_EmptyAndNaNEdgeCases_HandlesGracefully()
    {
        // 构造含有 NaN 特殊值（如未交易时的零除）与空集合的边缘报告
        var emptyFacts = new TradingResult();
        var emptyPortfolio = new PortfolioStatistics(
            TotalReturn: 0.0,
            AnnualizedReturn: 0.0,
            AnnualizedVolatility: 0.0,
            DownsideVolatility: 0.0,
            MaxDrawdown: 0.0,
            MaxDrawdownPeakTime: DateTime.MinValue,
            MaxDrawdownTroughTime: DateTime.MinValue,
            MaxDrawdownRecoveryTime: null,
            MaxDrawdownDurationSteps: 0,
            SharpeRatio: double.NaN, // 特殊值 NaN
            SortinoRatio: double.NaN,
            CalmarRatio: double.NaN,
            DailyVaR95: 0.0,
            DailyCVaR95: 0.0,
            BenchmarkReturn: null,
            Alpha: null,
            Beta: null,
            InformationRatio: null,
            MonthlyReturns: Array.Empty<MonthlyReturn>()
        );

        var report = new AnalysisReport(emptyFacts, emptyPortfolio, null, null, null, null);

        string json = JsonReportExporter.ToJson(report, indented: false);
        Assert.IsTrue(json.Contains("NaN") || json.Contains("\"NaN\""), "JSON 应当容错支持 NaN 特殊浮点数。");

        var restored = JsonReportExporter.FromJson(json);
        Assert.IsNotNull(restored.Portfolio);
        Assert.IsTrue(double.IsNaN(restored.Portfolio.SharpeRatio), "反序列化后应当精确保留 NaN 状态。");
        Assert.AreEqual(0, restored.Facts.ClosedTrades.Count);
    }

    [TestMethod]
    public void JsonReportExporter_ExportsValidJsonFile_CanBeReloaded()
    {
        var facts = new TradingResult();
        var report = new AnalysisReport(facts, null, null, null, null, null);

        string outputPath = Path.Combine(_tempDir, "exporter_output.json");
        var exporter = new JsonReportExporter(outputPath);
        exporter.Export(report);

        Assert.IsTrue(File.Exists(outputPath));
        var loaded = JsonReportExporter.Load(outputPath);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(0, loaded.Results.Count);

        // 验证 JsonReportExporter 静态直通 API
        string staticPath = Path.Combine(_tempDir, "static_output.json");
        JsonReportExporter.Save(report, staticPath);
        var loadedStatic = JsonReportExporter.Load(staticPath);
        Assert.IsNotNull(loadedStatic);
        Assert.AreEqual(0, loadedStatic.Results.Count);
    }

    [TestMethod]
    public void OfflineRendering_RestoredReport_CanBeRenderedByConsoleExporter()
    {
        // 构造一个基础报告并持久化
        var t0 = new DateTime(2025, 1, 1);
        var equity = new List<EquityPoint>
        {
            new(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0),
            new(t0.AddDays(1), 105000.0, 105000.0, 0.0, 0.0, 0.0, 0.0)
        };
        var facts = new TradingResult(Array.Empty<SettlementRecord>(), Array.Empty<Trade>(), Array.Empty<PositionSnapshot>(), equity, Array.Empty<DailySnapshot>(), Array.Empty<SignalRecord>());
        var portfolio = new PortfolioStatistics(
            TotalReturn: 0.05,
            AnnualizedReturn: 0.12,
            AnnualizedVolatility: 0.10,
            DownsideVolatility: 0.06,
            MaxDrawdown: 0.01,
            MaxDrawdownPeakTime: t0,
            MaxDrawdownTroughTime: t0,
            MaxDrawdownRecoveryTime: null,
            MaxDrawdownDurationSteps: 0,
            SharpeRatio: 1.20,
            SortinoRatio: 1.50,
            CalmarRatio: 12.0,
            DailyVaR95: 0.01,
            DailyCVaR95: 0.015,
            BenchmarkReturn: null,
            Alpha: null,
            Beta: null,
            InformationRatio: null,
            MonthlyReturns: new[] { new MonthlyReturn(2025, 1, 0.05) }
        );

        var original = new AnalysisReport(facts, portfolio, null, null, null, null);
        string jsonPath = Path.Combine(_tempDir, "offline_report.json");
        JsonReportExporter.Save(original, jsonPath);

        // 离线加载还原（完全脱离回测引擎计算上下文）
        var offlineReport = JsonReportExporter.Load(jsonPath);

        // 验证传入 ConsoleExporter 离线脱机渲染成功
        string htmlPath = Path.Combine(_tempDir, "offline_view.html");
        var consoleExporter = new ConsoleExporter(new ConsoleExporterOptions
        {
            SaveHtml = true,
            HtmlOutputPath = htmlPath
        });

        // 必须能够顺利完成渲染，绝不抛出任何异常
        consoleExporter.Export(offlineReport);

        Assert.IsTrue(File.Exists(htmlPath), "脱机还原的报告必须能够成功生成离线深色 HTML 报表文件。");
        string htmlContent = File.ReadAllText(htmlPath);
        Assert.IsTrue(htmlContent.Contains("CarrotQuant") || htmlContent.Contains("html"), "HTML 报表内容不可为空。");
    }

    private sealed class MockCustomResult : IAnalysisResult
    {
        public string Name { get; }
        public string Title { get; }
        private readonly IReadOnlyList<MetricItem> _metrics;

        public MockCustomResult(string name, string title, IReadOnlyList<MetricItem> metrics)
        {
            Name = name;
            Title = title;
            _metrics = metrics;
        }

        public IEnumerable<MetricItem> GetMetrics() => _metrics;
    }
}
