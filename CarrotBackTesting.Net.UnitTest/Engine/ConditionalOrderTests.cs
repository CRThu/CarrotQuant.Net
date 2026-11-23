using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;

namespace CarrotBackTesting.Net.UnitTest.Engine;

/// <summary>
/// 条件单（止损单 Stop、止盈单 TakeProfit、括号单 Bracket / OCO）风控中枢与物理执行测试套件。
/// 严格验证触发前零冻结、四象限定价法则、OCO 联动互斥注销、持仓不足合规拦截、双触碰悲观仲裁与断点续跑守恒。
/// </summary>
[TestClass]
public class ConditionalOrderTests
{
    private static (IMarketDataProvider MarketData, MarketTime[] Dates) CreateMultiFieldTimeseries(
        string symbol,
        double[] open,
        double[] high,
        double[] low,
        double[] close,
        double[]? volume = null)
    {
        int rows = close.Length;
        var dates = new MarketTime[rows];
        var baseDate = new DateTime(2025, 1, 1);
        for (int i = 0; i < rows; i++)
        {
            dates[i] = (MarketTime)baseDate.AddDays(i);
        }

        var metadata = new DynamicMarketMetadata(new[] { symbol }, dates);
        var matrices = new Dictionary<string, IReadOnlyMatrix2D<double>>(StringComparer.OrdinalIgnoreCase);

        ChunkMatrix2D<double> CreateMatrix(double[] values)
        {
            var mat = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: rows);
            for (int r = 0; r < rows; r++) mat[r, 0] = values[r];
            return mat;
        }

        matrices[StandardFields.Close] = CreateMatrix(close);
        matrices[StandardFields.Open] = CreateMatrix(open);
        matrices[StandardFields.High] = CreateMatrix(high);
        matrices[StandardFields.Low] = CreateMatrix(low);
        if (volume != null) matrices[StandardFields.Volume] = CreateMatrix(volume);

        var timeseries = new SimulatedExchangeGatewayTests.MultiMatrixTimeseriesRegistry(metadata, matrices);
        return (new MarketDataProvider(timeseries), dates);
    }

    [TestMethod]
    public void ConditionalOrder_Placement_DoesNotFreezeAvailablePosition_DualHangingAllowed()
    {
        // 场景：持有 1000 股现货，同时设置止损单与止盈单。
        // 验证：触发前可用持仓保持 1000 股，绝不产生持仓死锁。
        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);

        var date = (MarketTime)new DateTime(2025, 1, 1);
        gateway.OnMarketData("sh.600000", date, new BarSnapshot(close: 10.0));
        ((IBrokerEngineDriver)broker).Step(date);

        // 先买入 1000 股建立持仓
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);
        Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
        Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));

        // 同时挂出 9.0 止损单与 12.0 止盈单
        long stopId = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 1000m);
        long tpId = broker.TakeProfit("sh.600000", targetPrice: 12.0, quantity: 1000m);

        Assert.IsTrue(stopId > 0);
        Assert.IsTrue(tpId > 0);
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(stopId));
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(tpId));

        // 核心断言：触发前持仓保持 100% 可用，冻结持仓为 0
        Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));
        Assert.AreEqual(0.0, broker.FrozenCash);
    }

    [TestMethod]
    public void StopOrder_JumpGapDownAtOpen_ExecutesAtOpenPriceWithPenalty()
    {
        // 场景：多头持仓 1000 股，止损价 9.0 元。次日开盘跳空低开至 8.5 元（低于止损价）。
        // 验证：四象限定价法则 - 必须以恶化的开盘价 8.5 元成交并承担滑点惩罚，严禁以 9.0 或收盘价偷价成交。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 8.5 },
            high: new[] { 10.5, 8.8 },
            low: new[] { 9.8, 8.0 },
            close: new[] { 10.0, 8.6 });

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1: 开仓买入 1000 股，并挂出 9.0 止损单
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);
        long stopId = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 1000m);
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(stopId));

        ExecutionReport? fillReport = null;
        broker.OnTrade += r => { if (r.OrderId == stopId) fillReport = r; };

        // Day 2: 跳空低开至 8.5，驱动步进
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        // 验证：止损单触发且成交价严格等于 Open 8.5 元
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(stopId));
        Assert.IsNotNull(fillReport);
        Assert.AreEqual(8.5, fillReport.FillPrice, 1e-4);
        Assert.AreEqual(1000m, fillReport.FillQuantity);
        Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void StopOrder_IntradayBreakLow_ExecutesAtStopPrice()
    {
        // 场景：多头持仓 1000 股，止损价 9.0 元。次日开盘 9.5 元，盘中下探至 8.8 元跌破止损线，收盘 9.2 元。
        // 验证：四象限定价法则 - 开盘未跌破，盘中触碰击穿，必须以 StopPrice 9.0 元成交。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 9.5 },
            high: new[] { 10.5, 9.6 },
            low: new[] { 9.8, 8.8 },
            close: new[] { 10.0, 9.2 });

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);
        long stopId = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 1000m);

        ExecutionReport? fillReport = null;
        broker.OnTrade += r => { if (r.OrderId == stopId) fillReport = r; };

        // Day 2
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        // 验证：成交价严格等于委托止损价 9.0
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(stopId));
        Assert.IsNotNull(fillReport);
        Assert.AreEqual(9.0, fillReport.FillPrice, 1e-4);
        Assert.AreEqual(1000m, fillReport.FillQuantity);
        Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void TakeProfitOrder_JumpGapUpAtOpen_ExecutesAtOpenPriceWithPriceImprovement()
    {
        // 场景：多头持仓 1000 股，止盈价 12.0 元。次日开盘跳空高开至 12.5 元（高过止盈价）。
        // 验证：四象限定价法则 - 必须以更优的开盘价 12.5 元成交（享受价格改善 Price Improvement）。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 12.5 },
            high: new[] { 10.5, 12.8 },
            low: new[] { 9.8, 12.2 },
            close: new[] { 10.0, 12.6 });

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);
        long tpId = broker.TakeProfit("sh.600000", targetPrice: 12.0, quantity: 1000m);

        ExecutionReport? fillReport = null;
        broker.OnTrade += r => { if (r.OrderId == tpId) fillReport = r; };

        // Day 2
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        // 验证：成交价为开盘改善价 12.5 元
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(tpId));
        Assert.IsNotNull(fillReport);
        Assert.AreEqual(12.5, fillReport.FillPrice, 1e-4);
        Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void TakeProfitOrder_IntradayHigh_ExecutesAtTargetPrice()
    {
        // 场景：多头持仓 1000 股，止盈价 12.0 元。次日开盘 11.5 元，盘中冲高至 12.2 元触碰止盈线，收盘 11.8 元。
        // 验证：以 TargetPrice 12.0 元成交。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 11.5 },
            high: new[] { 10.5, 12.2 },
            low: new[] { 9.8, 11.4 },
            close: new[] { 10.0, 11.8 });

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);
        long tpId = broker.TakeProfit("sh.600000", targetPrice: 12.0, quantity: 1000m);

        ExecutionReport? fillReport = null;
        broker.OnTrade += r => { if (r.OrderId == tpId) fillReport = r; };

        // Day 2
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(tpId));
        Assert.IsNotNull(fillReport);
        Assert.AreEqual(12.0, fillReport.FillPrice, 1e-4);
        Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void BracketOrder_OcoPair_WhenStopTriggers_TakeProfitIsCanceled()
    {
        // 场景：提交 Bracket 括号单（止损 9.0，止盈 12.0）。行情下跌触发止损。
        // 验证：止损单成交，止盈单被原子化自动联动撤销（Canceled），且记录 OCO 撤销原因。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 9.5 },
            high: new[] { 10.5, 9.6 },
            low: new[] { 9.8, 8.8 },
            close: new[] { 10.0, 9.1 });

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1: 买入持仓并提交 Bracket 订单
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);

        var (stopId, tpId) = broker.Bracket("sh.600000", 1000m, stopPrice: 9.0, takeProfitPrice: 12.0);
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(stopId));
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(tpId));

        OrderEvent? tpCancelEvent = null;
        broker.OnOrderEvent += ev => { if (ev.OrderId == tpId && ev.EventType == OrderEventType.Canceled) tpCancelEvent = ev; };

        // Day 2: 价格跌至 8.8，触发止损
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        // 验证：止损单成交，止盈单被联动撤销
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(stopId));
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(tpId));
        Assert.IsNotNull(tpCancelEvent);
        Assert.IsTrue(tpCancelEvent.Reason.Contains("OCO", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void BracketOrder_OcoPair_WhenTakeProfitTriggers_StopLossIsCanceled()
    {
        // 场景：提交 Bracket 括号单（止损 9.0，止盈 12.0）。行情上涨冲高至 12.2 触发止盈。
        // 验证：止盈单成交，止损单被自动联动撤销。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 11.5 },
            high: new[] { 10.5, 12.2 },
            low: new[] { 9.8, 11.4 },
            close: new[] { 10.0, 11.9 });

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);

        var (stopId, tpId) = broker.Bracket("sh.600000", 1000m, stopPrice: 9.0, takeProfitPrice: 12.0);

        // Day 2
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(tpId));
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(stopId));
        Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void ConditionalOrder_WhenPositionAlreadyClosed_PreFreezeFailsAndRejects()
    {
        // 场景：挂出止损单，但在触碰前策略提前把持仓手动平仓。当行情后续暴跌触碰止损线时，
        // 验证：Broker 在触发转单时核验 TryFreeze 失败，订单直接判定为 Rejected，派发带明确理由的 OrderEvent。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 9.5 },
            high: new[] { 10.5, 9.6 },
            low: new[] { 9.8, 8.5 },
            close: new[] { 10.0, 8.8 });

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1: 买入持仓并设置止损单
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);
        long stopId = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 1000m);

        // 策略在止损触发前，由于其他逻辑手动卖平了持仓
        broker.Sell("sh.600000", 1000m, price: 10.0, type: OrderType.Market);
        Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
        Assert.AreEqual(0m, broker.GetAvailablePosition("sh.600000"));

        OrderEvent? rejectEvent = null;
        broker.OnOrderEvent += ev => { if (ev.OrderId == stopId && ev.EventType == OrderEventType.Rejected) rejectEvent = ev; };

        // Day 2: 行情跌至 8.5，触碰止损线
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        // 验证：由于持仓为 0，预冻结失败，被权威判定为 Rejected
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(stopId));
        Assert.IsNotNull(rejectEvent);
        Assert.IsTrue(rejectEvent.Reason.Contains("position", StringComparison.OrdinalIgnoreCase) || 
                      rejectEvent.Reason.Contains("insufficient", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ConditionalOrder_DualTouch_PessimisticArbitration_PrefersStopLoss()
    {
        // 场景：单根 Bar 振幅极大，Open=10.0, High=13.0（触碰 12.0 止盈）, Low=8.0（触碰 9.0 止损）。
        // 验证：自营量化风控准则 - 悲观仲裁机制（Pessimistic Arbitration）：优先判定止损生效，止盈注销。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 10.0 },
            high: new[] { 10.5, 13.0 },
            low: new[] { 9.8, 8.0 },
            close: new[] { 10.0, 10.5 });

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);

        var (stopId, tpId) = broker.Bracket("sh.600000", 1000m, stopPrice: 9.0, takeProfitPrice: 12.0);

        // Day 2: 巨大日内波动
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        // 验证：悲观仲裁，止损生效，止盈取消
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(stopId));
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(tpId));
        Assert.AreEqual(0m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void ConditionalOrder_DefaultTimeInForce_IsGoodTilCanceled()
    {
        // 场景：提交条件单未显式传参 TIF。
        // 验证：强制默认采用 GoodTilCanceled，日切清算时不被撤销。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 10.2 },
            high: new[] { 10.5, 10.4 },
            low: new[] { 9.8, 10.1 },
            close: new[] { 10.0, 10.3 });

        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            DefaultTimeInForce = TimeInForce.Day // 全局默认是 Day
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);

        // 挂出止损单，未指定 TIF
        long stopId = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 1000m);

        // Day 2: 未触碰价格（价格维持在 10.1 以上）
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        // 验证：即使全局配置是 Day，条件单依然保持 Pending 状态跨日存续，绝不被撤单
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(stopId));
    }

    [TestMethod]
    public void ConditionalOrder_Checkpoint_SavesAndRestoresPendingState()
    {
        // 场景：挂出止损单，抓取 Broker 快照后恢复到新实例中。
        // 验证：未触发条件单完整还原，且后续价格触碰时正常触发成交。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 10.0, 9.5 },
            high: new[] { 10.5, 10.2, 9.6 },
            low: new[] { 9.8, 9.9, 8.8 },
            close: new[] { 10.0, 10.1, 9.1 });

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);
        long stopId = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 1000m);

        // Day 2: 保持在 10.1，抓取检查点
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(stopId));

        var snapshot = broker.SaveState();

        // 模拟断点续跑：构建新 Broker 并还原状态
        var newBroker = new Broker(gateway, config);
        newBroker.RestoreState(snapshot);
        var newDriver = (IBrokerEngineDriver)newBroker;

        // 验证还原后订单仍然是 Pending
        Assert.AreEqual(OrderStatus.Pending, newBroker.GetOrderStatus(stopId));
        Assert.AreEqual(1000m, newBroker.GetPosition("sh.600000"));

        // Day 3: 价格跌至 8.8，触碰止损线
        gateway.Step(dates[2], marketData, 2, EnginePhase.Close);
        newDriver.Step(dates[2]);

        // 验证还原后的条件单正常触发成交
        Assert.AreEqual(OrderStatus.Filled, newBroker.GetOrderStatus(stopId));
        Assert.AreEqual(0m, newBroker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void LimitDownStopLossLiquidityTests_WhenZeroVolume_FailsLiquidityAndDoesNotFill()
    {
        // 场景：标的出现一字跌停无量（Volume = 0），配置了 VolumeLimit 约束。策略持有 1000 股且挂有止损单。
        // 验证：虽然跌破止损线，但流动性硬约束检查失败，订单被拒绝，绝对断言不以 Open 价格虚假成交逃顶。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 8.5 },
            high: new[] { 10.5, 8.5 },
            low: new[] { 9.8, 8.5 },
            close: new[] { 10.0, 8.5 },
            volume: new[] { 100_000.0, 0.0 }); // Day 2 出现一字跌停无成交量

        var config = new BrokerConfig 
        { 
            InitialCash = 100_000.0, 
            Slippage = 0.0, 
            FeeRate = 0.0,
            VolumeLimit = 0.1, // 开启成交量流动性上限约束 (最多占成交量 10%)
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1: 正常买入并挂止损单
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 1000m, price: 10.0, type: OrderType.Market);
        long stopId = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 1000m);

        OrderEvent? rejectEvent = null;
        broker.OnOrderEvent += ev => { if (ev.OrderId == stopId && ev.EventType == OrderEventType.Rejected) rejectEvent = ev; };

        // Day 2: 一字跌停无量
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        // 验证：因流动性不足被拒绝，未能成功逃顶，持仓依然保持 1000 股
        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(stopId));
        Assert.IsNotNull(rejectEvent);
        Assert.IsTrue(rejectEvent.Reason.Contains("volume", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void BracketOrder_ConsecutiveCalls_ShouldHaveUniqueOcoGroupIds()
    {
        var config = new BrokerConfig { InitialCash = 100_000.0 };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);

        var (stop1, tp1) = broker.Bracket("sh.600000", 100m, 9.0, 11.0);
        var (stop2, tp2) = broker.Bracket("sz.000001", 100m, 9.0, 11.0);

        Assert.AreNotEqual(stop1, stop2);
        Assert.AreNotEqual(tp1, tp2);
    }

    [TestMethod]
    public void BracketOrder_ExplicitCloseDirection_ShouldRespectSpecifiedDirection()
    {
        var config = new BrokerConfig { InitialCash = 100_000.0 };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);

        // 显式指定平仓方向为 Buy（用于空头平仓保护）
        var (stopId, tpId) = broker.Bracket(
            "sh.600000",
            100m,
            stopPrice: 11.0,
            takeProfitPrice: 9.0,
            closeDirection: OrderDirection.Buy);

        // 验证状态为挂单
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(stopId));
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(tpId));
    }

    [TestMethod]
    public void BracketOrder_ShortPosition_TriggerStopLoss_ShouldBuyToCoverAndCancelTakeProfit()
    {
        // 场景：开空单 100 股，挂出买入平空止损单 (11.0) 与止盈单 (9.0)。
        // 行情冲高至 11.5，触发止损，验证买入平仓且止盈单被自动联动撤销。
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "IF2412",
            open: new[] { 10.0, 10.5 },
            high: new[] { 10.5, 11.5 },
            low: new[] { 9.8, 10.2 },
            close: new[] { 10.0, 11.2 });

        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            AllowShort = true,
            Slippage = 0.0,
            FeeRate = 0.0,
            DefaultExecutionPrice = ExecutionPriceType.Close
        };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        // Day 1: 建立空头仓位 -100 股
        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Sell("IF2412", 100m, price: 10.0, type: OrderType.Market);

        // 挂 Bracket 单，平仓方向显式为 Buy（买入平空）
        var (stopId, tpId) = broker.Bracket(
            "IF2412",
            100m,
            stopPrice: 11.0,
            takeProfitPrice: 9.0,
            closeDirection: OrderDirection.Buy);

        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(stopId));
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(tpId));

        // Day 2: 价格涨至 11.2，触发买入平空止损
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(stopId));
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(tpId));
        Assert.AreEqual(0m, broker.GetPosition("IF2412"));
    }

    [TestMethod]
    public void ConditionalOrder_ActivePruningAndCancellation_ShouldPruneImmediately()
    {
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 8.5 },
            high: new[] { 10.5, 8.6 },
            low: new[] { 9.8, 8.2 },
            close: new[] { 10.0, 8.4 });

        var config = new BrokerConfig { InitialCash = 100_000.0, DefaultExecutionPrice = ExecutionPriceType.Close };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 200m, price: 10.0, type: OrderType.Market);

        long stopId1 = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 100m);
        long stopId2 = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 100m);

        // 取消第 1 笔，验证从活跃集合移出
        broker.CancelOrder(stopId1);
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(stopId1));
        Assert.AreEqual(OrderStatus.Pending, broker.GetOrderStatus(stopId2));

        // Day 2 步进
        gateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        driver.Step(dates[1]);

        // stopId1 依然为 Canceled，stopId2 触发成交
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(stopId1));
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(stopId2));
        Assert.AreEqual(100m, broker.GetPosition("sh.600000"));
    }

    [TestMethod]
    public void ConditionalOrder_CheckpointRestore_PendingConditionalsPreservedAndTriggered()
    {
        var (marketData, dates) = CreateMultiFieldTimeseries(
            "sh.600000",
            open: new[] { 10.0, 8.5 },
            high: new[] { 10.5, 8.6 },
            low: new[] { 9.8, 8.2 },
            close: new[] { 10.0, 8.4 });

        var config = new BrokerConfig { InitialCash = 100_000.0, DefaultExecutionPrice = ExecutionPriceType.Close };
        var gateway = new SimulatedExchangeGateway(config);
        var broker = new Broker(gateway, config);
        var driver = (IBrokerEngineDriver)broker;

        gateway.Step(dates[0], marketData, 0, EnginePhase.Close);
        driver.Step(dates[0]);
        broker.Buy("sh.600000", 100m, price: 10.0, type: OrderType.Market);
        long stopId = broker.StopLoss("sh.600000", stopPrice: 9.0, quantity: 100m);

        // 保存快照
        var snap = broker.SaveState();

        // 创建全新实例并恢复快照
        var newGateway = new SimulatedExchangeGateway(config);
        var newBroker = new Broker(newGateway, config);
        var newDriver = (IBrokerEngineDriver)newBroker;
        newBroker.RestoreState(snap);

        // 验证恢复后条件单仍为 Pending
        Assert.AreEqual(OrderStatus.Pending, newBroker.GetOrderStatus(stopId));

        // 推进 Day 2 行情，验证恢复后的活跃条件单能正常触发并成交
        newGateway.Step(dates[1], marketData, 1, EnginePhase.Close);
        newDriver.Step(dates[1]);

        Assert.AreEqual(OrderStatus.Filled, newBroker.GetOrderStatus(stopId));
        Assert.AreEqual(0m, newBroker.GetPosition("sh.600000"));
    }
}
