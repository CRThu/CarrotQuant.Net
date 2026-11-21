using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Indicators;
using CarrotBacktesting.NET.Strategy;
using CarrotBackTesting.Net.UnitTest.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class EngineCheckpointTests
{
    [TestMethod]
    public void AccountLedger_SaveAndRestoreState_ShouldRestoreAllFieldsAccurately()
    {
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            SettleDays = 1,
            AllowShort = false
        };
        var ledger = new AccountLedger(config);
        var t0 = new MarketTime(2025, 1, 1);

        // 买入建仓产生持仓和在途交收批次
        var buyReq = new OrderRequest("SH.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
        ledger.TryFreeze(buyReq, 10.0, 1.0, 1.0, out var cQty, out var oQty, out var fCash, out var fPos, out _);
        var fillBuy = new ExecutionReport(1L, "SH.600000", 10.0, 1000m, 5.0, t0);
        ledger.CommitFill(fillBuy, true, cQty, oQty, 1.0, 1.0, 1, fCash, fPos, t0, out _);

        // 验证当前状态：现金 89995，持仓 1000，可用 0，在途批次 1000，在途手续费池 5.0
        Assert.AreEqual(89995.0, ledger.Cash, 1e-4);
        Assert.AreEqual(1000m, ledger.GetPosition("SH.600000"));
        Assert.AreEqual(0m, ledger.GetAvailablePosition("SH.600000"));

        // 1. 抓取不可变快照
        var snapshot = ledger.SaveState();
        Assert.AreEqual(89995.0, snapshot.Cash, 1e-4);
        Assert.AreEqual(1, snapshot.Positions.Count);
        Assert.AreEqual("SH.600000", snapshot.Positions[0].Symbol);
        Assert.AreEqual(5.0, snapshot.Positions[0].OpenCommission);

        // 2. 篡改当前账本状态（如强行重置对账）
        ledger.SyncState(50000.0, new Dictionary<string, decimal> { ["SZ.000001"] = 500m });
        Assert.AreEqual(50000.0, ledger.Cash);
        Assert.AreEqual(0m, ledger.GetPosition("SH.600000"));

        // 3. 基于快照精确还原
        ledger.RestoreState(snapshot);

        // 4. 断言所有字段 100% 守恒恢复
        Assert.AreEqual(89995.0, ledger.Cash, 1e-4);
        Assert.AreEqual(0.0, ledger.FrozenCash, 1e-4);
        Assert.AreEqual(1000m, ledger.GetPosition("SH.600000"));
        Assert.AreEqual(0m, ledger.GetAvailablePosition("SH.600000"));
        Assert.AreEqual(10.0, ledger.GetPositionCost("SH.600000"), 1e-4);
    }

    [TestMethod]
    public void SimulatedExchangeGateway_SaveAndRestoreState_ShouldRestoreOrdersAndPrices()
    {
        var config = new BrokerConfig();
        var gateway = new SimulatedExchangeGateway(config);
        var t0 = new MarketTime(2025, 1, 1);

        // 先注入当前市价 10.0
        gateway.OnMarketData("SH.600000", t0, new BarSnapshot(close: 10.0));

        // 提交一个低于市价的限价买单 (9.5 < 10.0)，进入挂单簿排队
        var orderReq = new OrderRequest("SH.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 1000m);
        gateway.SendOrder(101L, orderReq);

        // 1. 抓取快照
        var snapshot = gateway.SaveState();
        Assert.AreEqual(1, snapshot.PendingOrders.Count);
        Assert.AreEqual(101L, snapshot.PendingOrders[0].OrderId);
        Assert.AreEqual(9.5, snapshot.PendingOrders[0].Request.Price);

        // 2. 撤销订单导致挂单簿清空
        gateway.CancelOrder(101L);
        var emptySnapshot = gateway.SaveState();
        Assert.AreEqual(0, emptySnapshot.PendingOrders.Count);

        // 3. 恢复快照
        gateway.RestoreState(snapshot);
        var restoredSnapshot = gateway.SaveState();
        Assert.AreEqual(1, restoredSnapshot.PendingOrders.Count);
        Assert.AreEqual(101L, restoredSnapshot.PendingOrders[0].OrderId);
    }

    [TestMethod]
    public void CheckpointExtensions_JsonSerialization_ShouldRoundtripAccurately()
    {
        var config = new BrokerConfig { InitialCash = 50000.0 };
        var ledger = new AccountLedger(config);

        // 验证通用泛型扩展方法 ToJsonBytes 与 RestoreFromJsonBytes
        byte[] jsonBytes = ledger.ToJsonBytes();
        Assert.IsTrue(jsonBytes.Length > 0);

        // 篡改原账本
        ledger.SyncState(100.0, new Dictionary<string, decimal>());
        Assert.AreEqual(100.0, ledger.Cash);

        // 通过 JSON 字节恢复
        ledger.RestoreFromJsonBytes(jsonBytes);
        Assert.AreEqual(50000.0, ledger.Cash);
    }

    [TestMethod]
    public void Broker_SaveAndRestoreState_WithActivePendingOrder_ShouldRestoreAndFillCorrectly()
    {
        var cfg = new BrokerConfig { InitialCash = 100_000.0, FeeRate = 0.0003, MinFee = 5.0 };
        var gateway = new SimulatedExchangeGateway(cfg);
        var broker = new Broker(gateway, cfg);

        // 先注入当前市价 10.0
        gateway.OnMarketData("SH.600000", new MarketTime(2025, 1, 1), new BarSnapshot(close: 10.0));

        // 提交一个低于市价的限价买单，并在 Broker 层面完成两阶段预冻结
        var req = new OrderRequest("SH.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 1000m);
        long orderId = broker.SubmitOrder(req);

        Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
        Assert.IsTrue(broker.FrozenCash > 0, "预冻结现金必须大于 0");
        double originalFrozenCash = broker.FrozenCash;

        // 1. 抓取快照
        var brokerSnapshot = broker.SaveState();
        Assert.AreEqual(1, brokerSnapshot.ActiveOrders.Count);
        Assert.AreEqual(orderId, brokerSnapshot.ActiveOrders[0].OrderId);

        // 2. 模拟新环境并恢复快照
        var newBroker = new Broker(gateway, cfg);
        newBroker.RestoreState(brokerSnapshot);

        Assert.AreEqual(OrderStatus.Submitted, newBroker.GetOrderStatus(orderId));
        Assert.AreEqual(originalFrozenCash, newBroker.FrozenCash, 1e-4);

        // 3. 模拟交易所回报成交（验证 Broker 能正确识别并核销恢复后的订单，不发生静默吞单）
        ExecutionReport? capturedReport = null;
        newBroker.OnTrade += r => capturedReport = r;

        // 通过网关触发该订单成交（市价跌破 9.5 触发限价单撮合）
        gateway.OnMarketData("SH.600000", new MarketTime(2025, 1, 2), new BarSnapshot(close: 9.0));

        Assert.IsNotNull(capturedReport, "恢复后的订单成交回报必须被正确接收派发");
        Assert.AreEqual(1000m, newBroker.GetPosition("SH.600000"), "持仓必须增加 1000 股");
        Assert.AreEqual(0.0, newBroker.FrozenCash, 1e-4, "预冻结资金必须完全释放");
        Assert.AreEqual(100_000.0 - 9500.0 - 5.0, newBroker.AvailableCash, 1e-4, "现金核销守恒");
    }

    private sealed record TestSignal(string Tag, double Score);

    [TestMethod]
    public void MarketState_SaveAndRestoreState_ShouldRestoreStronglyTypedEntries()
    {
        var market = new MarketState();
        var t0 = new MarketTime(2025, 1, 1);

        market.SetState("signal_alpha", new TestSignal("Alpha1", 0.98), t0);
        market.SetState("scalar_val", 42.5, t0);

        // 导出快照并序列化为 JSON 字节
        byte[] bytes = market.ToJsonBytes();

        // 恢复至新黑板
        var restoredMarket = new MarketState();
        restoredMarket.RestoreFromJsonBytes(bytes);

        // 验证强类型反序列化能力
        Assert.IsTrue(restoredMarket.HasState<TestSignal>("signal_alpha"));
        var sig = restoredMarket.GetState<TestSignal>("signal_alpha");
        Assert.IsNotNull(sig);
        Assert.AreEqual("Alpha1", sig.Tag);
        Assert.AreEqual(0.98, sig.Score, 1e-6);

        Assert.IsTrue(restoredMarket.TryGetState<double>("scalar_val", out double val, out var time));
        Assert.AreEqual(42.5, val, 1e-6);
        Assert.AreEqual(t0, time);
    }

    [TestMethod]
    public void MarketState_SingletonAndMultiKvEntries_ShouldSerializeAndRestoreAccurately()
    {
        var market = new MarketState();
        var t0 = new MarketTime(2025, 1, 1);

        // 1. 显式 Key 存取复合对象
        var mainSignal = new TestSignal("GlobalMain", 100.0);
        market.SetState("main_signal", mainSignal, t0);

        // 2. 多 KV 模式（同类型 TestSignal 多个不同命名空间条目）
        var worker1 = new TestSignal("WorkerAlpha", 10.5);
        var worker2 = new TestSignal("WorkerBeta", 20.5);
        var worker3 = new TestSignal("WorkerGamma", 30.5);
        market.SetState("worker_1", worker1, t0);
        market.SetState("worker_2", worker2, t0);
        market.SetState("worker_3", worker3, t0);

        // 3. 标量数据存取（double、string、bool）
        market.SetState("scalar_threshold", 888.88, t0);
        market.SetState("scalar_status", "RiskOff_Triggered", t0);
        market.SetState("scalar_flag", true, t0);

        // 导出为 JSON 字节
        byte[] bytes = market.ToJsonBytes();
        Assert.IsTrue(bytes.Length > 0);

        // 恢复至全新的黑板实例
        var restored = new MarketState();
        restored.RestoreFromJsonBytes(bytes);

        // 4. 验证复合对象无损还原
        Assert.IsTrue(restored.HasState<TestSignal>("main_signal"));
        var restoredMain = restored.GetState<TestSignal>("main_signal");
        Assert.IsNotNull(restoredMain);
        Assert.AreEqual("GlobalMain", restoredMain.Tag);
        Assert.AreEqual(100.0, restoredMain.Score);

        // 5. 验证多 KV 模式各个条目互不干扰、精准还原
        Assert.IsTrue(restored.HasState<TestSignal>("worker_1"));
        Assert.IsTrue(restored.HasState<TestSignal>("worker_2"));
        Assert.IsTrue(restored.HasState<TestSignal>("worker_3"));

        var rWorker1 = restored.GetState<TestSignal>("worker_1");
        var rWorker2 = restored.GetState<TestSignal>("worker_2");
        var rWorker3 = restored.GetState<TestSignal>("worker_3");

        Assert.IsNotNull(rWorker1);
        Assert.IsNotNull(rWorker2);
        Assert.IsNotNull(rWorker3);
        Assert.AreEqual("WorkerAlpha", rWorker1.Tag);
        Assert.AreEqual(10.5, rWorker1.Score);
        Assert.AreEqual("WorkerBeta", rWorker2.Tag);
        Assert.AreEqual(20.5, rWorker2.Score);
        Assert.AreEqual("WorkerGamma", rWorker3.Tag);
        Assert.AreEqual(30.5, rWorker3.Score);

        // 6. 验证各标量精准还原且类型纯正（绝无 JsonElement 污染）
        Assert.IsTrue(restored.TryGetState<double>("scalar_threshold", out double valDbl, out _));
        Assert.AreEqual(888.88, valDbl, 1e-6);

        Assert.IsTrue(restored.TryGetState<string>("scalar_status", out string? valStr, out _));
        Assert.AreEqual("RiskOff_Triggered", valStr);

        Assert.IsTrue(restored.TryGetState<bool>("scalar_flag", out bool valBool, out _));
        Assert.IsTrue(valBool);
    }

    private class LimitPendingAndExecutionStrategy : IStrategy
    {
        public string Name => "LimitPendingAndExecutionStrategy";
        public long OrderIdBuy { get; private set; } = 0;
        public long OrderIdSell { get; private set; } = 0;
        public int Day3Position { get; private set; } = -1;

        public void OnStart(IEngineContext context) { }
        public void OnStop(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            // Day 0: 市价 10.0，下达 9.0 限价买单（挂单在 OrderBook 排队）
            if (context.CurrentIndex == 0)
            {
                OrderIdBuy = context.Broker.SubmitOrder(
                    new OrderRequest("SH.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 1000m));
            }
            // Day 3: 持仓达到后平仓
            else if (context.CurrentIndex == 3)
            {
                Day3Position = (int)context.Broker.GetPosition("SH.600000");
                if (context.Broker.GetPosition("SH.600000") > 0)
                {
                    OrderIdSell = context.Broker.SubmitOrder(
                        new OrderRequest("SH.600000", OrderDirection.Sell, OrderType.Market, 11.0, 1000m));
                }
            }
        }
    }

    [TestMethod]
    public void BacktestingEngine_ContinuousVsCheckpointRestore_ShouldProduceIdenticalOutcome()
    {
        // 1. 构造 5 个交易日的确定性行情矩阵
        var symbols = new[] { "SH.600000" };
        var dates = new[]
        {
            new DateTime(2025, 1, 2), // Day 0: Close 10.0 -> 下 9.0 限价单
            new DateTime(2025, 1, 3), // Day 1: Close 10.0 -> 挂单中 (在途，打检查点)
            new DateTime(2025, 1, 4), // Day 2: Close 9.0  -> 撮合成交！
            new DateTime(2025, 1, 5), // Day 3: Close 11.0 -> 卖出平仓
            new DateTime(2025, 1, 6)  // Day 4: Close 11.0 -> 终局结算
        };

        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);

        var closeData = new double[,] { { 10.0 }, { 10.0 }, { 9.0 }, { 11.0 }, { 11.0 } };
        var volumeData = new double[,] { { 100000.0 }, { 100000.0 }, { 100000.0 }, { 100000.0 }, { 100000.0 } };

        tsRegistry.SetMatrix("close", new MockMatrix(closeData));
        tsRegistry.SetMatrix("open", new MockMatrix(closeData));
        tsRegistry.SetMatrix("high", new MockMatrix(closeData));
        tsRegistry.SetMatrix("low", new MockMatrix(closeData));
        tsRegistry.SetMatrix("volume", new MockMatrix(volumeData));

        var brokerConfig = new BrokerConfig
        {
            InitialCash = 100_000.0,
            FeeRate = 0.0003,
            MinFee = 5.0,
            SettleDays = 0, // 当日可卖
            DefaultTimeInForce = TimeInForce.GoodTilCanceled
        };

        // --- 运行 A: 连续完整回测 (Day 0 ~ Day 4) ---
        var simExchangeA = new SimulatedExchangeGateway(brokerConfig);
        simExchangeA.OnMarketData("SH.600000", dates[0], new BarSnapshot(close: 10.0));
        var brokerA = new Broker(simExchangeA, brokerConfig);
        var strategyA = new LimitPendingAndExecutionStrategy();
        var pipelineA = StrategyPipeline.Sequential().Add(strategyA);
        var engineConfigA = new EngineConfig { StartDate = dates[0], EndDate = dates[^1] };

        using var engineA = new BacktestingEngine(tsRegistry, brokerA, pipelineA, config: engineConfigA, simExchange: simExchangeA);
        engineA.Run();
        Assert.AreEqual(EngineStatus.Completed, engineA.Status);
        var resultA = engineA.GetResult();

        // --- 运行 B: 运行至 Day 1（此时有在途挂单），保存检查点，恢复并续跑至 Day 4 ---
        var simExchangeB = new SimulatedExchangeGateway(brokerConfig);
        simExchangeB.OnMarketData("SH.600000", dates[0], new BarSnapshot(close: 10.0));
        var brokerB = new Broker(simExchangeB, brokerConfig);
        var strategyB1 = new LimitPendingAndExecutionStrategy();
        var pipelineB1 = StrategyPipeline.Sequential().Add(strategyB1);
        var engineConfigB1 = new EngineConfig { StartDate = dates[0], EndDate = dates[1] }; // 仅运行 Day 0 与 Day 1

        byte[] checkpointBytes;
        using (var engineB1 = new BacktestingEngine(tsRegistry, brokerB, pipelineB1, config: engineConfigB1, simExchange: simExchangeB))
        {
            engineB1.Run();
            Assert.AreEqual(EngineStatus.Completed, engineB1.Status);
            // Day 1 此时限价买单处于排队挂单在途状态
            Assert.IsTrue(brokerB.FrozenCash > 0, "断点时必须有在途预冻结资金");
            checkpointBytes = engineB1.SaveCheckpoint();
        }

        // 用恢复的检查点配置继续跑 Day 2 ~ Day 4
        var strategyB2 = new LimitPendingAndExecutionStrategy();
        var pipelineB2 = StrategyPipeline.Sequential().Add(strategyB2);
        var engineConfigB2 = new EngineConfig { StartDate = dates[0], EndDate = dates[^1] };
        using (var engineB2 = new BacktestingEngine(tsRegistry, brokerB, pipelineB2, config: engineConfigB2, simExchange: simExchangeB))
        {
            engineB2.RestoreCheckpoint(checkpointBytes);
            engineB2.Run();
            Assert.AreEqual(EngineStatus.Completed, engineB2.Status);

            var resultB = engineB2.GetResult();

            Assert.AreEqual(1000, strategyB2.Day3Position, "Day 3 持仓应该为 1000");
            Assert.AreNotEqual(0L, strategyB2.OrderIdSell, "Day 3 卖单应该已被提交");

            // 严格断言：连续回测 与 断点恢复回测 的产出 100% 绝对一致！
            Assert.AreEqual(resultA.ClosedTrades.Count, resultB.ClosedTrades.Count, "平仓笔数一致");
            Assert.AreEqual(1, resultA.ClosedTrades.Count);
            Assert.AreEqual(resultA.ClosedTrades[0].RealizedPnL, resultB.ClosedTrades[0].RealizedPnL, 1e-4, "平仓净收益 100% 恒等");
            Assert.AreEqual(resultA.ClosedTrades[0].Commission, resultB.ClosedTrades[0].Commission, 1e-4, "双边总手续费 100% 恒等");
            Assert.AreEqual(brokerA.AvailableCash, brokerB.AvailableCash, 1e-4, "终局可用现金 100% 恒等");
            Assert.AreEqual(brokerA.GetPosition("SH.600000"), brokerB.GetPosition("SH.600000"), "终局持仓 100% 恒等");
            Assert.AreEqual(0.0, brokerB.FrozenCash, 1e-4, "终局在途冻结必须完全释放");
        }
    }

    private class IndicatorTrackingStrategy : IStrategy
    {
        public string Name => "IndicatorTrackingStrategy";
        public int Lookback => 5;
        private IndicatorPool<Sma>? _smaPool;
        public List<(int Step, double Value, bool IsReady)> RecordedSma { get; } = new();

        public void OnStart(IEngineContext context)
        {
            _smaPool = context.Indicators.GetOrAdd<Sma>("sma3", () => new Sma(3));
        }

        public void OnStop(IEngineContext context) { }

        public void OnUpdate(IEngineContext context)
        {
            if (_smaPool != null)
            {
                var sma = _smaPool[0];
                RecordedSma.Add((context.CurrentIndex, sma.Current, sma.IsReady));
            }
        }
    }

    [TestMethod]
    public void BacktestingEngine_RestoreCheckpoint_IndicatorsPrewarming_ShouldAlignWithContinuousRun()
    {
        // 构造 10 个时间步的时序行情
        var symbols = new[] { "SH.600000" };
        var dates = new List<DateTime>();
        var closeData = new double[10, 1];
        var volumeData = new double[10, 1];

        for (int i = 0; i < 10; i++)
        {
            dates.Add(new DateTime(2025, 1, 1).AddDays(i));
            closeData[i, 0] = 10.0 + i * 2.0; // 10, 12, 14, 16, 18, 20, 22, 24, 26, 28
            volumeData[i, 0] = 10000.0;
        }

        var metadata = new MockMetadata(symbols, dates);
        var tsRegistry = new MockTimeseriesRegistry(metadata);
        tsRegistry.SetMatrix("close", new MockMatrix(closeData));
        tsRegistry.SetMatrix("open", new MockMatrix(closeData));
        tsRegistry.SetMatrix("high", new MockMatrix(closeData));
        tsRegistry.SetMatrix("low", new MockMatrix(closeData));
        tsRegistry.SetMatrix("volume", new MockMatrix(volumeData));

        var brokerConfig = new BrokerConfig { InitialCash = 100_000.0 };

        // 1. 连续回测：Day 0 ~ Day 9
        var simGatewayA = new SimulatedExchangeGateway(brokerConfig);
        var brokerA = new Broker(simGatewayA, brokerConfig);
        var strategyA = new IndicatorTrackingStrategy();
        var pipelineA = StrategyPipeline.Sequential().Add(strategyA);
        using var engineA = new BacktestingEngine(tsRegistry, brokerA, pipelineA, config: new EngineConfig { StartDate = dates[0], EndDate = dates[^1] }, simExchange: simGatewayA);
        engineA.Run();

        // 2. 断点分段回测：Day 0 ~ Day 4 保存检查点，恢复续跑 Day 5 ~ Day 9
        var simGatewayB = new SimulatedExchangeGateway(brokerConfig);
        var brokerB = new Broker(simGatewayB, brokerConfig);
        var strategyB1 = new IndicatorTrackingStrategy();
        var pipelineB1 = StrategyPipeline.Sequential().Add(strategyB1);
        byte[] checkpointBytes;
        using (var engineB1 = new BacktestingEngine(tsRegistry, brokerB, pipelineB1, config: new EngineConfig { StartDate = dates[0], EndDate = dates[4] }, simExchange: simGatewayB))
        {
            engineB1.Run();
            checkpointBytes = engineB1.SaveCheckpoint();
        }

        // 恢复续跑
        var strategyB2 = new IndicatorTrackingStrategy();
        var pipelineB2 = StrategyPipeline.Sequential().Add(strategyB2);
        using (var engineB2 = new BacktestingEngine(tsRegistry, brokerB, pipelineB2, config: new EngineConfig { StartDate = dates[0], EndDate = dates[^1] }, simExchange: simGatewayB))
        {
            engineB2.RestoreCheckpoint(checkpointBytes);
            engineB2.Run();

            // 严格断言续跑后每一步指标状态与值绝对对齐连续回测（消除冷启动）
            Assert.AreEqual(5, strategyB2.RecordedSma.Count, "续跑应记录 Day 5~Day 9 共 5 步");
            for (int k = 0; k < 5; k++)
            {
                var continuousPoint = strategyA.RecordedSma[k + 5];
                var restoredPoint = strategyB2.RecordedSma[k];

                Assert.AreEqual(continuousPoint.Step, restoredPoint.Step, $"Step {k + 5} 序号一致");
                Assert.IsTrue(restoredPoint.IsReady, $"Step {k + 5} 指标在续跑后必须即时保持 Ready 状态");
                Assert.AreEqual(continuousPoint.IsReady, restoredPoint.IsReady, $"Step {k + 5} 就绪状态一致");
                Assert.AreEqual(continuousPoint.Value, restoredPoint.Value, 1e-4, $"Step {k + 5} SMA 指标值绝对一致");
            }
        }
    }
}
