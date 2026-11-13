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
public class StrategyPipelineTests
{
    private class FakeBroker : IBroker
    {
        public double GetCash() => 100000.0;
        public decimal GetPosition(string symbol) => 0m;
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

    private class StepTraceStrategy : IStrategy
    {
        public string Name { get; }
        public int Lookback { get; }
        public readonly List<string> Trace;

        public StepTraceStrategy(string name, int lookback, List<string> trace)
        {
            Name = name;
            Lookback = lookback;
            Trace = trace;
        }

        public void OnStart(IEngineContext context) => Trace.Add($"{Name}:OnStart");
        public void OnUpdate(IEngineContext context) => Trace.Add($"{Name}:OnUpdate");
        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) => Trace.Add($"{Name}:OnEvent:{tableName}");
        public void OnStop(IEngineContext context) => Trace.Add($"{Name}:OnStop");
        public void OnOrderStatusChanged(IEngineContext context, string orderId, OrderStatus status) => Trace.Add($"{Name}:OrderStatus:{orderId}:{status}");
        public void OnTrade(IEngineContext context, ExecutionReport e) => Trace.Add($"{Name}:Trade:{e.OrderId}");
        public void OnPositionChanged(IEngineContext context, PositionEvent e) => Trace.Add($"{Name}:Position:{e.Symbol}");
    }

    private record AlphaSignals(double Score);

    private class ProducerStrategy : ISignalStrategy
    {
        public string Name => "Producer";
        public void OnUpdate(IEngineContext context)
        {
            context.Market.SetState(new AlphaSignals(0.95));
        }
    }

    private class ConsumerStrategy : IStrategy
    {
        public string Name => "Consumer";
        public double ConsumedScore { get; private set; }

        public void OnUpdate(IEngineContext context)
        {
            var sig = context.Market.GetState<AlphaSignals>();
            if (sig != null)
            {
                ConsumedScore = sig.Score;
            }
        }
    }

    [TestMethod]
    public void Pipeline_LookbackAggregation_ShouldTakeMaximum()
    {
        var trace = new List<string>();
        var pipeline = new SequentialPipeline();

        pipeline.Add(new StepTraceStrategy("S1", 60, trace))
                .Add(new StepTraceStrategy("S2", 250, trace))
                .Add(new StepTraceStrategy("S3", 120, trace));

        // 编译前与编译后均应返回最大值 250
        Assert.AreEqual(250, pipeline.Lookback);
        pipeline.Compile();
        Assert.AreEqual(250, pipeline.Lookback);
    }

    [TestMethod]
    public void Pipeline_DefaultLookback_ShouldBe200_WhenEmptyOrZero()
    {
        var pipeline = new SequentialPipeline();
        Assert.AreEqual(200, pipeline.Lookback);

        var trace = new List<string>();
        pipeline.Add(new StepTraceStrategy("Zero", 0, trace));
        Assert.AreEqual(200, pipeline.Lookback);
    }

    [TestMethod]
    public void Pipeline_AfterCompile_AddingStrategyShouldThrow()
    {
        var trace = new List<string>();
        var pipeline = new SequentialPipeline();
        pipeline.Add(new StepTraceStrategy("S1", 60, trace));
        pipeline.Compile();

        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            pipeline.Add(new StepTraceStrategy("S2", 80, trace));
        });
    }

    [TestMethod]
    public void Pipeline_SequentialExecutionAndBlackboardSharing_ShouldWork()
    {
        var context = new FakeEngineContext(new FakeBroker(), new MarketState());
        var pipeline = new SequentialPipeline();

        var producer = new ProducerStrategy();
        var consumer = new ConsumerStrategy();

        pipeline.Add(producer).Add(consumer).Compile();

        pipeline.OnUpdate(context);

        Assert.AreEqual(0.95, consumer.ConsumedScore);
    }

    [TestMethod]
    public void Pipeline_LifecycleAndCallbacks_ShouldDispatchInOrder()
    {
        var trace = new List<string>();
        var pipeline = new SequentialPipeline();
        pipeline.Add(new StepTraceStrategy("NodeA", 50, trace))
                .Add(new StepTraceStrategy("NodeB", 100, trace));

        var context = new FakeEngineContext(new FakeBroker(), new MarketState());

        pipeline.OnStart(context);
        pipeline.OnUpdate(context);
        pipeline.OnEvent(context, "longhubang", Array.Empty<IDataRow>());
        pipeline.OnOrderStatusChanged(context, "ord-1", OrderStatus.Submitted);
        pipeline.OnTrade(context, new ExecutionReport("ord-1", "sh.600000", 10.0, 100m, 1.0, DateTime.UtcNow));
        pipeline.OnPositionChanged(context, new PositionEvent("sh.600000", 100m, 10.0, DateTime.UtcNow));
        pipeline.OnStop(context);

        CollectionAssert.AreEqual(new[]
        {
            "NodeA:OnStart", "NodeB:OnStart",
            "NodeA:OnUpdate", "NodeB:OnUpdate",
            "NodeA:OnEvent:longhubang", "NodeB:OnEvent:longhubang",
            "NodeA:OrderStatus:ord-1:Submitted", "NodeB:OrderStatus:ord-1:Submitted",
            "NodeA:Trade:ord-1", "NodeB:Trade:ord-1",
            "NodeA:Position:sh.600000", "NodeB:Position:sh.600000",
            "NodeA:OnStop", "NodeB:OnStop"
        }, trace);
    }

    [TestMethod]
    public void Pipeline_NestedPipeline_ShouldRecursivelyCompileAndFlattenStrategies()
    {
        var trace = new List<string>();
        var root = new SequentialPipeline { Name = "RootPipeline" };
        var subPipeline = new SequentialPipeline { Name = "SubPipeline" };

        subPipeline.Add(new StepTraceStrategy("Leaf1", 300, trace));
        subPipeline.Add(new StepTraceStrategy("Leaf2", 150, trace));

        root.Add(new StepTraceStrategy("Leaf0", 50, trace));
        root.Add(subPipeline);

        // 递归展平检查
        var flattened = new List<IStrategy>(root.GetStrategies());
        Assert.AreEqual(3, flattened.Count);
        Assert.AreEqual("Leaf0", flattened[0].Name);
        Assert.AreEqual("Leaf1", flattened[1].Name);
        Assert.AreEqual("Leaf2", flattened[2].Name);

        // 递归 Lookback 聚合
        Assert.AreEqual(300, root.Lookback);

        root.Compile();

        // 验证子管线也被密封，禁止追加
        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            subPipeline.Add(new StepTraceStrategy("Leaf3", 100, trace));
        });

        // 验证执行时按序触发全部叶子节点
        var context = new FakeEngineContext(new FakeBroker(), new MarketState());
        root.OnUpdate(context);

        CollectionAssert.AreEqual(new[] { "Leaf0:OnUpdate", "Leaf1:OnUpdate", "Leaf2:OnUpdate" }, trace);
    }

    [TestMethod]
    public void Pipeline_EmptyPipeline_ShouldRunSafelyWithoutException()
    {
        var empty = new SequentialPipeline();
        var context = new FakeEngineContext(new FakeBroker(), new MarketState());

        empty.Compile();
        empty.OnStart(context);
        empty.OnUpdate(context);
        empty.OnEvent(context, "dummy", Array.Empty<IDataRow>());
        empty.OnStop(context);

        Assert.AreEqual(0, new List<IStrategy>(empty.GetStrategies()).Count);
    }

    [TestMethod]
    public void Pipeline_AddNull_ShouldThrowArgumentNullException()
    {
        var pipeline = new SequentialPipeline();
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            pipeline.Add(null!);
        });
    }

    private class ConcurrentAlphaStrategy : ISignalStrategy
    {
        public string Name { get; }
        public int Lookback { get; }
        public double Factor { get; }

        public ConcurrentAlphaStrategy(string name, int lookback, double factor)
        {
            Name = name;
            Lookback = lookback;
            Factor = factor;
        }

        public void OnUpdate(IEngineContext context)
        {
            // 写入带命名空间隔离的黑板状态
            context.Market.SetState(new AlphaSignals(Factor), key: Name);
        }

        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events)
        {
            context.Market.SetState($"EventReceived:{tableName}", key: $"{Name}:event");
        }
    }

    [TestMethod]
    public void ParallelPipeline_ConcurrentExecution_ShouldPopulateBlackboardSafely()
    {
        var market = new MarketState();
        var context = new FakeEngineContext(new FakeBroker(), market);
        var parallel = new ParallelPipeline { Name = "SignalStage", Concurrent = true };

        for (int i = 0; i < 10; i++)
        {
            parallel.Add(new ConcurrentAlphaStrategy($"Alpha_{i}", 50 + i * 10, i * 1.5));
        }

        Assert.AreEqual(140, parallel.Lookback);

        parallel.Compile();
        parallel.OnUpdate(context);

        // 验证并发写入的 10 个信号均完好无损
        for (int i = 0; i < 10; i++)
        {
            var sig = market.GetState<AlphaSignals>($"Alpha_{i}");
            Assert.IsNotNull(sig);
            Assert.AreEqual(i * 1.5, sig.Score, 1e-6);
        }

        // 验证并发 OnEvent 分发
        parallel.OnEvent(context, "market_news", Array.Empty<IDataRow>());
        for (int i = 0; i < 10; i++)
        {
            var ev = market.GetState<string>($"Alpha_{i}:event");
            Assert.AreEqual("EventReceived:market_news", ev);
        }
    }

    [TestMethod]
    public void CompositePipeline_SequentialMasterWithParallelSignalStage()
    {
        var market = new MarketState();
        var broker = new FakeBroker();
        var context = new FakeEngineContext(broker, market);

        var master = new SequentialPipeline { Name = "Master" };
        var parallelSignals = new ParallelPipeline { Name = "ParallelSignals", Concurrent = true };

        parallelSignals.Add(new ConcurrentAlphaStrategy("FactorA", 30, 2.0));
        parallelSignals.Add(new ConcurrentAlphaStrategy("FactorB", 90, 3.0));

        // 阶段 2：组合调仓策略，消费阶段 1 并行计算的两个因子
        var portfolio = new StepTraceStrategy("PortfolioPM", 60, new List<string>());

        master.Add(parallelSignals);
        master.Add(portfolio);

        Assert.AreEqual(90, master.Lookback);

        master.Compile();
        master.OnUpdate(context);

        var sigA = market.GetState<AlphaSignals>("FactorA");
        var sigB = market.GetState<AlphaSignals>("FactorB");

        Assert.IsNotNull(sigA);
        Assert.IsNotNull(sigB);
        Assert.AreEqual(2.0, sigA.Score);
        Assert.AreEqual(3.0, sigB.Score);
    }

    [TestMethod]
    public void ParallelPipeline_SingleStrategy_ShouldExecuteSynchronously()
    {
        var market = new MarketState();
        var context = new FakeEngineContext(new FakeBroker(), market);
        var parallel = new ParallelPipeline();

        parallel.Add(new ConcurrentAlphaStrategy("SoloAlpha", 100, 42.0));
        parallel.Compile();

        parallel.OnUpdate(context);
        var sig = market.GetState<AlphaSignals>("SoloAlpha");
        Assert.IsNotNull(sig);
        Assert.AreEqual(42.0, sig.Score);
    }

    [TestMethod]
    public void ParallelPipeline_SingleStrategy_OnEvent_ShouldDispatchSynchronously()
    {
        var market = new MarketState();
        var context = new FakeEngineContext(new FakeBroker(), market);
        var parallel = new ParallelPipeline();

        parallel.Add(new ConcurrentAlphaStrategy("SoloAlpha", 100, 42.0));
        parallel.Compile();

        parallel.OnEvent(context, "daily_event", Array.Empty<IDataRow>());
        var ev = market.GetState<string>("SoloAlpha:event");
        Assert.AreEqual("EventReceived:daily_event", ev);
    }

    [TestMethod]
    public void StrategyPipeline_StaticFactoryMethods_ShouldCreateProperInstances()
    {
        using var seq = StrategyPipeline.Sequential();
        using var par = StrategyPipeline.Parallel();

        Assert.IsInstanceOfType<SequentialPipeline>(seq);
        Assert.IsInstanceOfType<ParallelPipeline>(par);
        Assert.AreEqual("SequentialPipeline", seq.Name);
        Assert.AreEqual("ParallelPipeline", par.Name);
    }

    private class InspectablePipeline : StrategyPipeline
    {
        public bool CheckCompiled() => IsCompiled;
        public override void OnUpdate(IEngineContext context) { }
        public override void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
    }

    [TestMethod]
    public void StrategyPipeline_IsCompiledProperty_ShouldReflectState()
    {
        var pipeline = new InspectablePipeline();
        Assert.IsFalse(pipeline.CheckCompiled());
        pipeline.Compile();
        Assert.IsTrue(pipeline.CheckCompiled());
    }

    private class ThrowingStrategy : IStrategy
    {
        public string Name => "Throwing";
        public void OnUpdate(IEngineContext context) => throw new InvalidOperationException("Update failed!");
        public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) => throw new InvalidOperationException("Event failed!");
    }

    [TestMethod]
    public void ParallelPipeline_ConcurrentTrue_ChildThrows_ShouldPropagateAggregateException()
    {
        var market = new MarketState();
        var context = new FakeEngineContext(new FakeBroker(), market);
        var parallel = new ParallelPipeline { Concurrent = true };

        parallel.Add(new ThrowingStrategy());
        parallel.Add(new ConcurrentAlphaStrategy("Alpha1", 50, 1.0));
        parallel.Compile();

        Assert.ThrowsException<AggregateException>(() =>
        {
            parallel.OnUpdate(context);
        });

        Assert.ThrowsException<AggregateException>(() =>
        {
            parallel.OnEvent(context, "evt", Array.Empty<IDataRow>());
        });
    }

    [TestMethod]
    public void ParallelPipeline_ConcurrentFalse_ChildThrows_ShouldPropagateDirectException()
    {
        var market = new MarketState();
        var context = new FakeEngineContext(new FakeBroker(), market);
        // 默认 Concurrent 为 false
        var parallel = new ParallelPipeline { Concurrent = false };

        parallel.Add(new ThrowingStrategy());
        parallel.Add(new ConcurrentAlphaStrategy("Alpha1", 50, 1.0));
        parallel.Compile();

        // 单线程执行直接抛出原始异常，无 AggregateException 包裹，极大方便排查
        var exUpdate = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            parallel.OnUpdate(context);
        });
        Assert.AreEqual("Update failed!", exUpdate.Message);

        var exEvent = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            parallel.OnEvent(context, "evt", Array.Empty<IDataRow>());
        });
        Assert.AreEqual("Event failed!", exEvent.Message);
    }

    [TestMethod]
    public void ParallelPipeline_DefaultConcurrent_IsFalse()
    {
        var parallel = new ParallelPipeline();
        Assert.IsFalse(parallel.Concurrent);
    }

    private class OrderRecordingStrategy : IStrategy
    {
        public string Name { get; }
        private readonly List<string> _executionLog;

        public OrderRecordingStrategy(string name, List<string> executionLog)
        {
            Name = name;
            _executionLog = executionLog;
        }

        public void OnUpdate(IEngineContext context)
        {
            _executionLog.Add(Name);
        }
    }

    [TestMethod]
    public void ParallelPipeline_DefaultConcurrentFalse_ExecutesDeterministicallyInSequence()
    {
        var context = new FakeEngineContext(new FakeBroker(), new MarketState());
        var parallel = new ParallelPipeline(); // Concurrent = false
        var executionLog = new List<string>();

        parallel.Add(new OrderRecordingStrategy("Step1", executionLog));
        parallel.Add(new OrderRecordingStrategy("Step2", executionLog));
        parallel.Add(new OrderRecordingStrategy("Step3", executionLog));
        parallel.Compile();

        parallel.OnUpdate(context);

        // 默认单核顺序执行，顺序具有严格确定性
        Assert.AreEqual(3, executionLog.Count);
        Assert.AreEqual("Step1", executionLog[0]);
        Assert.AreEqual("Step2", executionLog[1]);
        Assert.AreEqual("Step3", executionLog[2]);
    }

    [TestMethod]
    public void ParallelPipeline_ZeroStrategies_ShouldExecuteSafely()
    {
        var market = new MarketState();
        var context = new FakeEngineContext(new FakeBroker(), market);
        var parallel = new ParallelPipeline();

        parallel.Compile();

        // 0 个策略时 OnUpdate 与 OnEvent 应安全通过且不发生任何异常
        parallel.OnUpdate(context);
        parallel.OnEvent(context, "test", Array.Empty<IDataRow>());
    }

    [TestMethod]
    public void ParallelPipeline_RepeatedSteps_ReusesDelegatesWithoutContextLeak()
    {
        var market = new MarketState();
        var context = new FakeEngineContext(new FakeBroker(), market);
        var parallel = new ParallelPipeline();

        parallel.Add(new ConcurrentAlphaStrategy("A1", 50, 10.0));
        parallel.Add(new ConcurrentAlphaStrategy("A2", 60, 20.0));
        parallel.Compile();

        // 连续运行 100 次时间步，验证实例委托复用且上下文每次正常清理
        for (int i = 0; i < 100; i++)
        {
            parallel.OnUpdate(context);
            parallel.OnEvent(context, "evt", Array.Empty<IDataRow>());
        }

        Assert.AreEqual(10.0, market.GetState<AlphaSignals>("A1")!.Score);
        Assert.AreEqual(20.0, market.GetState<AlphaSignals>("A2")!.Score);
    }
}
