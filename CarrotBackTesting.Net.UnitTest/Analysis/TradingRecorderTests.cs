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
        var reportBuy = new ExecutionReport(1L, "sh.600000", 10.0, 1000m, 5.0, t1);
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
        var reportSell = new ExecutionReport(2L, "sh.600000", 12.0, 1000m, 6.0, t2);
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
        var reportSell = new ExecutionReport(1L, "sz.000001", 20.0, 500m, 10.0, t1);
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
        var reportBuy = new ExecutionReport(2L, "sz.000001", 18.0, 500m, 9.0, t2);
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
            new ExecutionReport(1L, "sh.600000", 10.0, 1000m, 10.0, t0),
            isBuy: true, closeQty: 0m, openQty: 1000m, multiplier: 1.0, marginRate: 1.0, settleDays: 0,
            unfreezeCash: 0.0, unfreezePosition: 0m, exchangeDate: t0.Date, orderTag: "Open1000",
            out _, out _, out _);

        // 2. 平仓 400 股 @ 11.0，手续费 4.4
        ledger.CommitFill(
            new ExecutionReport(2L, "sh.600000", 11.0, 400m, 4.4, t0.AddMinutes(10)),
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
        recorder.Record(p1);
        Assert.AreEqual(0.2, p1.MarginRatio, 1e-4);

        // 2. 记录交割单与已平仓单
        var settlement = new SettlementRecord(t0, "sh.600000", 1L, OrderDirection.Buy, 10.0, 100m, 1000.0, 5.0, 0.0, 98995.0, "Alpha");
        recorder.Record(settlement);

        var trade = new Trade("sh.600000", TradeSide.Long, t0, 10.0, t0.AddMinutes(5), 12.0, 100m, 1.0, 195.0, 0.195, 5.0, 0.0, 5, "Alpha", "TakeProfit");
        recorder.Record(trade);

        // 3. 记录日终快照
        var pos = new PositionSnapshot("sz.000001", TradeSide.Long, 200m, 200m, 0m, 10.0, 10.5, 1.0, 2100.0, 100.0, 1.0, 2000.0, 0.02);
        recorder.Record(new DailySnapshot(t0.Date, 101000.0, 98900.0, 0.0, 2000.0, 2100.0, 100.0, new[] { pos }));

        // 4. 记录策略信号与订单动作事件
        recorder.Record(new SignalRecord(t0, "sh.600000", 0.95, "AlphaGroup", "GoldenCross"));
        var ordEvt = new OrderEvent(
            t0, 1L, "sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m,
            OrderEventType.Fill, OrderStatus.Filled,
            FillQuantity: 100m, CumQuantity: 100m, LeavesQuantity: 0m,
            FillPrice: 10.0, Role: LiquidityRole.Taker, Reason: "", OrderTag: "Alpha");
        recorder.Record(ordEvt);

        // 5. 产出最终聚合结果
        var result = recorder.GetResult(new[] { pos });

        Assert.AreEqual(1, result.Settlements.Count);
        Assert.AreEqual(1, result.ClosedTrades.Count);
        Assert.AreEqual(1, result.EquityCurve.Count);
        Assert.AreEqual(1, result.DailySnapshots.Count);
        Assert.AreEqual(1, result.Signals.Count);
        Assert.AreEqual(1, result.OrderEvents.Count);
        Assert.AreEqual(1L, result.OrderEvents[0].OrderId);
        Assert.AreEqual(LiquidityRole.Taker, result.OrderEvents[0].Role);
        Assert.AreEqual(1, result.OrderEvents.Count);
        Assert.AreEqual(LiquidityRole.Taker, result.OrderEvents[0].Role);
        Assert.AreEqual(1, result.OpenPositions.Count);
        Assert.AreEqual(100.0, result.OpenPositions[0].UnrealizedPnL);
        Assert.AreEqual(1L, result.Settlements[0].OrderId);
        Assert.AreEqual(195.0, result.ClosedTrades[0].RealizedPnL);

        // 验证重置
        recorder.Reset();
        var emptyResult = recorder.GetResult(Array.Empty<PositionSnapshot>());
        Assert.AreEqual(0, emptyResult.Settlements.Count);
        Assert.AreEqual(0, emptyResult.ClosedTrades.Count);
        Assert.AreEqual(0, emptyResult.EquityCurve.Count);
        Assert.AreEqual(0, emptyResult.OrderEvents.Count);
    }

    [TestMethod]
    public void TradingRecorder_Checkpoint_SaveAndRestore_ShouldPreserveAllState()
    {
        var recorder = new TradingRecorder(initialStepCapacity: 128);
        var t0 = new DateTime(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        // 1. 记录 2 个时间步净值点
        recorder.Record(new EquityPoint(t0, 100000.0, 100000.0, 0.0, 0.0, 0.0, 0.0));
        recorder.Record(new EquityPoint(t0.AddMinutes(1), 102000.0, 80000.0, 0.0, 20000.0, 22000.0, 2000.0));

        // 2. 记录交割单与平仓单
        recorder.Record(new SettlementRecord(t0, "sh.600000", 1L, OrderDirection.Buy, 10.0, 100m, 1000.0, 5.0, 0.0, 98995.0, "Buy1"));
        recorder.Record(new SettlementRecord(t0.AddMinutes(2), "sh.600000", 2L, OrderDirection.Sell, 12.0, 100m, 1200.0, 6.0, 194.0, 100189.0, "Sell1"));

        recorder.Record(new Trade("sh.600000", TradeSide.Long, t0, 10.0, t0.AddMinutes(2), 12.0, 100m, 1.0, 194.0, 0.194, 6.0, 0.0, 2, "Buy1", "Sell1"));

        // 3. 记录快照
        var pos = new PositionSnapshot("sz.000001", TradeSide.Long, 500m, 500m, 0m, 20.0, 21.0, 1.0, 10500.0, 500.0, 1.0, 10000.0, 0.1);
        recorder.Record(new DailySnapshot(t0.Date, 102000.0, 91500.0, 0.0, 10000.0, 10500.0, 500.0, new[] { pos }));

        // 4. 记录信号与订单动作事件
        recorder.Record(new SignalRecord(t0, "sh.600000", 0.99, "Alpha", "StrongBuy"));
        recorder.Record(new OrderEvent(
            t0, 1L, "sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m,
            OrderEventType.Fill, OrderStatus.Filled,
            FillQuantity: 100m, CumQuantity: 100m, LeavesQuantity: 0m,
            FillPrice: 10.0, Role: LiquidityRole.Taker, Reason: "", OrderTag: "Buy1"));

        // 5. 保存强类型快照与通用检查点字节持久化
        var snapshot = recorder.SaveState();
        Assert.IsNotNull(snapshot);
        byte[] checkpoint = recorder.ToJsonBytes();
        Assert.IsNotNull(checkpoint);
        Assert.IsTrue(checkpoint.Length > 64);

        // 6. 恢复至新记录器实例（验证通用 ToJsonBytes / RestoreFromJsonBytes 往返无损性）
        var restoredRecorder = new TradingRecorder();
        restoredRecorder.RestoreFromJsonBytes(checkpoint);

        // 验证已恢复的历史状态
        var result = restoredRecorder.GetResult(new[] { pos });
        Assert.AreEqual(2, result.EquityCurve.Count);
        Assert.AreEqual(2, result.Settlements.Count);
        Assert.AreEqual(1, result.ClosedTrades.Count);
        Assert.AreEqual(1, result.DailySnapshots.Count);
        Assert.AreEqual(1, result.Signals.Count);
        Assert.AreEqual(1, result.OrderEvents.Count);
        Assert.AreEqual(1L, result.OrderEvents[0].OrderId);
        Assert.AreEqual(LiquidityRole.Taker, result.OrderEvents[0].Role);
        Assert.AreEqual(1L, result.Settlements[0].OrderId);
        Assert.AreEqual(2L, result.Settlements[1].OrderId);

        // 7. 验证纯内存快照直接恢复（ICheckpointable<TradingRecorderSnapshot>）
        var memoryRestoredRecorder = new TradingRecorder();
        memoryRestoredRecorder.RestoreState(snapshot);
        var memResult = memoryRestoredRecorder.GetResult(new[] { pos });
        Assert.AreEqual(2, memResult.EquityCurve.Count);
        Assert.AreEqual(1, memResult.OrderEvents.Count);
        Assert.AreEqual(1L, memResult.OrderEvents[0].OrderId);
        Assert.AreEqual(194.0, result.ClosedTrades[0].RealizedPnL);
        Assert.AreEqual(500.0, result.DailySnapshots[0].UnrealizedPnL);
        Assert.AreEqual("StrongBuy", result.Signals[0].Reason);
    }

    [TestMethod]
    public void TradingRecorder_SnapshotIsolation_ModificationsAfterSnapshotDoNotCorruptState()
    {
        // 验证 TradingRecorderSnapshot 的内存不可变性与深隔离：快照抓取后的新增记录不会反向污染既有快照
        var recorder = new TradingRecorder();
        var t0 = new DateTime(2025, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        recorder.Record(new SettlementRecord(t0, "sh.600000", 1L, OrderDirection.Buy, 10.0, 100m, 1000.0, 5.0, 0.0, 99000.0, "Tag1"));
        recorder.Record(new OrderEvent(t0, 1L, "sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m,
            OrderEventType.Submitted, OrderStatus.Submitted, 0m, 0m, 100m));

        var snapshot1 = recorder.SaveState();
        Assert.IsNotNull(snapshot1.Settlements);
        Assert.IsNotNull(snapshot1.OrderEvents);
        Assert.AreEqual(1, snapshot1.Settlements.Count);
        Assert.AreEqual(1, snapshot1.OrderEvents.Count);

        // 产生更多后续事件与交割
        recorder.Record(new SettlementRecord(t0.AddMinutes(1), "sh.600000", 2L, OrderDirection.Sell, 12.0, 100m, 1200.0, 6.0, 195.0, 100195.0, "Tag2"));
        recorder.Record(new OrderEvent(t0.AddMinutes(1), 1L, "sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m,
            OrderEventType.Fill, OrderStatus.Filled, 100m, 100m, 0m));

        // 断言 snapshot1 保持物理不可变隔离
        Assert.AreEqual(1, snapshot1.Settlements.Count);
        Assert.AreEqual(1, snapshot1.OrderEvents.Count);

        // 使用新实例恢复 snapshot1，断言仅包含初始快照的数据
        var restored = new TradingRecorder();
        restored.RestoreState(snapshot1);
        var restoredResult = restored.GetResult(Array.Empty<PositionSnapshot>());
        Assert.AreEqual(1, restoredResult.Settlements.Count);
        Assert.AreEqual(1, restoredResult.OrderEvents.Count);
        Assert.AreEqual(OrderEventType.Submitted, restoredResult.OrderEvents[0].EventType);
    }

    [TestMethod]
    public void TradingResult_TradingStatus_DefaultNormal_AndCustomStatusPreserved()
    {
        // 验证 TradingResult 默认状态为 Normal，且支持显式传入 Bankrupt 状态
        var defaultResult = new TradingResult();
        Assert.AreEqual(TradingStatus.Normal, defaultResult.Status);

        var bankruptResult = new TradingResult(
            Array.Empty<SettlementRecord>(),
            Array.Empty<Trade>(),
            Array.Empty<PositionSnapshot>(),
            Array.Empty<EquityPoint>(),
            Array.Empty<DailySnapshot>(),
            Array.Empty<SignalRecord>(),
            Array.Empty<OrderEvent>(),
            status: TradingStatus.Bankrupt);

        Assert.AreEqual(TradingStatus.Bankrupt, bankruptResult.Status);
    }

    [TestMethod]
    public void TradingRecorder_GetResult_WithCustomStatus_ShouldReflectInTradingResult()
    {
        var recorder = new TradingRecorder();
        var resultNormal = recorder.GetResult(Array.Empty<PositionSnapshot>());
        Assert.AreEqual(TradingStatus.Normal, resultNormal.Status);

        var resultBankrupt = recorder.GetResult(Array.Empty<PositionSnapshot>(), TradingStatus.Bankrupt);
        Assert.AreEqual(TradingStatus.Bankrupt, resultBankrupt.Status);
    }

    [TestMethod]
    public void CapitalStatistics_WithTradingStatus_ShouldOutputSelfDescribingMetricsAccurately()
    {
        // 验证 CapitalStatistics 自描述指标流输出 TradingStatus
        var capital = new CapitalStatistics(
            AveragePositionRatio: 0.5,
            MaxPositionRatio: 0.8,
            AverageMarginRatio: 0.2,
            MaxMarginRatio: 0.4,
            CashDrag: 0.5,
            AnnualizedTurnover: 2.5,
            Status: TradingStatus.Bankrupt);

        Assert.AreEqual(TradingStatus.Bankrupt, capital.Status);

        var metrics = capital.GetMetrics().ToList();
        var statusMetric = metrics.FirstOrDefault(m => m.Label.Contains("Trading Status"));
        Assert.IsNotNull(statusMetric);
        Assert.AreEqual("Bankrupt", statusMetric.Value);
        Assert.AreEqual("状态监控", statusMetric.Category);
    }
}
