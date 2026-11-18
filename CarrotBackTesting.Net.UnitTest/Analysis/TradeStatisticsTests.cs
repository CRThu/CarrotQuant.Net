using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class TradeStatisticsTests
{
    [TestMethod]
    public void Create_EmptyTrades_ReturnsEmptyMetrics()
    {
        var options = new AnalysisOptions();
        var trades = TradeStatistics.Create(
            Array.Empty<Trade>(),
            options
        );
        var cost = CostStatistics.Create(
            Array.Empty<SettlementRecord>(),
            Array.Empty<Trade>()
        );

        Assert.AreEqual(0, trades.TotalTrades);
        Assert.AreEqual(0.0, trades.WinRate);
        Assert.AreEqual(0.0, trades.ProfitFactor);
        Assert.AreEqual(0.0, cost.TotalFrictionCost);
    }

    [TestMethod]
    public void Create_DeterministicTrades_ComputesCorrectMetricsAndTagBreakdowns()
    {
        var t0 = new DateTime(2024, 1, 1);
        var trades = new List<Trade>
        {
            // 多头 1: 盈利 200, 收益 20%, 步数 5, 标签 Alpha
            new("sh.600000", TradeSide.Long, t0, 10.0, t0.AddDays(5), 12.0, 100m, 1.0, 200.0, 0.20, 5.0, 1.0, 5, "Alpha", "Exit_Target"),
            // 多头 2: 盈利 100, 收益 10%, 步数 3, 标签 Alpha
            new("sh.600000", TradeSide.Long, t0.AddDays(6), 10.0, t0.AddDays(9), 11.0, 100m, 1.0, 100.0, 0.10, 5.0, 1.0, 3, "Alpha", "Exit_Target"),
            // 多头 3: 亏损 -100, 收益 -10%, 步数 2, 标签 Beta
            new("sh.600000", TradeSide.Long, t0.AddDays(10), 10.0, t0.AddDays(12), 9.0, 100m, 1.0, -100.0, -0.10, 5.0, 1.0, 2, "Beta", "Exit_StopLoss"),
            // 多头 4: 盈利 300, 收益 30%, 步数 10, 标签 Alpha
            new("sh.600000", TradeSide.Long, t0.AddDays(13), 10.0, t0.AddDays(23), 13.0, 100m, 1.0, 300.0, 0.30, 5.0, 1.0, 10, "Alpha", "Exit_Target"),
            // 空头 1: 盈利 200, 收益 10%, 步数 4, 标签 Short_Trend
            new("sh.600001", TradeSide.Short, t0.AddDays(1), 20.0, t0.AddDays(5), 18.0, 100m, 1.0, 200.0, 0.10, 5.0, 1.0, 4, "Short_Trend", "Exit_Target"),
            // 空头 2: 亏损 -200, 收益 -10%, 步数 6, 标签 Short_Trend
            new("sh.600001", TradeSide.Short, t0.AddDays(6), 20.0, t0.AddDays(12), 22.0, 100m, 1.0, -200.0, -0.10, 5.0, 1.0, 6, "Short_Trend", "Exit_StopLoss")
        };

        var settlements = new List<SettlementRecord>
        {
            new(t0, "sh.600000", "ord-1", OrderDirection.Buy, 10.0, 100m, 1000.0, 10.0, 0.0, 99000.0, ""),
            new(t0.AddDays(5), "sh.600000", "ord-2", OrderDirection.Sell, 12.0, 100m, 1200.0, 10.0, 200.0, 100200.0, "")
        };

        var options = new AnalysisOptions();
        var stats = TradeStatistics.Create(trades, options);
        var cost = CostStatistics.Create(settlements, trades);

        // 1. 基础笔数与胜率
        Assert.AreEqual(6, stats.TotalTrades);
        Assert.AreEqual(4, stats.WinningTrades);
        Assert.AreEqual(2, stats.LosingTrades);
        Assert.AreEqual(0, stats.BreakevenTrades);
        Assert.AreEqual(4.0 / 6.0, stats.WinRate, 1e-4);

        // 2. 多空拆分
        Assert.AreEqual(4, stats.LongTrades);
        Assert.AreEqual(3, stats.LongWinningTrades);
        Assert.AreEqual(0.75, stats.LongWinRate, 1e-4);

        Assert.AreEqual(2, stats.ShortTrades);
        Assert.AreEqual(1, stats.ShortWinningTrades);
        Assert.AreEqual(0.50, stats.ShortWinRate, 1e-4);

        // 3. 收益率分析
        Assert.AreEqual(0.50 / 6.0, stats.AverageReturn, 1e-4);
        Assert.AreEqual(0.10, stats.MedianReturn, 1e-4);
        Assert.AreEqual(0.175, stats.AverageWinReturn, 1e-4);
        Assert.AreEqual(-0.10, stats.AverageLossReturn, 1e-4);

        // 4. 盈利因子与盈亏比
        Assert.AreEqual(800.0 / 300.0, stats.ProfitFactor, 1e-4);
        Assert.AreEqual(1.75, stats.WinLossRatio, 1e-4);

        // 5. 连续盈亏统计
        Assert.AreEqual(2, stats.MaxConsecutiveWins);
        Assert.AreEqual(1, stats.MaxConsecutiveLosses);

        // 6. 持仓步数统计
        Assert.AreEqual(5.0, stats.AverageHoldingSteps, 1e-4);
        Assert.AreEqual(5.5, stats.AverageWinHoldingSteps, 1e-4);
        Assert.AreEqual(4.0, stats.AverageLossHoldingSteps, 1e-4);

        // 7. 标签分组切片：Alpha 组有 3 笔且全部盈利
        Assert.IsTrue(stats.TagBreakdowns.ContainsKey("Alpha"));
        var alphaStats = stats.TagBreakdowns["Alpha"];
        Assert.AreEqual(3, alphaStats.TotalTrades);
        Assert.AreEqual(3, alphaStats.WinningTrades);
        Assert.AreEqual(1.0, alphaStats.WinRate, 1e-4);

        // 8. 摩擦成本: 佣金 20, 滑点 6
        Assert.AreEqual(20.0, cost.TotalCommission, 1e-4);
        Assert.AreEqual(6.0, cost.TotalSlippage, 1e-4);
        Assert.AreEqual(26.0, cost.TotalFrictionCost, 1e-4);
        Assert.AreEqual(26.0 / 800.0, cost.FeeToGrossProfitRatio, 1e-4);
        Assert.AreEqual(26.0 / 6.0, cost.AverageFrictionPerTrade, 1e-4);
    }

    [TestMethod]
    public void Create_AllWinningTrades_WinLossRatioAndProfitFactorCapAtSentinel()
    {
        var t0 = new DateTime(2024, 1, 1);
        var trades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, t0, 10.0, t0.AddDays(1), 11.0, 100m, 1.0, 100.0, 0.10, 5.0, 0.0, 1, "", ""),
            new("sh.600000", TradeSide.Long, t0.AddDays(2), 10.0, t0.AddDays(3), 12.0, 100m, 1.0, 200.0, 0.20, 5.0, 0.0, 1, "", "")
        };

        var stats = TradeStatistics.Create(trades, new AnalysisOptions());

        Assert.AreEqual(1.0, stats.WinRate);
        Assert.AreEqual(999.0, stats.WinLossRatio);
        Assert.AreEqual(999.0, stats.ProfitFactor);
    }

    [TestMethod]
    public void Create_AllLosingTrades_WinLossRatioAndProfitFactorZero()
    {
        var t0 = new DateTime(2024, 1, 1);
        var trades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, t0, 10.0, t0.AddDays(1), 9.0, 100m, 1.0, -100.0, -0.10, 5.0, 0.0, 1, "", "")
        };

        var stats = TradeStatistics.Create(trades, new AnalysisOptions());

        Assert.AreEqual(0.0, stats.WinRate);
        Assert.AreEqual(0.0, stats.WinLossRatio);
        Assert.AreEqual(0.0, stats.ProfitFactor);
    }

    [TestMethod]
    public void Create_ShortTradeMfe_ComputesExactEfficiency()
    {
        var dates = new List<DateTime>();
        for (int i = 0; i < 10; i++) dates.Add(new DateTime(2024, 1, 1).AddDays(i));
        var symbols = new[] { "sh.600000" };
        var metadata = new MockMetadata(symbols, dates);

        // 标的价格下跌：100 -> 90 -> 80 -> 85 -> 95
        // 空头入场：Day 0 @ 100
        // 区间最低价：Day 2 @ 80（潜在最大跌幅/收益 = 100 - 80 = 20）
        // 平仓：Day 3 @ 85（实际盈利 = 100 - 85 = 15）
        // 捕获效率 = 15 / 20 = 75.0%
        var prices = new double[10, 1]
        {
            { 100.0 }, { 90.0 }, { 80.0 }, { 85.0 }, { 95.0 },
            { 96.0 },  { 97.0 }, { 98.0 }, { 99.0 }, { 100.0 }
        };

        var timeseries = new MockTimeseriesRegistry(metadata);
        timeseries.SetMatrix("close", new MockMatrix(prices));

        var trade = new Trade(
            "sh.600000",
            TradeSide.Short,
            dates[0],
            100.0,
            dates[3],
            85.0,
            10m,
            1.0,
            150.0,
            0.15,
            5.0,
            0.0,
            3,
            "ShortAlpha",
            ""
        );

        var stats = TradeStatistics.Create(new[] { trade }, new AnalysisOptions(), timeseries);

        Assert.IsNotNull(stats.AverageTradeEfficiency);
        Assert.AreEqual(0.75, stats.AverageTradeEfficiency.Value, 1e-4);
    }

    [TestMethod]
    public void Create_BreakevenTrade_ResetsConsecutiveStreaks()
    {
        var t0 = new DateTime(2024, 1, 1);
        var trades = new List<Trade>
        {
            new("sh.600000", TradeSide.Long, t0, 10.0, t0.AddDays(1), 11.0, 100m, 1.0, 100.0, 0.10, 0, 0, 1, "", ""),
            new("sh.600000", TradeSide.Long, t0.AddDays(1), 10.0, t0.AddDays(2), 10.0, 100m, 1.0, 0.0, 0.0, 0, 0, 1, "", ""), // 保本平仓
            new("sh.600000", TradeSide.Long, t0.AddDays(2), 10.0, t0.AddDays(3), 11.0, 100m, 1.0, 100.0, 0.10, 0, 0, 1, "", "")
        };

        var stats = TradeStatistics.Create(trades, new AnalysisOptions());

        Assert.AreEqual(1, stats.BreakevenTrades);
        // 中间被打断，最大连续盈利应为 1
        Assert.AreEqual(1, stats.MaxConsecutiveWins);
    }

    [TestMethod]
    public void Create_ExitTiming_ComprehensiveVerification_AllPropertiesAsserted()
    {
        var dates = new List<DateTime>();
        for (int i = 0; i < 10; i++) dates.Add(new DateTime(2024, 1, 1).AddDays(i));
        var symbols = new[] { "sh.600000" };
        var metadata = new MockMetadata(symbols, dates);

        // 价格线性上涨：10, 11, 12, 13, 14, 15, 16, 17, 18, 19
        var prices = new double[10, 1];
        for (int i = 0; i < 10; i++) prices[i, 0] = 10.0 + i;

        var timeseries = new MockTimeseriesRegistry(metadata);
        timeseries.SetMatrix("close", new MockMatrix(prices));

        // Trade 1: 平仓于 Day 1 @ 11.0 (有后续 Day 2, 3, 4 行情)
        var trade1 = new Trade("sh.600000", TradeSide.Long, dates[0], 10.0, dates[1], 11.0, 100m, 1.0, 100.0, 0.10, 0, 0, 1, "", "");
        // Trade 2: 平仓于 Day 3 @ 13.0 (有后续 Day 4, 5, 6 行情)
        var trade2 = new Trade("sh.600000", TradeSide.Long, dates[2], 12.0, dates[3], 13.0, 100m, 1.0, 100.0, 0.0833, 0, 0, 1, "", "");
        // Trade 3: 平仓于 Day 8 @ 18.0 (后续不足 3 步，将被判定为无效样本)
        var trade3 = new Trade("sh.600000", TradeSide.Long, dates[7], 17.0, dates[8], 18.0, 100m, 1.0, 100.0, 0.0588, 0, 0, 1, "", "");

        var options = new AnalysisOptions { ExitTimingSteps = 3 };
        var stats = TradeStatistics.Create(new[] { trade1, trade2, trade3 }, options, timeseries);

        Assert.IsNotNull(stats.ExitTiming);
        var et = stats.ExitTiming;

        Assert.AreEqual(3, et.HorizonSteps);
        Assert.AreEqual(2, et.ValidTradesCount);
        Assert.AreEqual(3, et.AverageReturns.Count);
        Assert.AreEqual(3, et.MedianReturns.Count);
        Assert.AreEqual(3, et.WinRates.Count);

        // T+1: (1/11 + 1/13)/2 = 0.083916
        double expectedAvgT1 = (1.0 / 11.0 + 1.0 / 13.0) / 2.0;
        Assert.AreEqual(expectedAvgT1, et.AverageReturns[0], 1e-4);
        Assert.AreEqual(expectedAvgT1, et.MedianReturns[0], 1e-4);
        Assert.AreEqual(1.0, et.WinRates[0], 1e-4);

        // T+2: (2/11 + 2/13)/2 = 0.167832
        double expectedAvgT2 = (2.0 / 11.0 + 2.0 / 13.0) / 2.0;
        Assert.AreEqual(expectedAvgT2, et.AverageReturns[1], 1e-4);
        Assert.AreEqual(expectedAvgT2, et.MedianReturns[1], 1e-4);
        Assert.AreEqual(1.0, et.WinRates[1], 1e-4);

        // T+3: (3/11 + 3/13)/2 = 0.251748
        double expectedAvgT3 = (3.0 / 11.0 + 3.0 / 13.0) / 2.0;
        Assert.AreEqual(expectedAvgT3, et.AverageReturns[2], 1e-4);
        Assert.AreEqual(expectedAvgT3, et.MedianReturns[2], 1e-4);
        Assert.AreEqual(1.0, et.WinRates[2], 1e-4);
    }

    [TestMethod]
    public void Create_TradeEfficiency_LongAndShort_OddAndEvenMedians()
    {
        var dates = new List<DateTime>();
        for (int i = 0; i < 10; i++) dates.Add(new DateTime(2024, 1, 1).AddDays(i));
        var symbols = new[] { "sh.600000", "sz.000001" };
        var metadata = new MockMetadata(symbols, dates);

        // sh.600000 (多头): Day 0 @ 10, Day 1 @ 12, Day 2 @ 19, Day 3 @ 20 (最高), Day 4 @ 15
        // sz.000001 (空头): Day 0 @ 100, Day 1 @ 80, Day 2 @ 60 (最低), Day 3 @ 70
        var prices = new double[10, 2]
        {
            { 10.0, 100.0 },
            { 12.0, 80.0 },
            { 19.0, 60.0 },
            { 20.0, 70.0 },
            { 15.0, 80.0 },
            { 16.0, 85.0 },
            { 17.0, 90.0 },
            { 18.0, 95.0 },
            { 19.0, 100.0 },
            { 20.0, 105.0 }
        };

        var timeseries = new MockTimeseriesRegistry(metadata);
        timeseries.SetMatrix("close", new MockMatrix(prices));

        // 1. Long 交易: Entry Day 0 @ 10, Exit Day 4 @ 15, Max Price = 20
        // Potential = (20 - 10) * 10 = 100, Realized = (15 - 10) * 10 = 50 -> eff = 0.50
        var tLong = new Trade("sh.600000", TradeSide.Long, dates[0], 10.0, dates[4], 15.0, 10m, 1.0, 50.0, 0.50, 0, 0, 4, "", "");

        // 2. Short 交易: Entry Day 0 @ 100, Exit Day 3 @ 70, Min Price = 60
        // Potential = (100 - 60) * 10 = 400, Realized = (100 - 70) * 10 = 300 -> eff = 0.75
        var tShort = new Trade("sz.000001", TradeSide.Short, dates[0], 100.0, dates[3], 70.0, 10m, 1.0, 300.0, 0.30, 0, 0, 3, "", "");

        // 两个样本：偶数中位数分支
        var stats2 = TradeStatistics.Create(new[] { tLong, tShort }, new AnalysisOptions(), timeseries);
        Assert.IsNotNull(stats2.AverageTradeEfficiency);
        Assert.IsNotNull(stats2.MedianTradeEfficiency);
        Assert.AreEqual((0.50 + 0.75) / 2.0, stats2.AverageTradeEfficiency.Value, 1e-4);
        Assert.AreEqual((0.50 + 0.75) / 2.0, stats2.MedianTradeEfficiency.Value, 1e-4);

        // 3. 增加第 3 笔 Long 交易: Entry Day 1 @ 12, Exit Day 2 @ 19, 区间最高 19.0 (Day 2)
        // Potential = (19 - 12) * 10 = 70, Realized = 70 -> eff = 1.00
        var tLong2 = new Trade("sh.600000", TradeSide.Long, dates[1], 12.0, dates[2], 19.0, 10m, 1.0, 70.0, 0.5833, 0, 0, 1, "", "");

        // 三个样本：奇数中位数分支 (0.50, 0.75, 1.00) -> 中位数为 0.75
        var stats3 = TradeStatistics.Create(new[] { tLong, tShort, tLong2 }, new AnalysisOptions(), timeseries);
        Assert.IsNotNull(stats3.AverageTradeEfficiency);
        Assert.IsNotNull(stats3.MedianTradeEfficiency);
        Assert.AreEqual((0.50 + 0.75 + 1.00) / 3.0, stats3.AverageTradeEfficiency.Value, 1e-4);
        Assert.AreEqual(0.75, stats3.MedianTradeEfficiency.Value, 1e-4);
    }

    [TestMethod]
    public void Create_MaxConsecutiveStreaks_ExplicitVerification()
    {
        var t0 = new DateTime(2024, 1, 1);
        var trades = new List<Trade>
        {
            // 3 连亏
            new("s", TradeSide.Long, t0, 10, t0, 9, 1m, 1, -10.0, -0.1, 0, 0, 1, "", ""),
            new("s", TradeSide.Long, t0, 10, t0, 9, 1m, 1, -20.0, -0.1, 0, 0, 1, "", ""),
            new("s", TradeSide.Long, t0, 10, t0, 9, 1m, 1, -30.0, -0.1, 0, 0, 1, "", ""),
            // 4 连胜
            new("s", TradeSide.Long, t0, 10, t0, 11, 1m, 1, 10.0, 0.1, 0, 0, 1, "", ""),
            new("s", TradeSide.Long, t0, 10, t0, 11, 1m, 1, 20.0, 0.1, 0, 0, 1, "", ""),
            new("s", TradeSide.Long, t0, 10, t0, 11, 1m, 1, 30.0, 0.1, 0, 0, 1, "", ""),
            new("s", TradeSide.Long, t0, 10, t0, 11, 1m, 1, 40.0, 0.1, 0, 0, 1, "", ""),
            // 保本打断
            new("s", TradeSide.Long, t0, 10, t0, 10, 1m, 1, 0.0, 0.0, 0, 0, 1, "", ""),
            // 1 胜
            new("s", TradeSide.Long, t0, 10, t0, 11, 1m, 1, 15.0, 0.1, 0, 0, 1, "", ""),
            // 2 连亏
            new("s", TradeSide.Long, t0, 10, t0, 9, 1m, 1, -5.0, -0.1, 0, 0, 1, "", ""),
            new("s", TradeSide.Long, t0, 10, t0, 9, 1m, 1, -15.0, -0.1, 0, 0, 1, "", "")
        };

        var stats = TradeStatistics.Create(trades, new AnalysisOptions());

        Assert.AreEqual(11, stats.TotalTrades);
        Assert.AreEqual(5, stats.WinningTrades);
        Assert.AreEqual(5, stats.LosingTrades);
        Assert.AreEqual(1, stats.BreakevenTrades);
        Assert.AreEqual(4, stats.MaxConsecutiveWins);
        Assert.AreEqual(3, stats.MaxConsecutiveLosses);
    }
}
