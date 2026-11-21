using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Analysis.Analyzers;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class AnalysisHelperAndSlippageTests
{
    [TestMethod]
    public void AnalysisHelper_CalculateMedian_ShouldCalculateAccurately()
    {
        // 1. 空切片
        Assert.AreEqual(0.0, AnalysisHelper.CalculateMedian(Array.Empty<double>()));

        // 2. 单元素
        Assert.AreEqual(5.0, AnalysisHelper.CalculateMedian(new[] { 5.0 }));

        // 3. 奇数个元素
        var odd = new[] { 1.0, 3.0, 7.0 };
        Assert.AreEqual(3.0, AnalysisHelper.CalculateMedian(odd));

        // 4. 偶数个元素
        var even = new[] { 1.0, 3.0, 5.0, 9.0 };
        Assert.AreEqual(4.0, AnalysisHelper.CalculateMedian(even));

        // 5. List<double> 重载
        var list = new List<double> { 2.0, 4.0, 6.0 };
        Assert.AreEqual(4.0, AnalysisHelper.CalculateMedian(list));
    }

    [TestMethod]
    public void AnalysisHelper_ResolveDateIndex_ShouldSupportExactAndDateFallback()
    {
        var dates = new[]
        {
            new MarketTime(2025, 1, 2, 9, 30, 0),
            new MarketTime(2025, 1, 3, 9, 30, 0)
        };
        var meta = new DynamicMarketMetadata(new[] { "sh.600000" }, dates);

        // 1. 精确匹配
        int idxExact = AnalysisHelper.ResolveDateIndex(meta, new MarketTime(2025, 1, 2, 9, 30, 0));
        Assert.AreEqual(0, idxExact);

        // 2. 纯日期回退匹配
        int idxDateOnly = AnalysisHelper.ResolveDateIndex(meta, new MarketTime(2025, 1, 3, 0, 0, 0));
        Assert.AreEqual(1, idxDateOnly);

        // 3. 不存在日期返回负数
        int idxNotFound = AnalysisHelper.ResolveDateIndex(meta, new MarketTime(2025, 1, 10));
        Assert.IsTrue(idxNotFound < 0);
    }

    [TestMethod]
    public void AccountLedger_CommitFill_ShouldTrackSlippageCostAndHoldingStepsAccurately()
    {
        var ledger = new AccountLedger(new BrokerConfig { InitialCash = 100_000.0 });
        var t0 = new MarketTime(2025, 1, 2, 9, 30, 0);

        // 1. 开仓 100 股，基准价 10.0，成交价 10.1 (每股滑点 0.1，总滑点 10.0 元)，手续费 5.0 元
        var openReport = new ExecutionReport(
            OrderId: 1L,
            Symbol: "sh.600000",
            FillPrice: 10.1,
            FillQuantity: 100m,
            Commission: 5.0,
            Time: t0,
            Role: LiquidityRole.Taker,
            Slippage: 10.0
        );

        ledger.CommitFill(
            openReport,
            isBuy: true,
            closeQty: 0m,
            openQty: 100m,
            multiplier: 1.0,
            marginRate: 1.0,
            settleDays: 0,
            unfreezeCash: 1015.0,
            unfreezePosition: 0m,
            exchangeDate: t0,
            orderTag: "OpenTag",
            out var posEvent1,
            out var closedTrade1,
            out var settlement1);

        Assert.IsNull(closedTrade1, "开仓不应产生成交配对 Trade");
        Assert.AreEqual(100m, ledger.GetPosition("sh.600000"));

        // 2. 跨越 3 天后平仓 100 股，基准价 12.0，成交价 11.9 (每股滑点 0.1，总滑点 10.0 元)，手续费 6.0 元
        var tClose = new MarketTime(2025, 1, 5, 15, 0, 0);
        var closeReport = new ExecutionReport(
            OrderId: 2L,
            Symbol: "sh.600000",
            FillPrice: 11.9,
            FillQuantity: 100m,
            Commission: 6.0,
            Time: tClose,
            Role: LiquidityRole.Taker,
            Slippage: 10.0
        );

        ledger.CommitFill(
            closeReport,
            isBuy: false,
            closeQty: 100m,
            openQty: 0m,
            multiplier: 1.0,
            marginRate: 1.0,
            settleDays: 0,
            unfreezeCash: 0.0,
            unfreezePosition: 100m,
            exchangeDate: tClose,
            orderTag: "ExitTag",
            out var posEvent2,
            out var closedTrade2,
            out var settlement2);

        Assert.IsNotNull(closedTrade2, "完全平仓必须生成配对 Trade");
        // 验证：滑点开平仓损耗累加 = 10.0 + 10.0 = 20.0
        Assert.AreEqual(20.0, closedTrade2.SlippageCost, 1e-4, "Trade 滑点损耗必须等于开平仓滑点总和");
        // 验证：手续费累加 = 5.0 + 6.0 = 11.0
        Assert.AreEqual(11.0, closedTrade2.Commission, 1e-4, "Trade 手续费必须等于开平仓手续费总和");
        // 验证：跨 3 天持仓，HoldingSteps 必须精准等于 3 步，绝非 4320 分钟！
        Assert.AreEqual(3, closedTrade2.HoldingSteps, "跨 3 天平仓持仓步数必须为 3 步");
        Assert.AreEqual("OpenTag", closedTrade2.EntryTag);
        Assert.AreEqual("ExitTag", closedTrade2.ExitTag);

        // 验证第四层 CostAnalyzer 真实汇总滑点
        var costStats = CostAnalyzer.Calculate(new[] { settlement1, settlement2 }, new[] { closedTrade2 });
        Assert.AreEqual(20.0, costStats.TotalSlippage, 1e-4, "CostStatistics.TotalSlippage 必须真实统计到 20.0 滑点损耗");
    }

    [TestMethod]
    public void OrderBook_CancelDayOrders_ShouldCancelTriggeredStopLimitDayOrders_AndPreserveMarketOrders()
    {
        var book = new OrderBook();
        var t0 = new MarketTime(2025, 1, 2, 10, 0, 0);

        // 1. 触发后排队的 StopLimit 日内单 (Day) -> 应被日切撤销
        var stopLimitDay = new SimulatedOrder(
            1L,
            new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.StopLimit, 10.0, 100m, "", TimeInForce.Day) { StopPrice = 10.5 },
            t0);

        // 2. 普通 Limit 跨日单 (GoodTilCanceled) -> 必须跨日保留
        var limitGtc = new SimulatedOrder(
            2L,
            new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 100m, "", TimeInForce.GoodTilCanceled),
            t0);

        // 3. 收盘提交的跨步市价单 (Market, Day) -> 必须跨日保留留待次日开盘撮合
        var marketNextDay = new SimulatedOrder(
            3L,
            new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 0.0, 100m, "", TimeInForce.Day),
            t0);

        book.Add(stopLimitDay);
        book.Add(limitGtc);
        book.Add(marketNextDay);

        Assert.AreEqual(3, book.Count);

        var canceled = book.CancelDayOrders();

        Assert.AreEqual(1, canceled.Count, "应且仅应撤销 1 笔 StopLimit + Day 订单");
        Assert.AreEqual(1L, canceled[0].OrderId);

        Assert.AreEqual(2, book.Count, "挂单簿中应保留 GTC 订单与跨步市价单");
        Assert.IsTrue(book.TryGet(2L, out _), "GTC 限价单应保留");
        Assert.IsTrue(book.TryGet(3L, out _), "跨步市价单应保留留待开盘撮合");
    }
}
