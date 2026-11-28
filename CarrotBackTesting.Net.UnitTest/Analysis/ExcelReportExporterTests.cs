using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Analysis.Exporters;
using CarrotBacktesting.NET.Config;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class ExcelReportExporterTests
{
    [TestMethod]
    public void Save_FullReport_CreatesMultiSheetWorkbookWithExpectedData()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CarrotExcelTest_" + Guid.NewGuid().ToString("N"));
        string filePath = Path.Combine(tempDir, "sub", "report.xlsx");

        try
        {
            var report = BuildRichReport();
            ExcelReportExporter.Save(report, filePath);

            Assert.IsTrue(File.Exists(filePath));

            using var wb = new XLWorkbook(filePath);
            var sheetNames = wb.Worksheets.Select(w => w.Name).ToList();

            CollectionAssert.Contains(sheetNames, "Dashboard");
            CollectionAssert.Contains(sheetNames, "Monthly_Returns");
            CollectionAssert.Contains(sheetNames, "Closed_Trades");
            CollectionAssert.Contains(sheetNames, "Settlements");
            CollectionAssert.Contains(sheetNames, "Daily_Snapshots");
            CollectionAssert.Contains(sheetNames, "Open_Positions");
            CollectionAssert.Contains(sheetNames, "Exit_Timing");
            CollectionAssert.Contains(sheetNames, "Trade_Tags");
            CollectionAssert.Contains(sheetNames, "Signal_Analysis");
            CollectionAssert.Contains(sheetNames, "Signals");
            CollectionAssert.Contains(sheetNames, "Order_Events");

            var tradesSheet = wb.Worksheet("Closed_Trades");
            Assert.AreEqual("sh.600000", tradesSheet.Cell(2, 1).GetString());
            Assert.AreEqual(995.0, tradesSheet.Cell(2, 9).GetDouble(), 1e-6);

            var monthlySheet = wb.Worksheet("Monthly_Returns");
            Assert.AreEqual(2024, (int)monthlySheet.Cell(2, 1).GetDouble());
            Assert.AreEqual(0.10, monthlySheet.Cell(2, 2).GetDouble(), 1e-6);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Save_EmptyReport_CreatesValidWorkbookWithoutThrowing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CarrotExcelEmpty_" + Guid.NewGuid().ToString("N"));
        string filePath = Path.Combine(tempDir, "empty.xlsx");

        try
        {
            var empty = new AnalysisReport(new TradingResult(), Array.Empty<IAnalysisResult>());
            ExcelReportExporter.Save(empty, filePath);

            Assert.IsTrue(File.Exists(filePath));
            using var wb = new XLWorkbook(filePath);
            Assert.IsTrue(wb.Worksheets.Contains("Dashboard"));
            Assert.IsTrue(wb.Worksheets.Contains("Closed_Trades"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Save_OptionsDisabledOptionalSheets_OmitsDisabledSheets()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CarrotExcelOpt_" + Guid.NewGuid().ToString("N"));
        string filePath = Path.Combine(tempDir, "opt.xlsx");

        try
        {
            var report = BuildRichReport();
            var options = new ExcelReportExporterOptions
            {
                IncludeDailySnapshots = false,
                IncludeSignals = false,
                IncludeOrderEvents = false
            };

            ExcelReportExporter.Save(report, filePath, options);

            using var wb = new XLWorkbook(filePath);
            Assert.IsFalse(wb.Worksheets.Contains("Daily_Snapshots"));
            Assert.IsFalse(wb.Worksheets.Contains("Signals"));
            Assert.IsFalse(wb.Worksheets.Contains("Order_Events"));
            Assert.IsTrue(wb.Worksheets.Contains("Closed_Trades"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void AnalysisConfig_YamlExcelExporter_ResolvesAndExportsFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CarrotExcelCfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string yaml = """
                exporters:
                  - type: "excel"
                    params:
                      output_path: "out/backtest.xlsx"
                      include_signals: false
                """;

            var config = AnalysisConfig.FromYaml(yaml, tempDir);
            var report = BuildRichReport();
            config.Export(report, tempDir);

            string expectedPath = Path.Combine(tempDir, "out", "backtest.xlsx");
            Assert.IsTrue(File.Exists(expectedPath));
            using var wb = new XLWorkbook(expectedPath);
            Assert.IsFalse(wb.Worksheets.Contains("Signals"));
            Assert.IsTrue(wb.Worksheets.Contains("Dashboard"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static AnalysisReport BuildRichReport()
    {
        var t0 = new DateTime(2024, 1, 2);
        var t1 = new DateTime(2024, 1, 3);

        var settlements = new List<SettlementRecord>
        {
            new(t0, "sh.600000", 1, OrderDirection.Buy, 10.0, 1000, 10000, 5, 0, 90000, "entry"),
            new(t1, "sh.600000", 2, OrderDirection.Sell, 11.0, 1000, 11000, 5, 995, 100995, "exit")
        };

        var closedTrades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, t0, 10.0, t1, 11.0, 1000, 1.0, 995, 0.0995, 5, 0, 1, "momentum", "tp")
        };

        var openPositions = new List<PositionSnapshot>
        {
            new("sz.000001", TradeSide.Long, 500, 500, 0, 20.0, 21.0, 1.0, 10500, 500, 1.0, 10500, 0.1)
        };

        var equity = new List<EquityPoint>
        {
            new(t0, 100000, 90000, 0, 10000, 10000, 0),
            new(t1, 101495, 90995, 0, 10500, 10500, 500)
        };

        var daily = new List<DailySnapshot>
        {
            new(t0, 100000, 90000, 0, 10000, 10000, 0, openPositions),
            new(t1, 101495, 90995, 0, 10500, 10500, 500, openPositions)
        };

        var rawSignals = new List<SignalRecord>
        {
            new(t0, "sh.600000", 0.95, "Grp:A/B?", "cross")
        };

        var orderEvents = new List<OrderEvent>
        {
            new(t0, 1, "sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 1000, OrderEventType.Fill, OrderStatus.Filled, 1000, 1000, 0, 10.0, LiquidityRole.Taker, "", "momentum")
        };

        var facts = new TradingResult(settlements, closedTrades, openPositions, equity, daily, rawSignals, orderEvents);

        var portfolio = new PortfolioStatistics(
            0.015, 0.25, 0.10, 0.05, 0.0, t0, t0, null, 0,
            2.3, 3.1, 0.0, 0.01, 0.02, null, null, null, null,
            new[] { new MonthlyReturn(2024, 1, 0.10) });

        var exitTiming = new ExitTimingStatistics(2, 1, new[] { 0.01, 0.02 }, new[] { 0.01, 0.02 }, new[] { 1.0, 1.0 });
        var tagStats = new TradeStatistics(
            1, 1, 0, 0, 1.0, 1, 1, 1.0, 0, 0, 0.0,
            0.0995, 0.0995, 0.0995, 0.0, 0.0, 0.0, 1, 0, 1.0, 1.0, 0.0, null, null, null,
            new Dictionary<string, TradeStatistics>());

        var trades = new TradeStatistics(
            1, 1, 0, 0, 1.0, 1, 1, 1.0, 0, 0, 0.0,
            0.0995, 0.0995, 0.0995, 0.0, 0.0, 0.0, 1, 0, 1.0, 1.0, 0.0, null, null, exitTiming,
            new Dictionary<string, TradeStatistics> { ["momentum"] = tagStats });

        var horizon = new SignalHorizonStatistics(
            1,
            new SignalSampleStatistics(1, 1.0, 0.05, 0.05, 0.05, 0.0, 0.0),
            new SignalSampleStatistics(1, 1.0, 0.05, 0.05, 0.05, 0.0, 0.0));
        var grpStats = new SignalStatistics(1, 1, new[] { horizon }, 1, new Dictionary<string, SignalStatistics>());
        var signals = new SignalStatistics(
            1, 1, new[] { horizon }, 1,
            new Dictionary<string, SignalStatistics> { ["'Invalid:Sheet/Name*LengthExceeds31'"] = grpStats });

        return new AnalysisReport(facts, portfolio, null, trades, null, signals);
    }

    [TestMethod]
    public void Save_MinDateTimeAndSingleQuotedSheetName_HandlesSafely()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CarrotExcelEdge_" + Guid.NewGuid().ToString("N"));
        string filePath = Path.Combine(tempDir, "edge.xlsx");

        try
        {
            var trades = new List<Trade>
            {
                new("sh.600000", TradeSide.Long, DateTime.MinValue, 10.0, DateTime.MinValue, 11.0, 100, 1.0, double.NaN, 0.1, 5, 0, 1, "t1", "t2")
            };
            var facts = new TradingResult(Array.Empty<SettlementRecord>(), trades, Array.Empty<PositionSnapshot>(), Array.Empty<EquityPoint>(), Array.Empty<DailySnapshot>(), Array.Empty<SignalRecord>());
            var horizon = new SignalHorizonStatistics(1, new SignalSampleStatistics(1, 1, 0.1, 0.1, 0.1, 0, 0), new SignalSampleStatistics(1, 1, 0.1, 0.1, 0.1, 0, 0));
            var grp = new SignalStatistics(1, 1, new[] { horizon }, 1, new Dictionary<string, SignalStatistics>());
            var sig = new SignalStatistics(1, 1, new[] { horizon }, 1, new Dictionary<string, SignalStatistics> { ["'quoted'"] = grp });

            var report = new AnalysisReport(facts, null, null, null, null, sig);
            ExcelReportExporter.Save(report, filePath);

            Assert.IsTrue(File.Exists(filePath));
            using var wb = new XLWorkbook(filePath);
            Assert.AreEqual("-", wb.Worksheet("Closed_Trades").Cell(2, 3).GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
