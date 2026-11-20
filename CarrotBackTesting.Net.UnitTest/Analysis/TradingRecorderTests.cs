using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Analysis;

[TestClass]
public class TradingRecorderTests
{
    [TestMethod]
    public void AccountLedger_Settlement_LongTrade_GeneratesClosedTradeAndSettlements()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100000.0,
            FeeRate = 0.0005,
            MinFee = 5.0,
            Multiplier = 1.0,
            MarginRate = 1.0
        };
        var ledger = new AccountLedger(config);
        var t1 = new DateTime(2024, 1, 2, 9, 30, 0);
        var t2 = new DateTime(2024, 1, 2, 10, 0, 0);

        // 1. 买入开仓 1000 股 @ 10.0，手续费 5.0
        var reportBuy = new ExecutionReport("ord-1", "sh.600000", 10.0, 1000m, 5.0, t1);
        ledger.CommitFill(
            reportBuy,
            isBuy: true,
            closeQty: 0m,
            openQty: 1000m,
            multiplier: 1.0,
            marginRate: 1.0,
            settleDays: 0,
            unfreezeCash: 0.0,
            unfreezePosition: 0m,
            exchangeDate: t1.Date,
            orderTag: "Signal_Breakout",
            out var posEvent1,
            out var closedTrade1,
            out var settlement1);

        Assert.IsNull(closedTrade1);
        Assert.IsNotNull(settlement1);
        Assert.AreEqual("sh.600000", settlement1.Symbol);
        Assert.AreEqual(OrderDirection.Buy, settlement1.Direction);
        Assert.AreEqual(10000.0, settlement1.Turnover);
        Assert.AreEqual(5.0, settlement1.Commission);
        Assert.AreEqual(0.0, settlement1.RealizedPnL);
        Assert.AreEqual(100000.0 - 10000.0 - 5.0, settlement1.BalanceCash);
        Assert.AreEqual("Signal_Breakout", settlement1.OrderTag);

        // 2. 价格上涨至 12.0，检查在手持仓的未实现收益（浮盈）
        double unrealized = ledger.GetTotalUnrealizedPnL(sym => 12.0);
        Assert.AreEqual(2000.0, unrealized, 1e-4);

        var snapshots = ledger.GetActivePositionSnapshots(sym => 12.0, 101995.0);
        Assert.AreEqual(1, snapshots.Count);
        var posSnap = snapshots[0];
        Assert.AreEqual(10.0, posSnap.CostPrice);
        Assert.AreEqual(12.0, posSnap.CurrentPrice);
        Assert.AreEqual(12000.0, posSnap.MarketValue);
        Assert.AreEqual(2000.0, posSnap.UnrealizedPnL);
        Assert.AreEqual(10000.0, posSnap.OccupiedMargin);

        // 3. 卖出平仓 1000 股 @ 12.0，手续费 6.0
        var reportSell = new ExecutionReport("ord-2", "sh.600000", 12.0, 1000m, 6.0, t2);
        ledger.CommitFill(
            reportSell,
            isBuy: false,
            closeQty: 1000m,
            openQty: 0m,
            multiplier: 1.0,
            marginRate: 1.0,
            settleDays: 0,
            unfreezeCash: 0.0,
            unfreezePosition: 0m,
            exchangeDate: t2.Date,
            orderTag: "Exit_TakeProfit",
            out var posEvent2,
            out var closedTrade2,
            out var settlement2);

        Assert.IsNotNull(closedTrade2);
        Assert.AreEqual("sh.600000", closedTrade2.Symbol);
        Assert.AreEqual(TradeSide.Long, closedTrade2.Side);
        Assert.AreEqual(1000m, closedTrade2.Quantity);
        Assert.AreEqual(10.0, closedTrade2.EntryPrice);
        Assert.AreEqual(12.0, closedTrade2.ExitPrice);
        Assert.AreEqual(t1, closedTrade2.EntryTime);
        Assert.AreEqual(t2, closedTrade2.ExitTime);
        Assert.AreEqual("Signal_Breakout", closedTrade2.EntryTag);
        Assert.AreEqual("Exit_TakeProfit", closedTrade2.ExitTag);

        // 毛利 2000 - 开仓手续费 5.0 - 平仓手续费 6.0 = 1989.0
        Assert.AreEqual(1989.0, closedTrade2.RealizedPnL, 1e-4);
        Assert.AreEqual(11.0, closedTrade2.Commission, 1e-4);

        Assert.IsNotNull(settlement2);
        Assert.AreEqual(OrderDirection.Sell, settlement2.Direction);
        Assert.AreEqual(1989.0, settlement2.RealizedPnL, 1e-4);
        Assert.AreEqual(0.0, ledger.GetTotalUnrealizedPnL());
    }

    [TestMethod]
    public void AccountLedger_Settlement_ShortTrade_GeneratesClosedTradeAndUnrealizedPnL()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100000.0,
            FeeRate = 0.0005,
            MinFee = 5.0,
            Multiplier = 1.0,
            MarginRate = 1.0
        };
        var ledger = new AccountLedger(config);
        var t1 = new DateTime(2024, 1, 2, 9, 30, 0);
        var t2 = new DateTime(2024, 1, 2, 10, 0, 0);

        // 1. 卖出开空 500 股 @ 20.0，手续费 10.0
        var reportSell = new ExecutionReport("ord-1", "sz.000001", 20.0, 500m, 10.0, t1);
        ledger.CommitFill(
            reportSell,
            isBuy: false,
            closeQty: 0m,
            openQty: 500m,
            multiplier: 1.0,
            marginRate: 1.0,
            settleDays: 0,
            unfreezeCash: 0.0,
            unfreezePosition: 0m,
            exchangeDate: t1.Date,
            orderTag: "ShortEntry",
            out _,
            out var closedTrade1,
            out var settlement1);

        Assert.IsNull(closedTrade1);
        Assert.IsNotNull(settlement1);

        // 2. 标的价格下跌至 18.0，空头未实现收益 = (20 - 18) * 500 = 1000.0
        double unrealized = ledger.GetTotalUnrealizedPnL(sym => 18.0);
        Assert.AreEqual(1000.0, unrealized, 1e-4);

        // 3. 买入平空 500 股 @ 18.0，手续费 9.0
        var reportBuy = new ExecutionReport("ord-2", "sz.000001", 18.0, 500m, 9.0, t2);
        ledger.CommitFill(
            reportBuy,
            isBuy: true,
            closeQty: 500m,
            openQty: 0m,
            multiplier: 1.0,
            marginRate: 1.0,
            settleDays: 0,
            unfreezeCash: 0.0,
            unfreezePosition: 0m,
            exchangeDate: t2.Date,
            orderTag: "ShortExit",
            out _,
            out var closedTrade2,
            out var settlement2);

        Assert.IsNotNull(closedTrade2);
        Assert.AreEqual(TradeSide.Short, closedTrade2.Side);
        Assert.AreEqual(500m, closedTrade2.Quantity);
        Assert.AreEqual(20.0, closedTrade2.EntryPrice);
        Assert.AreEqual(18.0, closedTrade2.ExitPrice);
        // 毛利 1000 - 开仓手续费 10 - 平仓手续费 9 = 981.0
        Assert.AreEqual(981.0, closedTrade2.RealizedPnL, 1e-4);
        Assert.AreEqual(19.0, closedTrade2.Commission, 1e-4);
        Assert.AreEqual(981.0, settlement2.RealizedPnL, 1e-4);
    }

    [TestMethod]
    public void AccountLedger_Settlement_PartialClose_CalculatesProportionalFeeAndRemainingUnrealizedPnL()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100000.0,
            FeeRate = 0.001,
            MinFee = 0.0,
            Multiplier = 1.0,
            MarginRate = 1.0
        };
        var ledger = new AccountLedger(config);
        var t0 = new DateTime(2024, 1, 2, 9, 30, 0);

        // 1. 买入 1000 股 @ 10.0，手续费 10.0
        ledger.CommitFill(
            new ExecutionReport("ord-1", "sh.600000", 10.0, 1000m, 10.0, t0),
            isBuy: true, closeQty: 0m, openQty: 1000m, multiplier: 1.0, marginRate: 1.0, settleDays: 0,
            unfreezeCash: 0.0, unfreezePosition: 0m, exchangeDate: t0.Date, orderTag: "Open1000",
            out _, out _, out _);

        // 2. 平仓 400 股 @ 11.0，手续费 4.4
        ledger.CommitFill(
            new ExecutionReport("ord-2", "sh.600000", 11.0, 400m, 4.4, t0.AddMinutes(10)),
            isBuy: false, closeQty: 400m, openQty: 0m, multiplier: 1.0, marginRate: 1.0, settleDays: 0,
            unfreezeCash: 0.0, unfreezePosition: 0m, exchangeDate: t0.Date, orderTag: "Close400",
            out _, out var trade1, out var st1);

        Assert.IsNotNull(trade1);
        Assert.AreEqual(400m, trade1.Quantity);
        // 毛利 (11 - 10) * 400 = 400 - 开仓分摊手续费 4.0 - 平仓手续费 4.4 = 391.6
        Assert.AreEqual(391.6, trade1.RealizedPnL, 1e-4);
        Assert.AreEqual(8.4, trade1.Commission, 1e-4);

        // 3. 此时剩余 600 股持仓，在最新价 12.0 时的未实现收益 = (12 - 10) * 600 = 1200.0
        double remainingUnrealized = ledger.GetTotalUnrealizedPnL(sym => 12.0);
        Assert.AreEqual(1200.0, remainingUnrealized, 1e-4);
    }

    [TestMethod]
    public void TradingRecorder_FullSinkAndGetResult_ShouldAggregateCleanly()
    {
        var recorder = new TradingRecorder(initialStepCapacity: 128);
        var t0 = new DateTime(2024, 1, 2, 9, 30, 0);

        // 1. 记录包含未实现收益与保证金占用率的步进点
        var p1 = new EquityPoint(t0, 100000.0, 80000.0, 0.0, 20000.0, 20000.0, 0.0);
        recorder.RecordStep(p1);
        Assert.AreEqual(0.2, p1.MarginRatio, 1e-4);

        // 2. 记录交割单与已平仓单
        var settlement = new SettlementRecord(t0, "sh.600000", "ord-1", OrderDirection.Buy, 10.0, 100m, 1000.0, 5.0, 0.0, 98995.0, "Alpha");
        recorder.RecordSettlement(settlement);

        var trade = new Trade("sh.600000", TradeSide.Long, t0, 10.0, t0.AddMinutes(5), 12.0, 100m, 1.0, 195.0, 0.195, 5.0, 0.0, 5, "Alpha", "TakeProfit");
        recorder.RecordClosedTrade(trade);

        // 3. 记录日终快照
        var pos = new PositionSnapshot("sz.000001", TradeSide.Long, 200m, 200m, 0m, 10.0, 10.5, 1.0, 2100.0, 100.0, 1.0, 2000.0, 0.02);
        recorder.RecordDailySnapshot(new DailySnapshot(t0.Date, 101000.0, 98900.0, 0.0, 2000.0, 2100.0, 100.0, new[] { pos }));

        // 4. 记录策略信号
        recorder.RecordSignal(new SignalRecord(t0, "sh.600000", 0.95, "AlphaGroup", "GoldenCross"));

        // 5. 产出最终聚合结果
        var result = recorder.GetResult(new[] { pos });

        Assert.AreEqual(1, result.Settlements.Count);
        Assert.AreEqual(1, result.ClosedTrades.Count);
        Assert.AreEqual(1, result.EquityCurve.Count);
        Assert.AreEqual(1, result.DailySnapshots.Count);
        Assert.AreEqual(1, result.Signals.Count);
        Assert.AreEqual(1, result.OpenPositions.Count);
        Assert.AreEqual(100.0, result.OpenPositions[0].UnrealizedPnL);
        Assert.AreEqual("ord-1", result.Settlements[0].OrderId);
        Assert.AreEqual(195.0, result.ClosedTrades[0].RealizedPnL);

        // 验证重置
        recorder.Reset();
        var emptyResult = recorder.GetResult(Array.Empty<PositionSnapshot>());
        Assert.AreEqual(0, emptyResult.Settlements.Count);
        Assert.AreEqual(0, emptyResult.ClosedTrades.Count);
        Assert.AreEqual(0, emptyResult.EquityCurve.Count);
    }

    [TestMethod]
    public void TradingRecorder_Checkpoint_SaveAndRestore_ShouldPreserveAllState()
    {
        var recorder = new TradingRecorder(initialStepCapacity: 128);
        var t0 = new DateTime(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        // 1. 记录 2 个时间步净值点
        recorder.RecordStep(new EquityPoint(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0));
        recorder.RecordStep(new EquityPoint(t0.AddMinutes(1), 102000.0, 80000.0, 0.0, 20000.0, 22000.0, 2000.0));

        // 2. 记录交割单与平仓单
        recorder.RecordSettlement(new SettlementRecord(t0, "sh.600000", "ord-1", OrderDirection.Buy, 10.0, 100m, 1000.0, 5.0, 0.0, 98995.0, "Buy1"));
        recorder.RecordSettlement(new SettlementRecord(t0.AddMinutes(2), "sh.600000", "ord-2", OrderDirection.Sell, 12.0, 100m, 1200.0, 6.0, 194.0, 100189.0, "Sell1"));

        recorder.RecordClosedTrade(new Trade("sh.600000", TradeSide.Long, t0, 10.0, t0.AddMinutes(2), 12.0, 100m, 1.0, 194.0, 0.194, 6.0, 0.0, 2, "Buy1", "Sell1"));

        // 3. 记录快照
        var pos = new PositionSnapshot("sz.000001", TradeSide.Long, 500m, 500m, 0m, 20.0, 21.0, 1.0, 10500.0, 500.0, 1.0, 10000.0, 0.1);
        recorder.RecordDailySnapshot(new DailySnapshot(t0.Date, 102000.0, 91500.0, 0.0, 10000.0, 10500.0, 500.0, new[] { pos }));

        // 4. 记录信号
        recorder.RecordSignal(new SignalRecord(t0, "sh.600000", 0.99, "Alpha", "StrongBuy"));

        // 5. 保存检查点
        byte[] checkpoint = recorder.SaveCheckpoint();
        Assert.IsNotNull(checkpoint);
        Assert.IsTrue(checkpoint.Length > 64);

        // 6. 恢复至新记录器实例
        var restoredRecorder = new TradingRecorder();
        restoredRecorder.RestoreCheckpoint(checkpoint);

        // 验证已恢复的历史状态
        var result = restoredRecorder.GetResult(new[] { pos });
        Assert.AreEqual(2, result.EquityCurve.Count);
        Assert.AreEqual(2, result.Settlements.Count);
        Assert.AreEqual(1, result.ClosedTrades.Count);
        Assert.AreEqual(1, result.DailySnapshots.Count);
        Assert.AreEqual(1, result.Signals.Count);
        Assert.AreEqual("ord-1", result.Settlements[0].OrderId);
        Assert.AreEqual("ord-2", result.Settlements[1].OrderId);
        Assert.AreEqual(194.0, result.ClosedTrades[0].RealizedPnL);
        Assert.AreEqual(500.0, result.DailySnapshots[0].UnrealizedPnL);
        Assert.AreEqual("StrongBuy", result.Signals[0].Reason);
    }
}
