using System;
using System.Collections.Generic;
using CarrotBacktesting.NET;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class EnginePhaseAndAuctionTests
{
    private static (ITimeseriesRegistry, IReadOnlyList<MarketTime>) CreateMockTimeseries()
    {
        var dates = new MarketTime[]
        {
            (MarketTime)new DateTime(2024, 1, 2),
            (MarketTime)new DateTime(2024, 1, 3),
            (MarketTime)new DateTime(2024, 1, 4)
        };
        var symbols = new[] { "sh.600000" };
        var meta = new DynamicMarketMetadata(symbols, dates);
        var matrices = new Dictionary<string, IReadOnlyMatrix2D<double>>(StringComparer.OrdinalIgnoreCase);

        ChunkMatrix2D<double> CreateMatrix(double[] values)
        {
            var mat = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 3);
            for (int r = 0; r < 3; r++) mat[r, 0] = values[r];
            return mat;
        }

        matrices[StandardFields.Open] = CreateMatrix(new[] { 10.0, 10.0, 9.9 });
        matrices[StandardFields.High] = CreateMatrix(new[] { 10.5, 10.8, 10.2 });
        matrices[StandardFields.Low] = CreateMatrix(new[] { 9.8, 9.0, 9.5 });
        matrices[StandardFields.Close] = CreateMatrix(new[] { 10.2, 9.8, 10.0 });
        matrices[StandardFields.Volume] = CreateMatrix(new[] { 1000.0, 2000.0, 1500.0 });

        var ts = new SimulatedExchangeGatewayTests.MultiMatrixTimeseriesRegistry(meta, matrices);
        return (ts, dates);
    }

    [TestMethod]
    public void EnginePhaseSecurity_AllPhases_FieldAccessPermissions()
    {
        var (ts, dates) = CreateMockTimeseries();
        var broker = new CarrotBacktesting.NET.Engine.Broker.Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
        var ctx = new EngineContext(ts, broker, null);
        ctx.UpdateStep(1, dates[1]); // Step 1

        // 1. 未来偏移 offset > 0 在任何相位均应抛出 ArgumentOutOfRangeException
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => ctx.GetClose(0, offset: 1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => ctx.GetOpen(0, offset: 1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => ctx.GetHigh(0, offset: 1));

        // 2. PreMarket 盘前相位：当日所有行情均未产生，读取 offset == 0 坚决拦截
        ctx.Phase = EnginePhase.PreMarket;
        Assert.AreEqual(EnginePhase.PreMarket, ctx.Phase);
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetOpen(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetClose(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetHigh(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetLow(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetVolume(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetField(0, "close", 0));
        // 允许读取历史 offset < 0
        Assert.AreEqual(10.2, ctx.GetClose(0, -1));

        // 3. Open 相位：仅允许读取当期 Open，其余一律拦截
        ctx.Phase = EnginePhase.Open;
        Assert.AreEqual(EnginePhase.Open, ctx.Phase);
        Assert.AreEqual(10.0, ctx.GetOpen(0, 0));
        Assert.AreEqual(10.0, ctx.GetField(0, "open", 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetClose(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetHigh(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetLow(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetVolume(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetField(0, "close", 0));

        // 4. Intraday 相位：仅允许读取当期 Open，未收盘前严禁偷看收盘极值
        ctx.Phase = EnginePhase.Intraday;
        Assert.AreEqual(EnginePhase.Intraday, ctx.Phase);
        Assert.AreEqual(10.0, ctx.GetOpen(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetClose(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetHigh(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetLow(0, 0));
        Assert.ThrowsException<InvalidOperationException>(() => ctx.GetVolume(0, 0));

        // 5. Close 相位：当期 Bar 完整闭合，全字段允许访问
        ctx.Phase = EnginePhase.Close;
        Assert.AreEqual(EnginePhase.Close, ctx.Phase);
        Assert.AreEqual(10.0, ctx.GetOpen(0, 0));
        Assert.AreEqual(10.8, ctx.GetHigh(0, 0));
        Assert.AreEqual(9.0,  ctx.GetLow(0, 0));
        Assert.AreEqual(9.8,  ctx.GetClose(0, 0));
        Assert.AreEqual(2000.0, ctx.GetVolume(0, 0));

        // 6. PostMarket 相位：盘后结算，全字段允许访问
        ctx.Phase = EnginePhase.PostMarket;
        Assert.AreEqual(EnginePhase.PostMarket, ctx.Phase);
        Assert.AreEqual(9.8, ctx.GetClose(0, 0));
    }

    [TestMethod]
    public void BarOpen_LimitOrder_BuyBelowOpen_DoesNotFillAtOpenEvenIfLowTouched()
    {
        // 场景：在途限价买单委托价为 9.5 元。
        // 当日行情：Open = 10.0, Low = 9.0, High = 10.8, Close = 9.8。
        // Bar 初始时态 (Open 相位)：Open = 10.0 > 9.5，买单在开盘不能成交，严禁偷看当期 Bar 的 Low=9.0！
        // 收盘撮合 (Close 相位)：全天 Low=9.0 <= 9.5，限价买单在收盘相位正常触碰成交。
        var matcher = new MatchingEngine(new BrokerConfig());
        var order = new SimulatedOrder(1001, new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 100m), DateTime.UtcNow);
        var bar = new BarSnapshot(open: 10.0, high: 10.8, low: 9.0, close: 9.8, volume: 2000.0);

        // 1. Bar 初始时态开盘切片撮合 (Phase = EnginePhase.Open)
        var openMatch = matcher.EvaluatePendingLimitOrder(order, 10.0, in bar, DateTime.UtcNow, EnginePhase.Open);
        Assert.IsFalse(openMatch.IsFilled, "限价买单价 9.5 低于开盘价 10.0，开盘切片严禁偷看当期 Bar 的 Low=9.0 判定成交！");

        // 2. 收盘阶段撮合 (Phase = EnginePhase.Close)
        var closeMatch = matcher.EvaluatePendingLimitOrder(order, 9.8, in bar, DateTime.UtcNow, EnginePhase.Close);
        Assert.IsTrue(closeMatch.IsFilled, "当期真实波幅 Low=9.0 触碰限价 9.5，收盘相位应正常判定成交");
        Assert.AreEqual(9.5, closeMatch.FillPrice, 1e-4);
    }

    [TestMethod]
    public void BarOpen_LimitOrder_BuyAboveOpen_EnjoysPriceImprovement()
    {
        // 场景：隔夜限价买单委托价 10.5 元。
        // 当日跳空低开：Open = 10.0。
        // Bar 初始时态 (Open 相位)：买单 10.5 >= 10.0 满足开盘切片成交条件，并以更优开盘价 10.0 成交！
        var matcher = new MatchingEngine(new BrokerConfig());
        var order = new SimulatedOrder(1002, new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.5, 100m), DateTime.UtcNow);
        var bar = new BarSnapshot(open: 10.0, high: 10.8, low: 9.8, close: 10.2, volume: 2000.0);

        var match = matcher.EvaluatePendingLimitOrder(order, 10.0, in bar, DateTime.UtcNow, EnginePhase.Open);
        Assert.IsTrue(match.IsFilled, "限价买单委托价 10.5 高于开盘价 10.0，开盘切片撮合应判定成交");
        Assert.AreEqual(10.0, match.FillPrice, 1e-4, "成交价应享受开盘跳空价格改善（按 10.0 成交）");
    }

    [TestMethod]
    public void BarOpen_LimitOrder_SellAboveOpen_DoesNotFillAtOpenEvenIfHighTouched()
    {
        // 场景：在途限价卖单委托价 10.5 元。
        // 当日行情：Open = 10.0, High = 10.8, Low = 9.8, Close = 10.0。
        // Bar 初始时态 (Open 相位)：卖价 10.5 > Open 10.0，不满足开盘成交条件，严禁偷看当期 Bar 的 High=10.8！
        // 收盘撮合 (Close 相位)：当期 High=10.8 >= 10.5，收盘相位触碰成交。
        var matcher = new MatchingEngine(new BrokerConfig());
        var order = new SimulatedOrder(1003, new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.5, 100m), DateTime.UtcNow);
        var bar = new BarSnapshot(open: 10.0, high: 10.8, low: 9.8, close: 10.0, volume: 2000.0);

        // 1. Bar 初始时态开盘切片撮合
        var openMatch = matcher.EvaluatePendingLimitOrder(order, 10.0, in bar, DateTime.UtcNow, EnginePhase.Open);
        Assert.IsFalse(openMatch.IsFilled, "限价卖单价 10.5 高于开盘价 10.0，开盘切片撮合严禁偷看当期 Bar 的 High=10.8 判定成交！");

        // 2. 收盘阶段撮合
        var closeMatch = matcher.EvaluatePendingLimitOrder(order, 10.0, in bar, DateTime.UtcNow, EnginePhase.Close);
        Assert.IsTrue(closeMatch.IsFilled, "当期真实波幅 High=10.8 触碰卖单限价 10.5，收盘相位应正常成交");
        Assert.AreEqual(10.5, closeMatch.FillPrice, 1e-4);
    }

    [TestMethod]
    public void BarOpen_LimitOrder_SellBelowOpen_EnjoysPriceImprovement()
    {
        // 场景：隔夜限价卖单委托价 9.5 元。
        // 当日跳空高开：Open = 10.0。
        // Bar 初始时态 (Open 相位)：卖单 9.5 <= 10.0 满足开盘成交条件，并以更优开盘价 10.0 成交！
        var matcher = new MatchingEngine(new BrokerConfig());
        var order = new SimulatedOrder(1004, new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 9.5, 100m), DateTime.UtcNow);
        var bar = new BarSnapshot(open: 10.0, high: 10.8, low: 9.8, close: 10.2, volume: 2000.0);

        var match = matcher.EvaluatePendingLimitOrder(order, 10.0, in bar, DateTime.UtcNow, EnginePhase.Open);
        Assert.IsTrue(match.IsFilled, "限价卖单委托价 9.5 低于开盘价 10.0，开盘切片撮合应判定成交");
        Assert.AreEqual(10.0, match.FillPrice, 1e-4, "成交价应享受开盘跳空价格改善（按 10.0 成交）");
    }

    [TestMethod]
    public void BacktestingEngine_PhaseVerification_DuringExecution()
    {
        // 验证回测引擎在 Close 与 Open 相位驱动时，策略在 OnUpdate 中读取到的 context.Phase 严格准确
        var (ts, _) = CreateMockTimeseries();

        // 1. 验证默认 Close 相位驱动
        EnginePhase observedClosePhase = EnginePhase.PreMarket;
        var closeStrat = new CustomActionStrategy(ctx => observedClosePhase = ctx.Phase, EnginePhase.Close);
        var sim1 = new SimulatedExchangeGateway(new BrokerConfig());
        var broker1 = new CarrotBacktesting.NET.Engine.Broker.Broker(sim1, new BrokerConfig());
        using var engine1 = new BacktestingEngine(ts, broker1, StrategyPipeline.Sequential().Add(closeStrat), simExchange: sim1);
        engine1.Run();
        Assert.AreEqual(EnginePhase.Close, observedClosePhase, "Close 相位策略在 OnUpdate 触发时 context.Phase 必须为 EnginePhase.Close");

        // 2. 验证 Open 相位驱动
        EnginePhase observedOpenPhase = EnginePhase.PreMarket;
        var openStrat = new CustomActionStrategy(ctx => observedOpenPhase = ctx.Phase, EnginePhase.Open);
        var sim2 = new SimulatedExchangeGateway(new BrokerConfig());
        var broker2 = new CarrotBacktesting.NET.Engine.Broker.Broker(sim2, new BrokerConfig());
        using var engine2 = new BacktestingEngine(ts, broker2, StrategyPipeline.Sequential().Add(openStrat), simExchange: sim2);
        engine2.Run();
        Assert.AreEqual(EnginePhase.Open, observedOpenPhase, "Open 相位策略在 OnUpdate 触发时 context.Phase 必须为 EnginePhase.Open");
    }

    private sealed class CustomActionStrategy : StrategyBase
    {
        private readonly Action<IEngineContext> _action;
        public override EnginePhase UpdatePhase { get; }

        public CustomActionStrategy(Action<IEngineContext> action, EnginePhase phase)
        {
            _action = action;
            UpdatePhase = phase;
        }

        public override void OnUpdate(IEngineContext context) => _action(context);
    }

    [TestMethod]
    public void StrategyPipeline_ConflictingUpdatePhase_ThrowsInvalidOperationException()
    {
        // 场景：管线内子策略 UpdatePhase 冲突（一个 Open，一个 Close），Compile 时必须阻断报错
        var s1 = new CustomTimingStrategy("OpenStrat", EnginePhase.Open);
        var s2 = new CustomTimingStrategy("CloseStrat", EnginePhase.Close);

        var pipeline = StrategyPipeline.Sequential().Add(s1).Add(s2);

        var ex = Assert.ThrowsException<InvalidOperationException>(() => pipeline.Compile());
        StringAssert.Contains(ex.Message, "时态冲突");
    }

    [TestMethod]
    public void StrategyPipeline_UniformUpdatePhase_CompilesAndInheritsPhase()
    {
        var s1 = new CustomTimingStrategy("OpenStrat1", EnginePhase.Open);
        var s2 = new CustomTimingStrategy("OpenStrat2", EnginePhase.Open);

        var pipeline = StrategyPipeline.Sequential().Add(s1).Add(s2);
        pipeline.Compile();

        Assert.AreEqual(EnginePhase.Open, pipeline.UpdatePhase, "管线应统一继承子策略一致的 UpdatePhase");
    }

    [TestMethod]
    public void StrategyPipeline_UnsupportedUpdatePhase_ThrowsNotSupportedException()
    {
        // 场景：策略配置为 PreMarket / Intraday / PostMarket 这种非决策相位，管线 Compile 时必须 Fail-Fast 阻断，杜绝静默空跑
        var s = new CustomTimingStrategy("PreMarketStrat", EnginePhase.PreMarket);
        var pipeline = StrategyPipeline.Sequential().Add(s);

        var ex = Assert.ThrowsException<NotSupportedException>(() => pipeline.Compile());
        StringAssert.Contains(ex.Message, "策略时态非法");
    }

    private sealed class CustomTimingStrategy : StrategyBase
    {
        public override string Name { get; }
        public override EnginePhase UpdatePhase { get; }

        public CustomTimingStrategy(string name, EnginePhase phase)
        {
            Name = name;
            UpdatePhase = phase;
        }

        public override void OnUpdate(IEngineContext context) { }
    }
}
