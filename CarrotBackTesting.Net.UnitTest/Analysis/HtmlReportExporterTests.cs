using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Analysis.Exporters;
using CarrotBacktesting.NET.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class HtmlReportExporterTests
{
    [TestMethod]
    public void RenderHtml_FullReportWithBenchmarkAndSignals_ContainsExpectedSections()
    {
        var t0 = new DateTime(2024, 1, 2);
        var equity = new List<EquityPoint>
        {
            new(t0, 100000, 50000, 0, 10000, 50000, 0),
            new(t0.AddDays(1), 110000, 40000, 0, 14000, 70000, 10000),
            new(t0.AddDays(2), 99000, 45000, 0, 11000, 54000, -1000),
            new(t0.AddDays(3), 115000, 55000, 0, 12000, 60000, 15000)
        };
        var trades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, t0, 10.0, t0.AddDays(1), 11.0, 1000, 1.0, 995, 0.0995, 5, 0, 1, "alpha</script>", ""),
            new("sz.000001", TradeSide.Long, t0.AddDays(1), 20.0, t0.AddDays(2), 18.0, 500, 1.0, -1005, -0.1005, 5, 0, 1, "alpha", "")
        };

        var facts = new TradingResult(
            settlements: Array.Empty<SettlementRecord>(),
            closedTrades: trades,
            openPositions: Array.Empty<PositionSnapshot>(),
            equityCurve: equity,
            dailySnapshots: Array.Empty<DailySnapshot>(),
            signals: Array.Empty<SignalRecord>(),
            orderEvents: Array.Empty<OrderEvent>(),
            status: TradingStatus.Bankrupt);

        var benchmarkCurve = new List<BenchmarkPoint>
        {
            new(t0, 1.0),
            new(t0.AddDays(1), 1.02),
            new(t0.AddDays(2), 0.99),
            new(t0.AddDays(3), 1.05)
        };

        var portfolio = new PortfolioStatistics(
            TotalReturn: 0.15,
            AnnualizedReturn: 0.45,
            AnnualizedVolatility: 0.20,
            DownsideVolatility: 0.12,
            MaxDrawdown: 0.10,
            MaxDrawdownPeakTime: t0.AddDays(1),
            MaxDrawdownTroughTime: t0.AddDays(2),
            MaxDrawdownRecoveryTime: t0.AddDays(3),
            MaxDrawdownDurationSteps: 2,
            SharpeRatio: 2.1,
            SortinoRatio: 3.0,
            CalmarRatio: 4.5,
            DailyVaR95: 0.05,
            DailyCVaR95: 0.07,
            BenchmarkReturn: 0.05,
            Alpha: 0.10,
            Beta: 1.1,
            InformationRatio: 1.4,
            MonthlyReturns: new[] { new MonthlyReturn(2024, 1, 0.15) },
            BenchmarkCurve: benchmarkCurve);

        var report = new AnalysisReport(facts, portfolio, null, null, null, null);
        string html = HtmlReportExporter.RenderHtml(report, new HtmlReportExporterOptions { Title = "Test <TearSheet>" });

        StringAssert.Contains(html, "<!DOCTYPE html>");
        StringAssert.Contains(html, "Test &lt;TearSheet&gt;");
        StringAssert.Contains(html, "echarts");
        StringAssert.Contains(html, "benchmarkReturnsPct");
        StringAssert.Contains(html, "Bankrupt");
        // 防御 </script> 注入：原始未转义的 </script> 不可出现在 JSON 载荷中
        Assert.IsFalse(html.Contains("alpha</script>", StringComparison.Ordinal));
        StringAssert.Contains(html, "alpha\\u003C");
    }

    [TestMethod]
    public void Save_EmptyReport_WritesValidHtmlFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CarrotHtmlTest_" + Guid.NewGuid().ToString("N"));
        string outPath = Path.Combine(tempDir, "nested", "tearsheet.html");

        try
        {
            var emptyReport = new AnalysisReport(new TradingResult(), Array.Empty<IAnalysisResult>());
            HtmlReportExporter.Save(emptyReport, outPath);

            Assert.IsTrue(File.Exists(outPath));
            string content = File.ReadAllText(outPath);
            StringAssert.Contains(content, "window.__TEARSHEET_DATA__");
            StringAssert.Contains(content, "\"totalBars\":0");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void RenderHtml_LargeSeries_DownsamplesToMaxChartPoints()
    {
        var t0 = new DateTime(2020, 1, 1);
        var equity = new List<EquityPoint>(500);
        for (int i = 0; i < 500; i++)
        {
            double val = 100000 + Math.Sin(i * 0.1) * 5000 + i * 10;
            equity.Add(new EquityPoint(t0.AddDays(i), val, 50000, 0, 10000, val - 50000, 0));
        }

        var facts = new TradingResult(
            Array.Empty<SettlementRecord>(),
            Array.Empty<Trade>(),
            Array.Empty<PositionSnapshot>(),
            equity,
            Array.Empty<DailySnapshot>(),
            Array.Empty<SignalRecord>());

        var report = new AnalysisReport(facts, Array.Empty<IAnalysisResult>());
        string html = HtmlReportExporter.RenderHtml(report, new HtmlReportExporterOptions { MaxChartPoints = 50 });

        // 原始 500 点被记录为 totalBars=500，而图表采样后数组长度应为 50
        StringAssert.Contains(html, "\"totalBars\":500");
        int marker = html.IndexOf("\"times\":[", StringComparison.Ordinal);
        Assert.IsTrue(marker > 0);
        int endBracket = html.IndexOf("]", marker, StringComparison.Ordinal);
        string timesSlice = html.Substring(marker, endBracket - marker);
        int commaCount = timesSlice.Split(',').Length;
        Assert.AreEqual(50, commaCount);
    }

    [TestMethod]
    public void AnalysisConfig_YamlHtmlExporter_ResolvesAndExportsFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CarrotHtmlConfigTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string yaml = """
                exporters:
                  - type: "html"
                    params:
                      output_path: "reports/my_tear_sheet.html"
                      title: "YAML Driven Tear Sheet"
                      dark_theme: false
                """;

            var config = AnalysisConfig.FromYaml(yaml, tempDir);
            var report = new AnalysisReport(new TradingResult(), Array.Empty<IAnalysisResult>());
            config.Export(report, tempDir);

            string expectedPath = Path.Combine(tempDir, "reports", "my_tear_sheet.html");
            Assert.IsTrue(File.Exists(expectedPath));
            string html = File.ReadAllText(expectedPath);
        StringAssert.Contains(html, "data-theme=\"light\"");
        StringAssert.Contains(html, "YAML Driven Tear Sheet");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void RenderHtml_NonFiniteMetricsAndXssPayloads_AreSanitizedAndEscaped()
    {
        var t0 = new DateTime(2024, 1, 2);
        var trades = new List<Trade>
        {
            new("sh.600000<img src=x>", TradeSide.Long, t0, 10.0, t0.AddDays(1), 11.0, 100, 1.0, double.NaN, double.PositiveInfinity, 5, 0, 1, "<script>alert(1)</script>", "")
        };
        var facts = new TradingResult(Array.Empty<SettlementRecord>(), trades, Array.Empty<PositionSnapshot>(), Array.Empty<EquityPoint>(), Array.Empty<DailySnapshot>(), Array.Empty<SignalRecord>());
        var tagStats = new TradeStatistics(1, 1, 0, 0, 1.0, 1, 1, 1.0, 0, 0, 0.0, 0.1, 0.1, 0.1, 0.0, double.PositiveInfinity, double.NaN, 1, 0, 1, 1, 0, null, null, null, new Dictionary<string, TradeStatistics>());
        var tradeStats = new TradeStatistics(1, 1, 0, 0, 1.0, 1, 1, 1.0, 0, 0, 0.0, 0.1, 0.1, 0.1, 0.0, double.PositiveInfinity, double.NaN, 1, 0, 1, 1, 0, null, null, null, new Dictionary<string, TradeStatistics> { ["<b>tag</b>"] = tagStats });

        var report = new AnalysisReport(facts, null, null, tradeStats, null, null);
        string html = HtmlReportExporter.RenderHtml(report);

        StringAssert.Contains(html, "function escapeHtml(");
        Assert.IsFalse(html.Contains("<script>alert(1)</script>", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreateExporters_ProgrammaticConfig_NormalizesRelativePaths()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "CarrotBase_" + Guid.NewGuid().ToString("N"));
        var config = new AnalysisConfig();
        config.Exporters.Add(new ExporterNodeConfig
        {
            Type = "html",
            Params = new Dictionary<string, object> { ["output_path"] = "sub/out.html" }
        });
        config.Exporters.Add(new ExporterNodeConfig
        {
            Type = "excel",
            Params = new Dictionary<string, object> { ["output_path"] = "sub/out.xlsx" }
        });

        var exporters = AnalysisEngineFactory.CreateExporters(config, baseDir);
        Assert.AreEqual(2, exporters.Count);
        var htmlExp = (HtmlReportExporter)exporters[0];
        var excelExp = (ExcelReportExporter)exporters[1];
        Assert.AreEqual(Path.GetFullPath(Path.Combine(baseDir, "sub/out.html")), htmlExp.OutputPath);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(baseDir, "sub/out.xlsx")), excelExp.OutputPath);
    }

    [TestMethod]
    public void RenderHtml_ExceedingMaxTradeTableRows_TruncatesToRecentTrades()
    {
        var t0 = new DateTime(2024, 1, 2);
        var trades = new List<Trade>();
        for (int i = 0; i < 10; i++)
        {
            trades.Add(new Trade($"s.{i:D2}", TradeSide.Long, t0, 10.0, t0.AddDays(1), 11.0, 100, 1.0, 100, 0.1, 1, 0, 1, "t", "t"));
        }

        var facts = new TradingResult(Array.Empty<SettlementRecord>(), trades, Array.Empty<PositionSnapshot>(), Array.Empty<EquityPoint>(), Array.Empty<DailySnapshot>(), Array.Empty<SignalRecord>());
        var report = new AnalysisReport(facts, Array.Empty<IAnalysisResult>());

        string html = HtmlReportExporter.RenderHtml(report, new HtmlReportExporterOptions { MaxTradeTableRows = 3 });
        StringAssert.Contains(html, "\"tradesTruncated\":true");
        StringAssert.Contains(html, "\"totalTrades\":10");
        int rowsIdx = html.IndexOf("\"tradeRows\":[", StringComparison.Ordinal);
        Assert.IsTrue(rowsIdx > 0);
        string tradeRowsSlice = html.Substring(rowsIdx, html.IndexOf("]", rowsIdx, StringComparison.Ordinal) - rowsIdx);
        StringAssert.Contains(tradeRowsSlice, "\"symbol\":\"s.09\"");
        Assert.IsFalse(tradeRowsSlice.Contains("\"symbol\":\"s.00\"", StringComparison.Ordinal));
    }
}
