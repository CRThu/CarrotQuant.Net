using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.CLI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class ReportViewerServerTests
{
    [TestMethod]
    public async Task Server_Endpoints_ServeSummaryPaginatedTradesAndKlineWithSettlements()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CarrotViewerTest_" + Guid.NewGuid().ToString("N"));
        string dailyDir = Path.Combine(tempDir, "daily");
        Directory.CreateDirectory(dailyDir);

        try
        {
            // 构造标准 CSV 行情文件
            File.WriteAllText(Path.Combine(dailyDir, "sh.600000.csv"),
                "date,open,high,low,close,volume\n" +
                "2024-01-02,10.0,10.5,9.8,10.2,10000\n" +
                "2024-01-03,10.2,10.8,10.1,10.7,12000\n" +
                "2024-01-04,10.7,11.2,10.6,11.0,15000\n");

            var t0 = new DateTime(2024, 1, 2);
            var t1 = new DateTime(2024, 1, 4);

            var settlements = new List<SettlementRecord>
            {
                new(t0, "sh.600000", 1, OrderDirection.Buy, 10.1, 1000, 10100, 5, 0, 89900, "entry"),
                new(t1, "sh.600000", 2, OrderDirection.Sell, 11.0, 1000, 11000, 5, 890, 100890, "exit")
            };
            var trades = new List<Trade>
            {
                new("sh.600000", TradeSide.Long, t0, 10.1, t1, 11.0, 1000, 1.0, 890, 0.088, 10, 0, 2, "entry", "exit"),
                new("sz.000001", TradeSide.Long, t0, 20.0, t1, 19.0, 500, 1.0, -505, -0.0505, 5, 0, 2, "beta", "sl")
            };
            var equity = new List<EquityPoint>
            {
                new(t0, 100000, 89900, 0, 10100, 10100, 0),
                new(t1, 100385, 100385, 0, 0, 0, 0)
            };

            var facts = new TradingResult(settlements, trades, Array.Empty<PositionSnapshot>(), equity, Array.Empty<DailySnapshot>(), Array.Empty<SignalRecord>());
            var report = new AnalysisReport(facts, Array.Empty<IAnalysisResult>());

            await using var server = new ReportViewerServer(report, tempDir, "daily", "Viewer Test");
            await server.StartAsync();

            using var http = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

            // 1. HTML Shell
            string shell = await http.GetStringAsync("/");
            StringAssert.Contains(shell, "\"serverMode\":true");
            StringAssert.Contains(shell, "tab-btn-kline");

            // 2. Summary
            string summaryJson = await http.GetStringAsync("/api/summary");
            using (var doc = JsonDocument.Parse(summaryJson))
            {
                Assert.IsTrue(doc.RootElement.GetProperty("serverMode").GetBoolean());
                Assert.AreEqual(2, doc.RootElement.GetProperty("totalTrades").GetInt32());
                Assert.AreEqual(0, doc.RootElement.GetProperty("tradeRows").GetArrayLength());
            }

            // 3. Paginated & Filtered Trades
            string tradesJson = await http.GetStringAsync("/api/trades?page=1&size=1&q=sh.600000&sort=pnl_desc");
            using (var doc = JsonDocument.Parse(tradesJson))
            {
                Assert.AreEqual(1, doc.RootElement.GetProperty("total").GetInt32());
                var items = doc.RootElement.GetProperty("items");
                Assert.AreEqual(1, items.GetArrayLength());
                Assert.AreEqual("sh.600000", items[0].GetProperty("symbol").GetString());
            }

            // 4. Symbols
            string symbolsJson = await http.GetStringAsync("/api/symbols");
            using (var doc = JsonDocument.Parse(symbolsJson))
            {
                Assert.IsTrue(doc.RootElement.GetProperty("hasDataSource").GetBoolean());
                Assert.AreEqual(2, doc.RootElement.GetProperty("symbols").GetArrayLength());
            }

            // 5. K-line + Settlements
            string klineJson = await http.GetStringAsync("/api/kline?symbol=sh.600000");
            using (var doc = JsonDocument.Parse(klineJson))
            {
                Assert.IsTrue(doc.RootElement.GetProperty("hasDataSource").GetBoolean());
                Assert.AreEqual(3, doc.RootElement.GetProperty("dates").GetArrayLength());
                Assert.AreEqual(3, doc.RootElement.GetProperty("ohlcv").GetArrayLength());
                Assert.AreEqual(2, doc.RootElement.GetProperty("settlements").GetArrayLength());
                Assert.AreEqual("B", doc.RootElement.GetProperty("settlements")[0].GetProperty("direction").GetString());
            }

            // 6. Missing symbol query returns 400
            var badResp = await http.GetAsync("/api/kline");
            Assert.AreEqual(HttpStatusCode.BadRequest, badResp.StatusCode);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CliRunner_ViewMissingReportFile_ReturnsInvalidArgs()
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();
        int code = CliRunner.Run(new[] { "view", "nonexistent_report_12345.json" }, swOut, swErr);
        Assert.AreEqual(CliRunner.ExitInvalidArgs, code);
        StringAssert.Contains(swErr.ToString(), "不存在");
    }
}
