using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Strategy;
using CarrotBackTesting.Net.UnitTest.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Strategy;

public class TestInjectableStrategy : IStrategy
{
    public string Name => "Injectable";
    public int Period { get; set; } = 20;
    public int Lookback { get; set; } = 200;
    public double Factor { get; set; } = 1.0;
    public string Tag { get; set; } = "default";
    public bool Enabled { get; set; } = false;
    public PipelineMode Mode { get; set; } = PipelineMode.Sequential;
    public string[] Symbols { get; set; } = Array.Empty<string>();
    public List<int> Windows { get; set; } = new();
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    public void OnUpdate(IEngineContext context) { }
}

[TestClass]
public class PipelineLoadingAndYamlTests
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
        public CarrotBacktesting.NET.Abstraction.Data.ITimeseriesRegistry Timeseries => throw new NotImplementedException();
        public CarrotBacktesting.NET.Abstraction.Data.IEventRegistry Events => throw new NotImplementedException();
        public CarrotBacktesting.NET.Abstraction.Data.IFieldRegistry Fields => throw new NotImplementedException();
        public IBroker Broker { get; } = new FakeBroker();
        public IMarketState Market { get; } = new MarketState();
        public T? GetExtension<T>() where T : class => null;
    }

    [TestMethod]
    public void Mode1_CodeFirst_ShouldWorkDirectly()
    {
        using var pipeline = new SequentialPipeline();
        var strategy = new TestInjectableStrategy { Period = 50 };
        pipeline.Add(strategy).Compile();

        Assert.AreEqual(1, new List<IStrategy>(pipeline.GetStrategies()).Count);
        Assert.AreEqual(200, pipeline.Lookback);
    }

    [TestMethod]
    public void Mode2_AddFromSource_CodeAndFile_ShouldWork()
    {
        const string code = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public class ScriptStrategy : IStrategy
{
    public string Name => ""ScriptMA"";
    public int Lookback => 88;
    public void OnUpdate(IEngineContext context) { }
}
";
        var tempFile = Path.Combine(Path.GetTempPath(), $"Script_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempFile, code);

        try
        {
            using var pipeline = new SequentialPipeline();
            // 1. 从源码字符串加载
            pipeline.AddFromSource(code);
            // 2. 从 .cs 物理文件加载
            pipeline.AddFromSource(tempFile);
            pipeline.Compile();

            var list = new List<IStrategy>(pipeline.GetStrategies());
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(88, pipeline.Lookback);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void Mode3_FromYaml_WithMixedNodesAndParamsInjection_ShouldWork()
    {
        const string scriptCode = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public class YamlScriptStrategy : IStrategy
{
    public string Name => ""YamlScriptMA"";
    public int FastPeriod { get; set; } = 5;
    public int SlowPeriod { get; set; } = 20;
    public double Multiplier { get; set; } = 1.0;

    public void OnUpdate(IEngineContext context)
    {
        context.Market.SetState($""Fast={FastPeriod},Slow={SlowPeriod},Mul={Multiplier}"", key: ""MA"");
    }
}
";
        var tempCsFile = Path.Combine(Path.GetTempPath(), $"YamlScript_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempCsFile, scriptCode);

        try
        {
            // 使用 TestPipelineConfigBuilder 构建真实的 YAML 文本
            string yaml = new TestPipelineConfigBuilder()
                .WithName("MixedPipeline")
                .AddSourceStrategy(tempCsFile, new()
                {
                    ["FastPeriod"] = 15,
                    ["SlowPeriod"] = 60,
                    ["Multiplier"] = 2.5
                })
                .AddStrategy(typeof(TestInjectableStrategy), new()
                {
                    ["Period"] = 100,
                    ["Factor"] = 3.14,
                    ["Tag"] = "Production",
                    ["Enabled"] = true
                })
                .BuildYaml();

            // 1. Config 层反序列化为强类型配置
            var config = PipelineConfigLoader.FromYaml(yaml);
            Assert.AreEqual("MixedPipeline", config.Name);

            // 2. 策略工厂装配管线
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(tempCsFile))!;
            using var pipeline = StrategyPipelineFactory.Create(config, baseDir);

            var strategies = new List<IStrategy>(pipeline.GetStrategies());
            Assert.AreEqual(2, strategies.Count);

            // 验证参数正确注入到第二个策略
            var second = strategies[1] as TestInjectableStrategy;
            Assert.IsNotNull(second);
            Assert.AreEqual(100, second.Period);
            Assert.AreEqual(3.14, second.Factor);
            Assert.AreEqual("Production", second.Tag);
            Assert.IsTrue(second.Enabled);

            // 运行 OnUpdate 验证动态脚本参数也被正确注入
            var context = new FakeEngineContext();
            pipeline.OnUpdate(context);
            Assert.AreEqual("Fast=15,Slow=60,Mul=2.5", context.Market.GetState<string>("MA"));
        }
        finally
        {
            if (File.Exists(tempCsFile)) File.Delete(tempCsFile);
        }
    }

    [TestMethod]
    public void Mode3_FromFile_ShouldLoadYamlFileCorrectly()
    {
        const string scriptCode = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public class MiniScriptStrategy : IStrategy
{
    public string Name => ""Mini"";
    public void OnUpdate(IEngineContext context) { }
}
";
        var tempDir = Path.Combine(Path.GetTempPath(), $"YamlDir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempCs = Path.Combine(tempDir, "MiniStrategy.cs");
        File.WriteAllText(tempCs, scriptCode);

        try
        {
            // 使用 TestPipelineConfigBuilder 保存 YAML 配置文件
            string tempYaml = new TestPipelineConfigBuilder()
                .AddSourceStrategy("MiniStrategy.cs")
                .Save(tempDir, "pipeline.yaml");

            // 工厂直接从物理文件路径装配
            using var pipeline = StrategyPipelineFactory.Create(tempYaml);
            var list = new List<IStrategy>(pipeline.GetStrategies());
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("Mini", list[0].Name);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void Mode3_FromYaml_InvalidFormatOrMissingNode_ShouldThrow()
    {
        // 1. 空内容
        Assert.ThrowsException<ArgumentException>(() =>
        {
            PipelineConfigLoader.FromYaml("   ");
        });

        // 2. 缺少 pipeline 列表
        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            string yaml = new TestPipelineConfigBuilder().WithName("Empty").WithoutPipeline().BuildYaml();
            PipelineConfigLoader.FromYaml(yaml);
        });

        // 3. 节点未指定 source / type / pipeline
        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            string yaml = new TestPipelineConfigBuilder()
                .AddInvalidNode(new() { ["foo"] = "bar" })
                .BuildYaml();
            var config = PipelineConfigLoader.FromYaml(yaml);
            StrategyPipelineFactory.Create(config);
        });

        // 4. 指定了不存在的 type
        Assert.ThrowsException<TypeLoadException>(() =>
        {
            string yaml = new TestPipelineConfigBuilder()
                .AddStrategy("NonExistentNamespace.FakeStrategy")
                .BuildYaml();
            var config = PipelineConfigLoader.FromYaml(yaml);
            StrategyPipelineFactory.Create(config);
        });

        // 5. 配置文件不存在
        Assert.ThrowsException<FileNotFoundException>(() =>
        {
            StrategyPipelineFactory.Create("non_existent_config.yaml");
        });
    }

    [TestMethod]
    public void Mode3_FromYaml_WithNestedParallelStage_ShouldInstantiateAndExecute()
    {
        // 使用 TestPipelineConfigBuilder 构造多层嵌套管线 YAML
        string yaml = new TestPipelineConfigBuilder()
            .WithName("MasterPipeline")
            .WithMode(PipelineMode.Sequential)
            .AddSubPipeline(sub => sub
                .WithName("ParallelSignals")
                .WithMode(PipelineMode.Parallel)
                .WithConcurrent(true)
                .AddStrategy(typeof(TestInjectableStrategy), new() { ["Tag"] = "Signal1", ["Period"] = 10 })
                .AddStrategy(typeof(TestInjectableStrategy), new() { ["Tag"] = "Signal2", ["Period"] = 20 }))
            .AddStrategy(typeof(TestInjectableStrategy), new() { ["Tag"] = "PortfolioExecution", ["Period"] = 30, ["Lookback"] = 350 })
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config);
        Assert.AreEqual("MasterPipeline", pipeline.Name);

        var strategies = new List<IStrategy>(pipeline.GetStrategies());
        // 展平后包含 3 个叶子策略
        Assert.AreEqual(3, strategies.Count);
        Assert.AreEqual(350, pipeline.Lookback);

        var context = new FakeEngineContext();
        pipeline.OnUpdate(context);
    }

    [TestMethod]
    public void Mode3_FromYaml_WithNullParam_ShouldInjectNull()
    {
        string yaml = new TestPipelineConfigBuilder()
            .AddStrategy(typeof(TestInjectableStrategy), new() { ["Tag"] = null! })
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config);
        var strategy = (TestInjectableStrategy)new List<IStrategy>(pipeline.GetStrategies())[0];
        Assert.IsNull(strategy.Tag);
    }

    [TestMethod]
    public void Mode3_FromYaml_WithInvalidParamType_ShouldThrowDescriptiveException()
    {
        string yaml = new TestPipelineConfigBuilder()
            .AddStrategy(typeof(TestInjectableStrategy), new() { ["Period"] = "not_an_int" })
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            StrategyPipelineFactory.Create(config);
        });

        StringAssert.Contains(ex.Message, "注入参数 'Period' 失败");
    }

    [TestMethod]
    public void Mode3_FromYaml_EmptyNodeDefinition_ShouldThrowInvalidOperationException()
    {
        string yaml = new TestPipelineConfigBuilder()
            .AddEmptyNode("EmptyNode")
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            StrategyPipelineFactory.Create(config);
        });

        StringAssert.Contains(ex.Message, "必须指定 'pipeline'");
    }

    [TestMethod]
    public void Mode3_FromYaml_WithEnumParam_ShouldInjectEnumCorrectly()
    {
        string yaml = new TestPipelineConfigBuilder()
            .AddStrategy(typeof(TestInjectableStrategy), new() { ["Mode"] = "Parallel" })
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config);
        var strategy = (TestInjectableStrategy)new List<IStrategy>(pipeline.GetStrategies())[0];
        Assert.AreEqual(PipelineMode.Parallel, strategy.Mode);
    }

    private class DisposableStrategy : IStrategy, IDisposable
    {
        public string Name => "Disposable";
        public bool IsDisposed { get; private set; }
        public void OnUpdate(IEngineContext context) { }
        public void Dispose() => IsDisposed = true;
    }

    [TestMethod]
    public void Pipeline_Dispose_ShouldRecursivelyDisposeChildStrategiesAndClearReferences()
    {
        var master = StrategyPipeline.Sequential();
        var subPipeline = StrategyPipeline.Parallel();
        var disposableLeaf = new DisposableStrategy();

        subPipeline.Add(disposableLeaf);
        master.Add(subPipeline);
        master.Compile();

        Assert.IsFalse(disposableLeaf.IsDisposed);

        master.Dispose();

        // 验证递归释放了嵌套管线内的 IDisposable 子策略
        Assert.IsTrue(disposableLeaf.IsDisposed);
        // 验证清空了内部策略引用
        Assert.AreEqual(0, new List<IStrategy>(master.GetStrategies()).Count);
    }

    [TestMethod]
    public void FromYaml_UnknownProperty_ShouldThrowArgumentException()
    {
        string yaml = new TestPipelineConfigBuilder()
            .AddStrategy(typeof(TestInjectableStrategy), new() { ["NonExistentProperty"] = 123 })
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        var ex = Assert.ThrowsException<ArgumentException>(() =>
        {
            StrategyPipelineFactory.Create(config);
        });

        StringAssert.Contains(ex.Message, "不存在可写入的公共属性 'NonExistentProperty'");
        StringAssert.Contains(ex.Message, "可用属性:");
    }

    [TestMethod]
    public void FromYaml_ComplexTypes_Array_List_TimeSpan_ShouldInjectCorrectly()
    {
        string yaml = new TestPipelineConfigBuilder()
            .AddStrategy(typeof(TestInjectableStrategy), new()
            {
                ["Symbols"] = new[] { "sh.600000", "sz.000001" },
                ["Windows"] = new[] { 5, 10, 20 },
                ["Interval"] = "00:05:00"
            })
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config);
        var strategy = (TestInjectableStrategy)new List<IStrategy>(pipeline.GetStrategies())[0];

        Assert.AreEqual(2, strategy.Symbols.Length);
        Assert.AreEqual("sh.600000", strategy.Symbols[0]);
        Assert.AreEqual("sz.000001", strategy.Symbols[1]);

        Assert.AreEqual(3, strategy.Windows.Count);
        Assert.AreEqual(5, strategy.Windows[0]);
        Assert.AreEqual(10, strategy.Windows[1]);
        Assert.AreEqual(20, strategy.Windows[2]);

        Assert.AreEqual(TimeSpan.FromMinutes(5), strategy.Interval);
    }

    [TestMethod]
    public void FromYaml_EmptyOrMissingPipeline_ShouldThrowInvalidOperationException()
    {
        string yamlEmpty = new TestPipelineConfigBuilder()
            .WithName("EmptyPipeline")
            .WithEmptyPipeline()
            .BuildYaml();

        var ex1 = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            PipelineConfigLoader.FromYaml(yamlEmpty);
        });
        StringAssert.Contains(ex1.Message, "YAML 配置未包含有效的 'pipeline' 节点列表");

        string yamlMissing = new TestPipelineConfigBuilder()
            .WithName("NoPipeline")
            .WithMode("sequential")
            .WithoutPipeline()
            .BuildYaml();

        var ex2 = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            PipelineConfigLoader.FromYaml(yamlMissing);
        });
        StringAssert.Contains(ex2.Message, "YAML 配置未包含有效的 'pipeline' 节点列表");
    }

    [TestMethod]
    public void FromYaml_ParallelPipeline_ConcurrentConfig_ShouldBeSetCorrectly()
    {
        string yaml = new TestPipelineConfigBuilder()
            .WithName("RootParallel")
            .WithMode(PipelineMode.Parallel)
            .WithConcurrent(true)
            .AddStrategy(typeof(TestInjectableStrategy))
            .AddStrategy(typeof(TestInjectableStrategy))
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config);
        Assert.IsInstanceOfType<ParallelPipeline>(pipeline);
        var parallel = (ParallelPipeline)pipeline;
        Assert.IsTrue(parallel.Concurrent);
    }

    [TestMethod]
    public void FromYaml_NestedParallelPipeline_ConcurrentParam_ShouldBeSetCorrectly()
    {
        string yaml = new TestPipelineConfigBuilder()
            .WithName("RootSequential")
            .WithMode(PipelineMode.Sequential)
            .AddSubPipeline(sub => sub
                .WithName("SubParallel")
                .WithMode(PipelineMode.Parallel)
                .WithConcurrent(true)
                .AddStrategy(typeof(TestInjectableStrategy))
                .AddStrategy(typeof(TestInjectableStrategy)))
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config);
        Assert.IsInstanceOfType<SequentialPipeline>(pipeline);
        Assert.AreEqual(2, new List<IStrategy>(pipeline.GetStrategies()).Count);
    }

    [TestMethod]
    public void StrategyPipelineFactory_RoundTrip_SaveAndCreate_ShouldWork()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"PipelineTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string filePath = new TestPipelineConfigBuilder()
                .WithName("DiskPipeline")
                .WithMode(PipelineMode.Parallel)
                .WithConcurrent(true)
                .AddStrategy(typeof(TestInjectableStrategy), new() { ["Period"] = 88, ["Factor"] = 2.718 })
                .Save(tempDir, "strategy_disk.yaml");

            Assert.IsTrue(File.Exists(filePath));

            // 直接通过文件路径装配
            using var loadedPipeline = StrategyPipelineFactory.Create(filePath);
            Assert.AreEqual("DiskPipeline", loadedPipeline.Name);
            Assert.IsInstanceOfType<ParallelPipeline>(loadedPipeline);
            Assert.IsTrue(((ParallelPipeline)loadedPipeline).Concurrent);

            var leaf = (TestInjectableStrategy)new List<IStrategy>(loadedPipeline.GetStrategies())[0];
            Assert.AreEqual(88, leaf.Period);
            Assert.AreEqual(2.718, leaf.Factor);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
