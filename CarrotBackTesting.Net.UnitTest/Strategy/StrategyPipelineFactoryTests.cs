using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Strategy;

public class TestInjectableStrategy : IStrategy
{
    public string Name => "Injectable";
    public int Period { get; set; } = 20;
    public int Lookback { get; set; } = 200;
    public double Factor { get; set; } = 1.0;
    public string? Tag { get; set; } = "default";
    public bool Enabled { get; set; } = false;
    public PipelineMode Mode { get; set; } = PipelineMode.Sequential;
    public string[] Symbols { get; set; } = Array.Empty<string>();
    public List<int> Windows { get; set; } = new();
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    public void OnUpdate(IEngineContext context) { }
}

/// <summary>
/// 策略管线装配工厂（StrategyPipelineFactory）与配置加载端到端集成测试。
/// 验证：串行、并行（单核/多核并发）、多层复合嵌套、Roslyn 源码与编译类型混编、各类型参数注入以及异常防御。
/// 严格遵循“标准 YAML 文本 -> ConfigLoader 反序列化 -> Factory 装配并执行验证”的真实闭环。
/// </summary>
[TestClass]
public class StrategyPipelineFactoryTests
{
    private string _tempDir = "";

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PipelineFactoryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private class SequenceTrackingStrategy : IStrategy
    {
        public string Name { get; set; } = "Tracker";
        public int Order { get; set; } = 0;
        public int Lookback => 50;

        public void OnUpdate(IEngineContext context)
        {
            var list = context.Market.GetState<List<int>>("ExecutionSequence");
            if (list == null)
            {
                list = new List<int>();
                context.Market.SetState(list, "ExecutionSequence");
            }
            lock (list)
            {
                list.Add(Order);
            }
        }
    }

    public record SignalScore(double Value);

    private class ConcurrentProbeStrategy : IStrategy
    {
        public string Name { get; set; } = "ConcurrentProbe";
        public string SignalKey { get; set; } = "DefaultKey";
        public double Score { get; set; } = 0.5;
        public int Lookback => 20;

        public void OnUpdate(IEngineContext context)
        {
            Thread.SpinWait(100);
            context.Market.SetState(new SignalScore(Score), SignalKey);
        }
    }

    private class MockEngineContext : IEngineContext
    {
        public DateTime CurrentTime { get; set; } = DateTime.UtcNow;
        public int CurrentIndex { get; set; } = 100;
        public CarrotBacktesting.NET.Abstraction.Data.ITimeseriesRegistry Timeseries => throw new NotImplementedException();
        public CarrotBacktesting.NET.Abstraction.Data.IEventRegistry Events => throw new NotImplementedException();
        public CarrotBacktesting.NET.Abstraction.Data.IFieldRegistry Fields => throw new NotImplementedException();
        public IBroker Broker => throw new NotImplementedException();
        public IMarketState Market { get; } = new MarketState();
        public T? GetExtension<T>() where T : class => null;
    }

    #region 1. 拓扑装配与执行 (Topology Assembly & Execution)

    [TestMethod]
    public void Create_SequentialPipeline_LoadsAndExecutesInOrder()
    {
        string yaml = $"""
            name: "StrictSequentialPipeline"
            mode: "sequential"
            concurrent: false
            pipeline:
              - type: "{typeof(SequenceTrackingStrategy).AssemblyQualifiedName}"
                params:
                  Name: "Step1"
                  Order: 1
              - type: "{typeof(SequenceTrackingStrategy).AssemblyQualifiedName}"
                params:
                  Name: "Step2"
                  Order: 2
              - type: "{typeof(SequenceTrackingStrategy).AssemblyQualifiedName}"
                params:
                  Name: "Step3"
                  Order: 3
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        Assert.AreEqual("StrictSequentialPipeline", config.Name);
        Assert.AreEqual(PipelineMode.Sequential, config.Mode);
        Assert.IsFalse(config.Concurrent);
        Assert.AreEqual(3, config.Pipeline.Count);

        using var pipeline = StrategyPipelineFactory.Create(config);
        Assert.IsInstanceOfType<SequentialPipeline>(pipeline);
        Assert.AreEqual(50, pipeline.Lookback);

        var context = new MockEngineContext();
        pipeline.OnUpdate(context);

        var sequence = context.Market.GetState<List<int>>("ExecutionSequence");
        Assert.IsNotNull(sequence);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, sequence);
    }

    [TestMethod]
    public void Create_ParallelPipeline_ConcurrentFalse_ExecutesSynchronously()
    {
        string yaml = $"""
            name: "DeterministicParallel"
            mode: "parallel"
            concurrent: false
            pipeline:
              - type: "{typeof(ConcurrentProbeStrategy).AssemblyQualifiedName}"
                params:
                  SignalKey: "AlphaA"
                  Score: 0.88
              - type: "{typeof(ConcurrentProbeStrategy).AssemblyQualifiedName}"
                params:
                  SignalKey: "AlphaB"
                  Score: 0.99
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        Assert.AreEqual(PipelineMode.Parallel, config.Mode);
        Assert.IsFalse(config.Concurrent);

        using var pipeline = StrategyPipelineFactory.Create(config);
        Assert.IsInstanceOfType<ParallelPipeline>(pipeline);
        var parallel = (ParallelPipeline)pipeline;
        Assert.IsFalse(parallel.Concurrent);

        var context = new MockEngineContext();
        pipeline.OnUpdate(context);
        Assert.AreEqual(0.88, context.Market.GetState<SignalScore>("AlphaA")?.Value);
        Assert.AreEqual(0.99, context.Market.GetState<SignalScore>("AlphaB")?.Value);
    }

    [TestMethod]
    public void Create_ParallelPipeline_ConcurrentTrue_ExecutesConcurrently()
    {
        string yaml = $"""
            name: "HighThroughputParallel"
            mode: "parallel"
            concurrent: true
            pipeline:
              - type: "{typeof(ConcurrentProbeStrategy).AssemblyQualifiedName}"
                params:
                  SignalKey: "Factor1"
                  Score: 1.1
              - type: "{typeof(ConcurrentProbeStrategy).AssemblyQualifiedName}"
                params:
                  SignalKey: "Factor2"
                  Score: 2.2
              - type: "{typeof(ConcurrentProbeStrategy).AssemblyQualifiedName}"
                params:
                  SignalKey: "Factor3"
                  Score: 3.3
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        Assert.AreEqual(PipelineMode.Parallel, config.Mode);
        Assert.IsTrue(config.Concurrent);

        using var pipeline = StrategyPipelineFactory.Create(config);
        Assert.IsInstanceOfType<ParallelPipeline>(pipeline);
        var parallel = (ParallelPipeline)pipeline;
        Assert.IsTrue(parallel.Concurrent);

        var context = new MockEngineContext();
        pipeline.OnUpdate(context);

        Assert.AreEqual(1.1, context.Market.GetState<SignalScore>("Factor1")?.Value);
        Assert.AreEqual(2.2, context.Market.GetState<SignalScore>("Factor2")?.Value);
        Assert.AreEqual(3.3, context.Market.GetState<SignalScore>("Factor3")?.Value);
    }

    [TestMethod]
    public void Create_NestedCompositePipeline_AggregatesLookbackAndExecutes()
    {
        string yaml = $"""
            name: "MasterWorkflow"
            mode: "sequential"
            pipeline:
              - name: "AlphaStage"
                mode: "parallel"
                concurrent: true
                pipeline:
                  - type: "{typeof(ConcurrentProbeStrategy).AssemblyQualifiedName}"
                    params:
                      SignalKey: "S1"
                      Score: 10.0
                  - type: "{typeof(ConcurrentProbeStrategy).AssemblyQualifiedName}"
                    params:
                      SignalKey: "S2"
                      Score: 20.0
              - type: "{typeof(TestInjectableStrategy).AssemblyQualifiedName}"
                params:
                  Tag: "Optimizer"
                  Period: 30
                  Lookback: 120
              - name: "RiskStage"
                mode: "parallel"
                concurrent: false
                pipeline:
                  - type: "{typeof(TestInjectableStrategy).AssemblyQualifiedName}"
                    params:
                      Tag: "Risk1"
                      Lookback: 300
                  - type: "{typeof(TestInjectableStrategy).AssemblyQualifiedName}"
                    params:
                      Tag: "Risk2"
                      Lookback: 150
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        Assert.AreEqual("MasterWorkflow", config.Name);
        Assert.AreEqual(3, config.Pipeline.Count);

        using var pipeline = StrategyPipelineFactory.Create(config);
        Assert.IsInstanceOfType<SequentialPipeline>(pipeline);
        Assert.AreEqual(300, pipeline.Lookback); // 递归聚合全局最大 Lookback: max(20, 20, 120, 300, 150) = 300

        var strategies = new List<IStrategy>(pipeline.GetStrategies());
        Assert.AreEqual(5, strategies.Count); // 展平后包含 5 个叶子策略

        var context = new MockEngineContext();
        pipeline.OnUpdate(context);

        Assert.AreEqual(10.0, context.Market.GetState<SignalScore>("S1")?.Value);
        Assert.AreEqual(20.0, context.Market.GetState<SignalScore>("S2")?.Value);
    }

    [TestMethod]
    public void Create_HybridScriptAndCompiledStrategy_ExecutesProperly()
    {
        const string scriptSource = @"
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Abstraction.Engine;

public class DynamicAlphaStrategy : IStrategy
{
    public string Name => ""DynamicAlpha"";
    public double Boost { get; set; } = 1.0;
    public int Lookback => 45;

    public void OnUpdate(IEngineContext context)
    {
        context.Market.SetState((99.0 * Boost).ToString(), ""DynamicResult"");
    }
}
";
        string scriptPath = Path.Combine(_tempDir, "DynamicAlpha.cs");
        File.WriteAllText(scriptPath, scriptSource);

        string yaml = $"""
            name: "HybridPipeline"
            mode: "sequential"
            pipeline:
              - source: "{scriptPath.Replace('\\', '/')}"
                params:
                  Boost: 2.0
              - type: "{typeof(ConcurrentProbeStrategy).AssemblyQualifiedName}"
                params:
                  SignalKey: "StaticResult"
                  Score: 77.0
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config, _tempDir);

        Assert.AreEqual(45, pipeline.Lookback);
        var context = new MockEngineContext();
        pipeline.OnUpdate(context);

        Assert.AreEqual("198", context.Market.GetState<string>("DynamicResult"));
        Assert.AreEqual(77.0, context.Market.GetState<SignalScore>("StaticResult")?.Value);
    }

    [TestMethod]
    public void Create_DirectFromFilePath_Succeeds()
    {
        string fullPath = Path.Combine(_tempDir, "disk_pipeline.yaml");
        File.WriteAllText(fullPath, $"""
            name: "FileDrivenPipeline"
            mode: "parallel"
            concurrent: true
            pipeline:
              - type: "{typeof(ConcurrentProbeStrategy).AssemblyQualifiedName}"
                params:
                  SignalKey: "FromDisk"
                  Score: 123.45
            """);

        using var pipeline = StrategyPipelineFactory.Create(fullPath);
        Assert.AreEqual("FileDrivenPipeline", pipeline.Name);
        Assert.IsTrue(((ParallelPipeline)pipeline).Concurrent);

        var context = new MockEngineContext();
        pipeline.OnUpdate(context);
        Assert.AreEqual(123.45, context.Market.GetState<SignalScore>("FromDisk")?.Value);
    }

    #endregion

    #region 2. 参数注入与类型映射 (Parameter Injection & Types)

    [TestMethod]
    public void Create_WithNullParam_InjectsNull()
    {
        string yaml = $"""
            pipeline:
              - type: "{typeof(TestInjectableStrategy).AssemblyQualifiedName}"
                params:
                  Tag: null
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config);
        var strategy = (TestInjectableStrategy)new List<IStrategy>(pipeline.GetStrategies())[0];
        Assert.IsNull(strategy.Tag);
    }

    [TestMethod]
    public void Create_WithEnumParam_InjectsEnumCorrectly()
    {
        string yaml = $"""
            pipeline:
              - type: "{typeof(TestInjectableStrategy).AssemblyQualifiedName}"
                params:
                  Mode: "Parallel"
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config);
        var strategy = (TestInjectableStrategy)new List<IStrategy>(pipeline.GetStrategies())[0];
        Assert.AreEqual(PipelineMode.Parallel, strategy.Mode);
    }

    [TestMethod]
    public void Create_WithComplexTypes_Array_List_TimeSpan_InjectsCorrectly()
    {
        string yaml = $"""
            pipeline:
              - type: "{typeof(TestInjectableStrategy).AssemblyQualifiedName}"
                params:
                  Symbols:
                    - "sh.600000"
                    - "sz.000001"
                  Windows:
                    - 5
                    - 10
                    - 20
                  Interval: "00:05:00"
            """;

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

    #endregion

    #region 3. 异常防御与边界分支 (Validation & Negative Cases)

    [TestMethod]
    public void Create_WithInvalidParamType_ThrowsDescriptiveException()
    {
        string yaml = $"""
            pipeline:
              - type: "{typeof(TestInjectableStrategy).AssemblyQualifiedName}"
                params:
                  Period: "not_an_int"
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            StrategyPipelineFactory.Create(config);
        });

        StringAssert.Contains(ex.Message, "注入参数 'Period' 失败");
    }

    [TestMethod]
    public void Create_WithUnknownProperty_ThrowsArgumentException()
    {
        string yaml = $"""
            pipeline:
              - type: "{typeof(TestInjectableStrategy).AssemblyQualifiedName}"
                params:
                  NonExistentProperty: 123
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        var ex = Assert.ThrowsException<ArgumentException>(() =>
        {
            StrategyPipelineFactory.Create(config);
        });

        StringAssert.Contains(ex.Message, "不存在可写入的公共属性 'NonExistentProperty'");
        StringAssert.Contains(ex.Message, "可用属性:");
    }

    [TestMethod]
    public void Create_EmptyOrMissingPipeline_ThrowsInvalidOperationException()
    {
        string yamlEmpty = """
            name: "EmptyPipeline"
            pipeline: []
            """;

        var ex1 = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            PipelineConfigLoader.FromYaml(yamlEmpty);
        });
        StringAssert.Contains(ex1.Message, "未包含有效的 'pipeline' 节点列表");

        string yamlMissing = """
            name: "NoPipeline"
            mode: "sequential"
            """;

        var ex2 = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            PipelineConfigLoader.FromYaml(yamlMissing);
        });
        StringAssert.Contains(ex2.Message, "未包含有效的 'pipeline' 节点列表");
    }

    [TestMethod]
    public void Create_EmptyNodeDefinition_ThrowsInvalidOperationException()
    {
        string yaml = """
            pipeline:
              - name: "EmptyNode"
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            StrategyPipelineFactory.Create(config);
        });

        StringAssert.Contains(ex.Message, "必须指定 'pipeline'");
    }

    [TestMethod]
    public void Create_NonExistentType_ThrowsTypeLoadException()
    {
        string yaml = """
            pipeline:
              - type: "NonExistentNamespace.FakeStrategy"
            """;

        var config = PipelineConfigLoader.FromYaml(yaml);
        Assert.ThrowsException<TypeLoadException>(() =>
        {
            StrategyPipelineFactory.Create(config);
        });
    }

    [TestMethod]
    public void Create_NonExistentFile_ThrowsFileNotFoundException()
    {
        Assert.ThrowsException<FileNotFoundException>(() =>
        {
            StrategyPipelineFactory.Create("non_existent_config.yaml");
        });
    }

    [TestMethod]
    public void Loader_FromYaml_EmptyContent_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() =>
        {
            PipelineConfigLoader.FromYaml("   ");
        });
    }

    #endregion
}
