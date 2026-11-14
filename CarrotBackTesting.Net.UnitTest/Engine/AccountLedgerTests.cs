using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine.Broker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class AccountLedgerTests
{
    [TestMethod]
    public void InitialState_ShouldMatchConfig()
    {
        var config = new BrokerConfig { InitialCash = 500_000.0 };
        var ledger = new AccountLedger(config);

        Assert.AreEqual(500_000.0, ledger.Cash);
        Assert.AreEqual(0.0, ledger.FrozenCash);
        Assert.AreEqual(500_000.0, ledger.AvailableCash);
        Assert.AreEqual(500_000.0, ledger.GetTotalEquity());
        Assert.AreEqual(0m, ledger.GetPosition("sh.600000"));
        Assert.AreEqual(0m, ledger.GetAvailablePosition("sh.600000"));
    }

    [TestMethod]
    public void SpotSell_ZeroCash_ShouldNotBeBlocked()
    {
        // 核心测试：复现并验证已修复历史 Bug（现金为0时卖出股票必须成功冻结持仓，绝不因缺少现金被拒单）
        var config = new BrokerConfig
        {
            InitialCash = 0.0, // 现金完全为 0！
            AllowShort = false,
            SettleDays = 0
        };
        var ledger = new AccountLedger(config);

        // 先通过对账同步注入 1000 股现货
        ledger.SyncState(0.0, new Dictionary<string, decimal> { ["sh.600000"] = 1000m });
        Assert.AreEqual(1000m, ledger.GetAvailablePosition("sh.600000"));

        var sellOrder = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.0, 500m);
        bool ok = ledger.TryFreeze(
            sellOrder,
            10.0,
            1.0,
            1.0,
            out var closeQty,
            out var openQty,
            out var frozenCash,
            out var frozenPos,
            out var rejectReason);

        Assert.IsTrue(ok, $"卖出平仓在现金为0时必须成功，实际错误: {rejectReason}");
        Assert.AreEqual(500m, closeQty);
        Assert.AreEqual(0m, openQty);
        Assert.AreEqual(0.0, frozenCash, "纯平仓所需预冻结现金必须为 0");
        Assert.AreEqual(500m, frozenPos);
        Assert.AreEqual(500m, ledger.GetAvailablePosition("sh.600000"));
    }

    [TestMethod]
    public void BuyAndSell_FinancialConservation_ShouldBeExact()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            SettleDays = 0,
            AllowShort = false,
            FeeRate = 0.001,
            MinFee = 0.0
        };
        var ledger = new AccountLedger(config);

        // 1. 买入 1000 股 @ 10.0 元
        var buyReq = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
        ledger.TryFreeze(buyReq, 10.0, 1.0, 1.0, out var cQty, out var oQty, out var fCash, out var fPos, out _);

        // 模拟成交报告
        var fillBuy = new ExecutionReport("ord-1", "sh.600000", 10.0, 1000m, 10.0, DateTime.UtcNow); // 手续费 10.0
        ledger.CommitFill(fillBuy, true, cQty, oQty, 1.0, 1.0, 0, fCash, fPos, DateTime.UtcNow, out _);

        // 现金 = 100,000 - 10,000 (货款) - 10 (佣金) = 89,990
        Assert.AreEqual(89990.0, ledger.Cash, 1e-4);
        Assert.AreEqual(1000m, ledger.GetPosition("sh.600000"));
        Assert.AreEqual(89990.0 + 10000.0, ledger.GetTotalEquity(), 1e-4); // 总资产守恒 = 99,990

        // 2. 卖出 1000 股 @ 15.0 元
        var sellReq = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 15.0, 1000m);
        ledger.TryFreeze(sellReq, 15.0, 1.0, 1.0, out cQty, out oQty, out fCash, out fPos, out _);

        var fillSell = new ExecutionReport("ord-2", "sh.600000", 15.0, 1000m, 15.0, DateTime.UtcNow); // 手续费 15.0
        ledger.CommitFill(fillSell, false, cQty, oQty, 1.0, 1.0, 0, fCash, fPos, DateTime.UtcNow, out _);

        // 现金 = 89,990 + 15,000 (回款) - 15 (佣金) = 104,975
        Assert.AreEqual(104975.0, ledger.Cash, 1e-4);
        Assert.AreEqual(0m, ledger.GetPosition("sh.600000"));
        Assert.AreEqual(104975.0, ledger.GetTotalEquity(), 1e-4); // 净赚 4975 元
    }

    [TestMethod]
    public void TPlusOne_PendingLots_UnlocksOnNextDay()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            SettleDays = 1,
            AllowShort = false
        };
        var ledger = new AccountLedger(config);
        var today = new DateTime(2025, 1, 1);

        var buyReq = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
        ledger.TryFreeze(buyReq, 10.0, 1.0, 1.0, out var cQty, out var oQty, out var fCash, out var fPos, out _);

        var fillBuy = new ExecutionReport("ord-1", "sh.600000", 10.0, 1000m, 0.0, today);
        ledger.CommitFill(fillBuy, true, cQty, oQty, 1.0, 1.0, 1, fCash, fPos, today, out _);

        Assert.AreEqual(1000m, ledger.GetPosition("sh.600000"));
        Assert.AreEqual(0m, ledger.GetAvailablePosition("sh.600000"), "T+1 当日不可卖");

        // 当日推进日切，仍不可用
        ledger.ProcessDayRollover(today);
        Assert.AreEqual(0m, ledger.GetAvailablePosition("sh.600000"));

        // 次日日切，必须解锁
        ledger.ProcessDayRollover(today.AddDays(1));
        Assert.AreEqual(1000m, ledger.GetAvailablePosition("sh.600000"));
    }

    [TestMethod]
    public void Futures_ShortSelling_MarginAndPnL_ShouldBeExact()
    {
        var config = new BrokerConfig
        {
            InitialCash = 500_000.0,
            AllowShort = true,
            Multiplier = 10.0,
            MarginRate = 0.1,
            FeeRate = 0.0,
            MinFee = 0.0
        };
        var ledger = new AccountLedger(config);

        // 1. 开空 5 手 @ 3500 元
        // 名义价值 = 5 * 3500 * 10 = 175,000 元，保证金 = 17,500 元
        var sellReq = new OrderRequest("rb2505", OrderDirection.Sell, OrderType.Market, 3500.0, 5m);
        bool freezeOk = ledger.TryFreeze(sellReq, 3500.0, 10.0, 0.1, out var cQty, out var oQty, out var fCash, out var fPos, out _);
        Assert.IsTrue(freezeOk);
        Assert.AreEqual(17500.0, fCash);
        Assert.AreEqual(5m, oQty);
        Assert.AreEqual(0m, cQty);

        var fillOpen = new ExecutionReport("o-1", "rb2505", 3500.0, 5m, 0.0, DateTime.UtcNow);
        ledger.CommitFill(fillOpen, false, cQty, oQty, 10.0, 0.1, 0, fCash, fPos, DateTime.UtcNow, out _);

        Assert.AreEqual(-5m, ledger.GetPosition("rb2505"));
        Assert.AreEqual(-5m, ledger.GetAvailablePosition("rb2505"));
        Assert.AreEqual(500_000.0 - 17500.0, ledger.Cash);
        Assert.AreEqual(500_000.0, ledger.GetTotalEquity());

        // 2. 平空 5 手 @ 3400 元 (盈利：(3500 - 3400) * 5 * 10 = 5000 元)
        var buyReq = new OrderRequest("rb2505", OrderDirection.Buy, OrderType.Market, 3400.0, 5m);
        freezeOk = ledger.TryFreeze(buyReq, 3400.0, 10.0, 0.1, out cQty, out oQty, out fCash, out fPos, out _);
        Assert.IsTrue(freezeOk);
        Assert.AreEqual(5m, cQty);
        Assert.AreEqual(0m, oQty);
        Assert.AreEqual(0.0, fCash, "纯平空所需预冻结现金必须为 0");

        var fillClose = new ExecutionReport("o-2", "rb2505", 3400.0, 5m, 0.0, DateTime.UtcNow);
        ledger.CommitFill(fillClose, true, cQty, oQty, 10.0, 0.1, 0, fCash, fPos, DateTime.UtcNow, out _);

        Assert.AreEqual(0m, ledger.GetPosition("rb2505"));
        // 最终现金 = 初始 500,000 + 盈利 5,000 = 505,000
        Assert.AreEqual(505_000.0, ledger.Cash);
        Assert.AreEqual(505_000.0, ledger.GetTotalEquity());
    }

    [TestMethod]
    public void PositionFlip_LongToShort_TwoSegmentSplit_ShouldWorkCorrectly()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            AllowShort = true,
            Multiplier = 1.0,
            MarginRate = 1.0,
            FeeRate = 0.0
        };
        var ledger = new AccountLedger(config);

        // 初始持有多头 100 股 @ 10 元 (占用 1000 元)
        ledger.SyncState(99000.0, new Dictionary<string, decimal> { ["sh.600000"] = 100m });
        Assert.AreEqual(100m, ledger.GetAvailablePosition("sh.600000"));

        // 提交卖出 250 股 @ 10 元 (多翻空：平多 100 股 + 开空 150 股)
        var flipReq = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.0, 250m);
        bool ok = ledger.TryFreeze(flipReq, 10.0, 1.0, 1.0, out var closeQty, out var openQty, out var fCash, out var fPos, out _);

        Assert.IsTrue(ok);
        Assert.AreEqual(100m, closeQty, "平仓额度被准确限制为当前多头 100 股");
        Assert.AreEqual(150m, openQty, "反手开空额度为剩余 150 股");
        Assert.AreEqual(100m, fPos, "持仓冻结 100 股");
        Assert.AreEqual(1500.0, fCash, "反手新开 150 股按 10 元冻结 1500 元现金");
        Assert.AreEqual(0m, ledger.GetAvailablePosition("sh.600000"));
    }

    [TestMethod]
    public void Unfreeze_ShouldRestoreCashAndPosition()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            AllowShort = false,
            SettleDays = 0,
            FeeRate = 0.0,
            MinFee = 0.0
        };
        var ledger = new AccountLedger(config);

        // 预冻结买入 1000 股 @ 10 元
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 1000m);
        ledger.TryFreeze(req, 10.0, 1.0, 1.0, out _, out _, out var fCash, out _, out _);

        Assert.AreEqual(10000.0, ledger.FrozenCash);
        Assert.AreEqual(90000.0, ledger.AvailableCash);

        // 撤单解冻
        ledger.Unfreeze("sh.600000", fCash, 0m, true);
        Assert.AreEqual(0.0, ledger.FrozenCash);
        Assert.AreEqual(100000.0, ledger.AvailableCash);
    }

    [TestMethod]
    public void WeightedAverageCost_MultipleBuys_ShouldUpdateAccurately()
    {
        var config = new BrokerConfig
        {
            InitialCash = 500_000.0,
            AllowShort = false,
            SettleDays = 0,
            FeeRate = 0.0
        };
        var ledger = new AccountLedger(config);

        // 批次 1: 买入 100 股 @ 10 元
        var req1 = new OrderRequest("s", OrderDirection.Buy, OrderType.Market, 10.0, 100m);
        ledger.TryFreeze(req1, 10.0, 1.0, 1.0, out var c1, out var o1, out var fc1, out var fp1, out _);
        ledger.CommitFill(new ExecutionReport("1", "s", 10.0, 100m, 0.0, DateTime.UtcNow), true, c1, o1, 1.0, 1.0, 0, fc1, fp1, DateTime.UtcNow, out _);

        // 批次 2: 买入 100 股 @ 20 元
        // 加权成本应当变为 (100 * 10 + 100 * 20) / 200 = 15 元
        var req2 = new OrderRequest("s", OrderDirection.Buy, OrderType.Market, 20.0, 100m);
        ledger.TryFreeze(req2, 20.0, 1.0, 1.0, out var c2, out var o2, out var fc2, out var fp2, out _);
        ledger.CommitFill(new ExecutionReport("2", "s", 20.0, 100m, 0.0, DateTime.UtcNow), true, c2, o2, 1.0, 1.0, 0, fc2, fp2, DateTime.UtcNow, out var posEvent);

        Assert.AreEqual(200m, ledger.GetPosition("s"));
        Assert.AreEqual(15.0, posEvent.AveragePrice, 1e-4);
    }

    [TestMethod]
    public void GetTotalEquity_WithCustomPriceResolver_ShouldMarkToMarket()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            AllowShort = false,
            SettleDays = 0,
            FeeRate = 0.0
        };
        var ledger = new AccountLedger(config);
        ledger.SyncState(50000.0, new Dictionary<string, decimal> { ["s1"] = 1000m, ["s2"] = 500m });

        // 自定义实时行情解析器：s1 价格 30.0，s2 价格 40.0
        double equity = ledger.GetTotalEquity(sym => sym switch
        {
            "s1" => 30.0,
            "s2" => 40.0,
            _ => 0.0
        });

        // 50,000 + 1000 * 30 + 500 * 40 = 50,000 + 30,000 + 20,000 = 100,000
        Assert.AreEqual(100_000.0, equity, 1e-4);
    }
}
