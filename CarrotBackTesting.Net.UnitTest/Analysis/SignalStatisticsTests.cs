using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Analysis.Analyzers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class SignalStatisticsTests
{
    [TestMethod]
    public void Create_EmptySignalsOrNullTimeseries_ReturnsNull()
    {
        var options = new AnalysisOptions();
        var res1 = SignalAnalyzer.Calculate(Array.Empty<SignalRecord>(), null, options);
        Assert.IsNull(res1);

        var res2 = SignalAnalyzer.Calculate(
            new[] { new SignalRecord(DateTime.Now, "sh.600000", 1.0, "GroupA", "") },
            null,
            options
        );
        Assert.IsNull(res2);
    }

    [TestMethod]
    public void Create_DeterministicSignals_ComputesHorizonReturnsAndPeakStep()
    {
        var dates = new List<DateTime>();
        var t0 = new DateTime(2024, 1, 1);
        for (int i = 0; i < 10; i++) dates.Add(t0.AddDays(i));

        var symbols = new[] { "sh.600000" };
        var metadata = new MockMetadata(symbols, dates);

        // Day 0: 10.0
        // Day 1: 11.0 (+10% from Day 0)
        // Day 2: 13.0 (+30% from Day 0) -> 涨幅峰值在 T+2
        // Day 3: 12.0 (+20% from Day 0)
        // Day 4: 10.0 ( 0% from Day 0)
        var prices = new double[10, 1]
        {
            { 10.0 }, { 11.0 }, { 13.0 }, { 12.0 }, { 10.0 },
            { 9.0 },  { 8.0 },  { 8.5 },  { 9.0 },  { 9.5 }
        };

        var timeseries = new MockTimeseriesRegistry(metadata);
        timeseries.SetMatrix("close", new MockMatrix(prices));

        var signal = new SignalRecord(dates[0], "sh.600000", 1.0, "Momentum", "Breakout");

        var options = new AnalysisOptions
        {
            SignalHorizonSteps = 4
        };

        var stats = SignalAnalyzer.Calculate(new[] { signal }, timeseries, options);

        Assert.IsNotNull(stats);
        Assert.AreEqual(1, stats.TotalSignals);
        Assert.AreEqual(4, stats.HorizonSteps);
        Assert.AreEqual(4, stats.Horizons.Count);

        // T+1: +10%
        Assert.AreEqual(0.10, stats.Horizons[0].SampleWeighted.AvgReturn, 1e-4);
        Assert.AreEqual(1.0, stats.Horizons[0].SampleWeighted.WinRate, 1e-4);

        // T+2: +30% (峰值)
        Assert.AreEqual(0.30, stats.Horizons[1].SampleWeighted.AvgReturn, 1e-4);

        // T+3: +20%
        Assert.AreEqual(0.20, stats.Horizons[2].SampleWeighted.AvgReturn, 1e-4);

        // T+4: 0%
        Assert.AreEqual(0.00, stats.Horizons[3].SampleWeighted.AvgReturn, 1e-4);

        // 验证 PeakHorizonStep 应自动识别为第 2 步 (T+2)
        Assert.AreEqual(2, stats.PeakHorizonStep);

        // 分组下钻检查
        Assert.IsTrue(stats.GroupBreakdowns.ContainsKey("Momentum"));
        var groupMetric = stats.GroupBreakdowns["Momentum"];
        Assert.AreEqual(2, groupMetric.PeakHorizonStep);
    }

    [TestMethod]
    public void Create_SignalsWithNaNAndMissingPrice_SkipsGracefully()
    {
        var dates = new List<DateTime>();
        var t0 = new DateTime(2024, 1, 1);
        for (int i = 0; i < 5; i++) dates.Add(t0.AddDays(i));

        var symbols = new[] { "sh.600000" };
        var metadata = new MockMetadata(symbols, dates);

        // Day 1 价格为 NaN
        var prices = new double[5, 1]
        {
            { 10.0 }, { double.NaN }, { 12.0 }, { 13.0 }, { 14.0 }
        };

        var timeseries = new MockTimeseriesRegistry(metadata);
        timeseries.SetMatrix("close", new MockMatrix(prices));

        var signal = new SignalRecord(dates[0], "sh.600000", 1.0, "TestGroup", "");
        var stats = SignalAnalyzer.Calculate(new[] { signal }, timeseries, new AnalysisOptions { SignalHorizonSteps = 3 });

        Assert.IsNotNull(stats);
        // T+1 (Day 1) 为 NaN，样本计数为 0
        Assert.AreEqual(0, stats.Horizons[0].SampleWeighted.SignalCount);
        // T+2 (Day 2) 价格为 12.0，收益率应为 20%
        Assert.AreEqual(1, stats.Horizons[1].SampleWeighted.SignalCount);
        Assert.AreEqual(0.20, stats.Horizons[1].SampleWeighted.AvgReturn, 1e-4);
    }

    [TestMethod]
    public void Create_MultiMonthSignals_VerifiesAllSampleWeightedAndTimeWeightedMetrics()
    {
        var dates = new List<DateTime>
        {
            new(2024, 1, 10),
            new(2024, 1, 11),
            new(2024, 1, 12),
            new(2024, 1, 20),
            new(2024, 1, 21),
            new(2024, 1, 22),
            new(2024, 2, 15),
            new(2024, 2, 16),
            new(2024, 2, 17),
            new(2024, 2, 18)
        };

        var symbols = new[] { "sh.600000" };
        var metadata = new MockMetadata(symbols, dates);

        var prices = new double[10, 1]
        {
            { 100.0 }, // Index 0: 2024-01-10 (Sig 1)
            { 110.0 }, // Index 1: +10% at T+1
            { 120.0 }, // Index 2: +20% at T+2
            { 100.0 }, // Index 3: 2024-01-20 (Sig 2)
            { 90.0 },  // Index 4: -10% at T+1
            { 80.0 },  // Index 5: -20% at T+2
            { 100.0 }, // Index 6: 2024-02-15 (Sig 3)
            { 130.0 }, // Index 7: +30% at T+1
            { 140.0 }, // Index 8: +40% at T+2
            { 150.0 }  // Index 9
        };

        var timeseries = new MockTimeseriesRegistry(metadata);
        timeseries.SetMatrix("close", new MockMatrix(prices));

        var sig1 = new SignalRecord(dates[0], "sh.600000", 1.0, "GroupA", "Reason1");
        var sig2 = new SignalRecord(dates[3], "sh.600000", 1.0, "GroupB", "Reason2");
        var sig3 = new SignalRecord(dates[6], "sh.600000", 1.0, "GroupA", "Reason3");

        var options = new AnalysisOptions
        {
            SignalHorizonSteps = 2
        };

        var stats = SignalAnalyzer.Calculate(new[] { sig1, sig2, sig3 }, timeseries, options);

        Assert.IsNotNull(stats);
        Assert.AreEqual(3, stats.TotalSignals);
        Assert.AreEqual(2, stats.HorizonSteps);
        Assert.AreEqual(2, stats.Horizons.Count);

        // ==========================================
        // 1. T+1 步全量指标显式断言 (Step 1)
        // ==========================================
        var h1 = stats.Horizons[0];
        Assert.AreEqual(1, h1.Step);

        // A. Sample-Weighted (样本等权): returns = [+0.10, -0.10, +0.30]
        var sw1 = h1.SampleWeighted;
        Assert.AreEqual(3, sw1.SignalCount);
        Assert.AreEqual(2.0 / 3.0, sw1.WinRate, 1e-4);
        Assert.AreEqual(0.10, sw1.AvgReturn, 1e-4);
        Assert.AreEqual(0.10, sw1.MedianReturn, 1e-4);
        Assert.AreEqual(0.20, sw1.AvgWin, 1e-4);
        Assert.AreEqual(-0.10, sw1.AvgLoss, 1e-4);
        Assert.AreEqual(2.0, sw1.WinLossRatio, 1e-4);

        // B. Time-Weighted (时间等权月度均化):
        // Jan: [+0.10, -0.10] -> WinRate=0.5, Avg=0.0, Median=0.0, AvgWin=0.10, AvgLoss=-0.10
        // Feb: [+0.30]        -> WinRate=1.0, Avg=0.3, Median=0.3, AvgWin=0.30, AvgLoss=0.0
        var tw1 = h1.TimeWeighted;
        Assert.AreEqual(3, tw1.SignalCount);
        Assert.AreEqual(0.75, tw1.WinRate, 1e-4);
        Assert.AreEqual(0.15, tw1.AvgReturn, 1e-4);
        Assert.AreEqual(0.15, tw1.MedianReturn, 1e-4);
        Assert.AreEqual(0.20, tw1.AvgWin, 1e-4);
        Assert.AreEqual(-0.10, tw1.AvgLoss, 1e-4);
        Assert.AreEqual(2.0, tw1.WinLossRatio, 1e-4);

        // ==========================================
        // 2. T+2 步全量指标显式断言 (Step 2)
        // returns = [+0.20, -0.20, +0.40]
        // ==========================================
        var h2 = stats.Horizons[1];
        Assert.AreEqual(2, h2.Step);
        Assert.AreEqual(0.40 / 3.0, h2.SampleWeighted.AvgReturn, 1e-4);

        // 峰值步数应为 T+2 (0.1333 > 0.10)
        Assert.AreEqual(2, stats.PeakHorizonStep);

        // ==========================================
        // 3. 分组下钻 (Group Breakdown)
        // ==========================================
        Assert.IsTrue(stats.GroupBreakdowns.ContainsKey("GroupA"));
        var groupA = stats.GroupBreakdowns["GroupA"];
        Assert.AreEqual(2, groupA.TotalSignals);
        Assert.AreEqual(0.20, groupA.Horizons[0].SampleWeighted.AvgReturn, 1e-4);

        Assert.IsTrue(stats.GroupBreakdowns.ContainsKey("GroupB"));
        var groupB = stats.GroupBreakdowns["GroupB"];
        Assert.AreEqual(1, groupB.TotalSignals);
        Assert.AreEqual(-0.10, groupB.Horizons[0].SampleWeighted.AvgReturn, 1e-4);
    }

    [TestMethod]
    public void Create_ShortSignals_ComputesExactSymmetricReturns()
    {
        var dates = new List<DateTime>();
        var t0 = new DateTime(2024, 1, 1);
        for (int i = 0; i < 5; i++) dates.Add(t0.AddDays(i));

        var symbols = new[] { "sh.600000" };
        var metadata = new MockMetadata(symbols, dates);

        // 标的价格下跌：100 -> 90 (-10%) -> 70 (-30%)
        var prices = new double[5, 1]
        {
            { 100.0 }, { 90.0 }, { 70.0 }, { 80.0 }, { 85.0 }
        };

        var timeseries = new MockTimeseriesRegistry(metadata);
        timeseries.SetMatrix("close", new MockMatrix(prices));

        // 做空信号 SignalValue = -1.0
        var signal = new SignalRecord(dates[0], "sh.600000", -1.0, "ShortAlpha", "Breakdown");

        var options = new AnalysisOptions { SignalHorizonSteps = 2 };
        var stats = SignalAnalyzer.Calculate(new[] { signal }, timeseries, options);

        Assert.IsNotNull(stats);
        Assert.AreEqual(1, stats.TotalSignals);

        // T+1: 标的跌 10%，做空信号前向收益应为 +10%
        Assert.AreEqual(0.10, stats.Horizons[0].SampleWeighted.AvgReturn, 1e-4);
        Assert.AreEqual(1.0, stats.Horizons[0].SampleWeighted.WinRate, 1e-4);

        // T+2: 标的跌 30%，做空信号前向收益应为 +30%
        Assert.AreEqual(0.30, stats.Horizons[1].SampleWeighted.AvgReturn, 1e-4);
        Assert.AreEqual(1.0, stats.Horizons[1].SampleWeighted.WinRate, 1e-4);
        Assert.AreEqual(2, stats.PeakHorizonStep);
    }

    [TestMethod]
    public void Create_TimeWeighted_WithZeroWinOrZeroLossMonths_HandlesGracefully()
    {
        var dates = new List<DateTime>
        {
            new(2024, 1, 1),
            new(2024, 1, 2),
            new(2024, 2, 1),
            new(2024, 2, 2)
        };
        var symbols = new[] { "sh.600000" };
        var metadata = new MockMetadata(symbols, dates);

        // 1月: 100 -> 110 (+10% 全胜，无亏损)
        // 2月: 100 -> 90  (-10% 全败，无盈利)
        var prices = new double[4, 1]
        {
            { 100.0 }, { 110.0 }, { 100.0 }, { 90.0 }
        };

        var timeseries = new MockTimeseriesRegistry(metadata);
        timeseries.SetMatrix("close", new MockMatrix(prices));

        var sig1 = new SignalRecord(dates[0], "sh.600000", 1.0, "G1", "");
        var sig2 = new SignalRecord(dates[2], "sh.600000", 1.0, "G1", "");

        var options = new AnalysisOptions { SignalHorizonSteps = 1 };
        var stats = SignalAnalyzer.Calculate(new[] { sig1, sig2 }, timeseries, options);

        Assert.IsNotNull(stats);
        var tw = stats.Horizons[0].TimeWeighted;

        // 1月 WinRate=1.0, AvgWin=0.10, AvgLoss=0.0
        // 2月 WinRate=0.0, AvgWin=0.0, AvgLoss=-0.10
        // TimeWeighted: WinRate = (1.0 + 0.0)/2 = 0.50
        Assert.AreEqual(0.50, tw.WinRate, 1e-4);
        Assert.AreEqual(0.00, tw.AvgReturn, 1e-4);
        Assert.AreEqual(0.10, tw.AvgWin, 1e-4);
        Assert.AreEqual(-0.10, tw.AvgLoss, 1e-4);
        Assert.AreEqual(1.0, tw.WinLossRatio, 1e-4);
    }
}
