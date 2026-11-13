using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Strategy;

[TestClass]
public class RuleStrategyBaseTests
{
    private class FakeBroker : IBroker
    {
        private readonly Dictionary<string, decimal> _positions = new();

        public void SetPosition(string symbol, decimal quantity) => _positions[symbol] = quantity;
        public decimal GetPosition(string symbol) => _positions.TryGetValue(symbol, out var q) ? q : 0m;

        public double GetCash() => 100000.0;
        public string SubmitOrder(OrderRequest request) => "ord-1";
        public void CancelOrder(string orderId) { }
        public OrderStatus GetOrderStatus(string orderId) => OrderStatus.Filled;

#pragma warning disable CS0067
        public event Action<string, OrderStatus>? OnOrderStatusChanged;
        public event Action<ExecutionReport>? OnTrade;
        public event Action<PositionEvent>? OnPositionChanged;
#pragma warning restore CS0067
    }

    private class FakeEngineContext : IEngineContext
    {
        public DateTime CurrentTime { get; set; } = DateTime.UtcNow;
        public int CurrentIndex { get; set; } = 0;
        public ITimeseriesRegistry Timeseries => throw new NotImplementedException();
        public IEventRegistry Events => throw new NotImplementedException();
        public IFieldRegistry Fields => throw new NotImplementedException();
        public IBroker Broker { get; }
        public IMarketState Market { get; }

        public FakeEngineContext(IBroker broker, IMarketState market)
        {
            Broker = broker;
            Market = market;
        }

        public T? GetExtension<T>() where T : class => null;
    }

    private class TestRuleStrategy : RuleStrategyBase
    {
        public readonly List<string> EntriesChecked = new();
        public readonly List<(string Symbol, decimal Pos)> ExitsChecked = new();
        private readonly List<string> _targetSymbols;

        public TestRuleStrategy(params string[] symbols)
        {
            _targetSymbols = new List<string>(symbols);
        }

        protected override IEnumerable<string> GetSymbols(IEngineContext context) => _targetSymbols;

        protected override void CheckEntry(IEngineContext context, string symbol)
        {
            EntriesChecked.Add(symbol);
        }

        protected override void CheckExit(IEngineContext context, string symbol, decimal position)
        {
            ExitsChecked.Add((symbol, position));
        }
    }

    private class MockAlphaSignalStrategy : ISignalStrategy
    {
        public string Name => "MockAlpha";
        public void OnUpdate(IEngineContext context)
        {
            context.Market.SetState(new Dictionary<string, double> { ["sh.600000"] = 0.99 }, key: Name);
        }
    }

    [TestMethod]
    public void RuleStrategyBase_ShouldDispatchEntryAndExit_Correctly()
    {
        var broker = new FakeBroker();
        broker.SetPosition("sh.600000", 0m);    // 无仓 -> 应触发 CheckEntry
        broker.SetPosition("sz.000001", 500m);   // 有仓 -> 应触发 CheckExit

        var context = new FakeEngineContext(broker, new MarketState());
        var strategy = new TestRuleStrategy("sh.600000", "sz.000001");

        strategy.OnUpdate(context);

        Assert.AreEqual(1, strategy.EntriesChecked.Count);
        Assert.AreEqual("sh.600000", strategy.EntriesChecked[0]);

        Assert.AreEqual(1, strategy.ExitsChecked.Count);
        Assert.AreEqual("sz.000001", strategy.ExitsChecked[0].Symbol);
        Assert.AreEqual(500m, strategy.ExitsChecked[0].Pos);
    }

    [TestMethod]
    public void SignalStrategy_CanPublishSignalToMarketState()
    {
        var broker = new FakeBroker();
        var market = new MarketState();
        var context = new FakeEngineContext(broker, market);

        ISignalStrategy alpha = new MockAlphaSignalStrategy();
        Assert.IsInstanceOfType<IStrategy>(alpha);

        alpha.OnUpdate(context);

        var signals = market.GetState<Dictionary<string, double>>("MockAlpha");
        Assert.IsNotNull(signals);
        Assert.AreEqual(0.99, signals["sh.600000"]);
    }

    [TestMethod]
    public void RuleStrategyBase_NullContext_ShouldThrowArgumentNullException()
    {
        var strategy = new TestRuleStrategy("sh.600000");
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            strategy.OnUpdate(null!);
        });
    }

    [TestMethod]
    public void RuleStrategyBase_NullOrEmptySymbols_ShouldSafelySkip()
    {
        var broker = new FakeBroker();
        var context = new FakeEngineContext(broker, new MarketState());

        var strategy = new TestRuleStrategy("", null!, "   ");
        strategy.OnUpdate(context);

        Assert.AreEqual(0, strategy.EntriesChecked.Count);
        Assert.AreEqual(0, strategy.ExitsChecked.Count);
    }

    private class NullSymbolsRuleStrategy : RuleStrategyBase
    {
        protected override IEnumerable<string> GetSymbols(IEngineContext context) => null!;
        protected override void CheckEntry(IEngineContext context, string symbol) { }
        protected override void CheckExit(IEngineContext context, string symbol, decimal position) { }
    }

    [TestMethod]
    public void RuleStrategyBase_NullSymbolsList_ShouldSafelyReturn()
    {
        var broker = new FakeBroker();
        var context = new FakeEngineContext(broker, new MarketState());
        var strategy = new NullSymbolsRuleStrategy();

        // 验证 GetSymbols 返回 null 时安全退出且不抛异常
        strategy.OnUpdate(context);
    }

    [TestMethod]
    public void RuleStrategyBase_DefaultCallbacks_ShouldExecuteWithoutException()
    {
        var broker = new FakeBroker();
        var context = new FakeEngineContext(broker, new MarketState());
        var strategy = new TestRuleStrategy("sh.600000");

        // 默认空实现回调调用
        strategy.OnStart(context);
        strategy.OnEvent(context, "dividend", Array.Empty<IDataRow>());
        strategy.OnOrderStatusChanged(context, "ord-1", OrderStatus.Filled);
        strategy.OnTrade(context, new ExecutionReport("ord-1", "sh.600000", 10.0, 100m, 1.0, DateTime.UtcNow));
        strategy.OnPositionChanged(context, new PositionEvent("sh.600000", 100m, 10.0, DateTime.UtcNow));
        strategy.OnStop(context);

        Assert.AreEqual("TestRuleStrategy", strategy.Name);
        Assert.AreEqual(200, strategy.Lookback);
    }

    [TestMethod]
    public void RuleStrategyBase_MixedPositions_LongShortFlat_CorrectDispatch()
    {
        var broker = new FakeBroker();
        broker.SetPosition("sh.600000", 0m);     // Flat -> Entry
        broker.SetPosition("sh.600001", 100m);   // Long -> Exit
        broker.SetPosition("sh.600002", -50m);   // Short -> Exit

        var context = new FakeEngineContext(broker, new MarketState());
        var strategy = new TestRuleStrategy("sh.600000", "sh.600001", "sh.600002");

        strategy.OnUpdate(context);

        Assert.AreEqual(1, strategy.EntriesChecked.Count);
        Assert.AreEqual("sh.600000", strategy.EntriesChecked[0]);

        Assert.AreEqual(2, strategy.ExitsChecked.Count);
        Assert.AreEqual("sh.600001", strategy.ExitsChecked[0].Symbol);
        Assert.AreEqual(100m, strategy.ExitsChecked[0].Pos);
        Assert.AreEqual("sh.600002", strategy.ExitsChecked[1].Symbol);
        Assert.AreEqual(-50m, strategy.ExitsChecked[1].Pos);
    }
}
