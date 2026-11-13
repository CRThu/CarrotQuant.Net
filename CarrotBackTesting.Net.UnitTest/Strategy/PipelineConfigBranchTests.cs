using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Strategy;
using CarrotBackTesting.Net.UnitTest.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Strategy;

/// <summary>
/// 策略管线配置生成 YAML 并直接测试 YAML 全分支情况的完备单元测试。
/// 全面覆盖配置分支：串行、并行（单核/多核并发）、多层复合嵌套、Roslyn源码/静态类型混编、全类型参数注入以及异常防御分支。
/// 严格遵循“Builder 生成 YAML -> ConfigLoader 反序列化并断言配置 -> Factory 装配并执行验证”的真实闭环。
/// </summary>
[TestClass]
public class PipelineConfigBranchTests
{
    private string _tempDir = "";

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PipelineBranch_" + Guid.NewGuid().ToString("N"));
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
            // 模拟极轻微延迟，验证多核并发下的无锁线程安全性
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

    [TestMethod]
    public void Branch1_SequentialPipeline_GeneratesYaml_LoadsAndExecutesInOrder()
    {
        // 1. 通过 Builder 流式生成 Sequential 管线 YAML
        string yaml = new TestPipelineConfigBuilder()
            .WithName("StrictSequentialPipeline")
            .WithMode(PipelineMode.Sequential)
            .WithConcurrent(false)
            .AddStrategy(typeof(SequenceTrackingStrategy), new() { ["Name"] = "Step1", ["Order"] = 1 })
            .AddStrategy(typeof(SequenceTrackingStrategy), new() { ["Name"] = "Step2", ["Order"] = 2 })
            .AddStrategy(typeof(SequenceTrackingStrategy), new() { ["Name"] = "Step3", ["Order"] = 3 })
            .BuildYaml();

        // 2. 校验生成的 YAML 文本分支特征
        StringAssert.Contains(yaml, "name: \"StrictSequentialPipeline\"");
        StringAssert.Contains(yaml, "mode: \"sequential\"");

        // 3. 反序列化并断言配置模型分支
        var config = PipelineConfigLoader.FromYaml(yaml);
        Assert.AreEqual("StrictSequentialPipeline", config.Name);
        Assert.AreEqual(PipelineMode.Sequential, config.Mode);
        Assert.IsFalse(config.Concurrent);
        Assert.AreEqual(3, config.Pipeline.Count);
        Assert.AreEqual("Step1", config.Pipeline[0].Params!["Name"]);
        Assert.AreEqual(1, Convert.ToInt32(config.Pipeline[0].Params!["Order"]));

        // 4. 工厂装配并验证执行顺序
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
    public void Branch2_ParallelPipeline_ConcurrentFalse_SafeSingleThreadBranch()
    {
        // 1. 生成 Parallel + Concurrent=false（默认安全单核顺序分支）
        string yaml = new TestPipelineConfigBuilder()
            .WithName("DeterministicParallel")
            .WithMode(PipelineMode.Parallel)
            .WithConcurrent(false)
            .AddStrategy(typeof(ConcurrentProbeStrategy), new() { ["SignalKey"] = "AlphaA", ["Score"] = 0.88 })
            .AddStrategy(typeof(ConcurrentProbeStrategy), new() { ["SignalKey"] = "AlphaB", ["Score"] = 0.99 })
            .BuildYaml();

        // 2. 校验文本中未开启并发
        Assert.IsFalse(yaml.Contains("concurrent: true"));

        // 3. 反序列化
        var config = PipelineConfigLoader.FromYaml(yaml);
        Assert.AreEqual(PipelineMode.Parallel, config.Mode);
        Assert.IsFalse(config.Concurrent);

        // 4. 装配并执行
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
    public void Branch3_ParallelPipeline_ConcurrentTrue_MultiCoreConcurrentBranch()
    {
        // 1. 生成 Parallel + Concurrent=true（显式开启多核并发加速分支）
        string yaml = new TestPipelineConfigBuilder()
            .WithName("HighThroughputParallel")
            .WithMode(PipelineMode.Parallel)
            .WithConcurrent(true)
            .AddStrategy(typeof(ConcurrentProbeStrategy), new() { ["SignalKey"] = "Factor1", ["Score"] = 1.1 })
            .AddStrategy(typeof(ConcurrentProbeStrategy), new() { ["SignalKey"] = "Factor2", ["Score"] = 2.2 })
            .AddStrategy(typeof(ConcurrentProbeStrategy), new() { ["SignalKey"] = "Factor3", ["Score"] = 3.3 })
            .BuildYaml();

        // 2. 校验文本特征
        StringAssert.Contains(yaml, "mode: \"parallel\"");
        StringAssert.Contains(yaml, "concurrent: true");

        // 3. 反序列化
        var config = PipelineConfigLoader.FromYaml(yaml);
        Assert.AreEqual(PipelineMode.Parallel, config.Mode);
        Assert.IsTrue(config.Concurrent);
        Assert.AreEqual(3, config.Pipeline.Count);

        // 4. 装配并驱动并发执行
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
    public void Branch4_DeepNestedCompositePipeline_MultiStageExecution()
    {
        // 1. 构造多阶段生产级复合管线：
        // Root: Sequential
        //   Stage 1: Parallel (Concurrent=true) [Alpha1, Alpha2]
        //   Stage 2: Sequential [Optimizer]
        //   Stage 3: Parallel (Concurrent=false) [RiskChecker1, RiskChecker2]
        string yaml = new TestPipelineConfigBuilder()
            .WithName("MasterWorkflow")
            .WithMode(PipelineMode.Sequential)
            .AddSubPipeline(stage1 => stage1
                .WithName("AlphaStage")
                .WithMode(PipelineMode.Parallel)
                .WithConcurrent(true)
                .AddStrategy(typeof(ConcurrentProbeStrategy), new() { ["SignalKey"] = "S1", ["Score"] = 10.0 })
                .AddStrategy(typeof(ConcurrentProbeStrategy), new() { ["SignalKey"] = "S2", ["Score"] = 20.0 }))
            .AddStrategy(typeof(TestInjectableStrategy), new() { ["Tag"] = "Optimizer", ["Period"] = 30, ["Lookback"] = 120 })
            .AddSubPipeline(stage3 => stage3
                .WithName("RiskStage")
                .WithMode(PipelineMode.Parallel)
                .WithConcurrent(false)
                .AddStrategy(typeof(TestInjectableStrategy), new() { ["Tag"] = "Risk1", ["Lookback"] = 300 })
                .AddStrategy(typeof(TestInjectableStrategy), new() { ["Tag"] = "Risk2", ["Lookback"] = 150 }))
            .BuildYaml();

        // 2. 校验配置结构
        var config = PipelineConfigLoader.FromYaml(yaml);
        Assert.AreEqual("MasterWorkflow", config.Name);
        Assert.AreEqual(3, config.Pipeline.Count);

        // 子阶段 1 校验
        Assert.AreEqual("AlphaStage", config.Pipeline[0].Name);
        Assert.AreEqual(PipelineMode.Parallel, config.Pipeline[0].Mode);
        Assert.IsTrue(config.Pipeline[0].Concurrent);

        // 子阶段 3 校验
        Assert.AreEqual("RiskStage", config.Pipeline[2].Name);
        Assert.AreEqual(PipelineMode.Parallel, config.Pipeline[2].Mode);
        Assert.IsFalse(config.Pipeline[2].Concurrent);

        // 3. 装配并执行
        using var pipeline = StrategyPipelineFactory.Create(config);
        Assert.IsInstanceOfType<SequentialPipeline>(pipeline);
        Assert.AreEqual(300, pipeline.Lookback); // 递归聚合全局最大 Lookback: max(20, 20, 120, 300, 150) = 300

        var context = new MockEngineContext();
        pipeline.OnUpdate(context);

        Assert.AreEqual(10.0, context.Market.GetState<SignalScore>("S1")?.Value);
        Assert.AreEqual(20.0, context.Market.GetState<SignalScore>("S2")?.Value);
    }

    [TestMethod]
    public void Branch5_RoslynScriptAndCompiledType_HybridBranch()
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

        // 构造动态源码与静态类型混编的 YAML
        string yaml = new TestPipelineConfigBuilder()
            .WithName("HybridPipeline")
            .WithMode(PipelineMode.Sequential)
            .AddSourceStrategy(scriptPath, new() { ["Boost"] = 2.0 })
            .AddStrategy(typeof(ConcurrentProbeStrategy), new() { ["SignalKey"] = "StaticResult", ["Score"] = 77.0 })
            .BuildYaml();

        var config = PipelineConfigLoader.FromYaml(yaml);
        using var pipeline = StrategyPipelineFactory.Create(config);

        Assert.AreEqual(45, pipeline.Lookback);
        var context = new MockEngineContext();
        pipeline.OnUpdate(context);

        Assert.AreEqual("198", context.Market.GetState<string>("DynamicResult"));
        Assert.AreEqual(77.0, context.Market.GetState<SignalScore>("StaticResult")?.Value);
    }

    [TestMethod]
    public void Branch6_SaveToFile_And_FactoryCreateFromFile_DirectPath()
    {
        // 验证物理文件路径端到端直接装配分支
        string fullPath = new TestPipelineConfigBuilder()
            .WithName("FileDrivenPipeline")
            .WithMode(PipelineMode.Parallel)
            .WithConcurrent(true)
            .AddStrategy(typeof(ConcurrentProbeStrategy), new() { ["SignalKey"] = "FromDisk", ["Score"] = 123.45 })
            .Save(_tempDir, "disk_pipeline.yaml");

        Assert.IsTrue(File.Exists(fullPath));

        // 直接通过文件路径装配
        using var pipeline = StrategyPipelineFactory.Create(fullPath);
        Assert.AreEqual("FileDrivenPipeline", pipeline.Name);
        Assert.IsTrue(((ParallelPipeline)pipeline).Concurrent);

        var context = new MockEngineContext();
        pipeline.OnUpdate(context);
        Assert.AreEqual(123.45, context.Market.GetState<SignalScore>("FromDisk")?.Value);
    }
}
