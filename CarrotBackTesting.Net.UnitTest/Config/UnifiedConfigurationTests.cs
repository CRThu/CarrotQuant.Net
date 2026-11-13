using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Config
{
    [TestClass]
    public class UnifiedConfigurationTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UnifiedConfigTests_" + Guid.NewGuid().ToString("N"));
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

        [TestMethod]
        public void TestEngineConfig_FacadeMethods_RoundTrip()
        {
            // 1. Code-First 强类型构建
            var original = new EngineConfig
            {
                Engine = new EngineSettings
                {
                    Mode = "backtest",
                    StartDate = new DateTime(2025, 1, 1),
                    EndDate = new DateTime(2025, 6, 30)
                },
                Broker = new BrokerConfig
                {
                    InitialCash = 888888.0,
                    FeeRate = 0.0002
                },
                Data = new DataConfig
                {
                    Root = "./my_data",
                    Timeseries = new List<TimeseriesConfig>
                    {
                        new() { TableId = "ashare.kline.1d.raw.baostock" }
                    }
                }
            };

            // 2. 实例方法 ToYaml 序列化
            string yaml = original.ToYaml();
            Assert.IsFalse(string.IsNullOrWhiteSpace(yaml));
            StringAssert.Contains(yaml, "mode: backtest");
            StringAssert.Contains(yaml, "initial_cash: 888888");

            // 3. 静态门面 FromYaml 反序列化
            var loadedFromYaml = EngineConfig.FromYaml(yaml, _tempDir);
            Assert.AreEqual("backtest", loadedFromYaml.Engine.Mode);
            Assert.AreEqual(888888.0, loadedFromYaml.Broker.InitialCash);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(_tempDir, "./my_data")), loadedFromYaml.Data.Root);

            // 4. 实例方法 Save 落盘与静态门面 Load 加载
            string filePath = Path.Combine(_tempDir, "roundtrip.yaml");
            original.Save(filePath);
            Assert.IsTrue(File.Exists(filePath));

            var loadedFromFile = EngineConfig.Load(filePath);
            Assert.AreEqual("backtest", loadedFromFile.Engine.Mode);
            Assert.AreEqual(888888.0, loadedFromFile.Broker.InitialCash);
        }

        [TestMethod]
        public void TestPipelineConfiguration_FacadeAndSerializer_RoundTrip()
        {
            // 1. 构造管线配置模型
            var pipeline = new PipelineConfiguration
            {
                Name = "AlphaProductionPipeline",
                Mode = PipelineMode.Sequential,
                Concurrent = false,
                Pipeline = new List<StrategyNodeConfiguration>
                {
                    new()
                    {
                        Type = "CarrotBackTesting.Net.UnitTest.Strategy.DemoMovingAverageCrossStrategy",
                        Params = new Dictionary<string, object>
                        {
                            ["FastPeriod"] = 5,
                            ["SlowPeriod"] = 20
                        }
                    }
                }
            };

            // 2. 实例方法序列化
            string yaml = pipeline.ToYaml();
            Assert.IsFalse(string.IsNullOrWhiteSpace(yaml));
            StringAssert.Contains(yaml, "name: AlphaProductionPipeline");
            Assert.IsTrue(yaml.Contains("mode: sequential", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(yaml, "FastPeriod: 5");

            // 3. 静态方法反序列化
            var loadedFromYaml = PipelineConfiguration.FromYaml(yaml);
            Assert.AreEqual("AlphaProductionPipeline", loadedFromYaml.Name);
            Assert.AreEqual(PipelineMode.Sequential, loadedFromYaml.Mode);
            Assert.AreEqual(1, loadedFromYaml.Pipeline.Count);
            Assert.AreEqual("CarrotBackTesting.Net.UnitTest.Strategy.DemoMovingAverageCrossStrategy", loadedFromYaml.Pipeline[0].Type);

            // 4. 落盘与读取闭环
            string filePath = Path.Combine(_tempDir, "pipeline.yaml");
            pipeline.Save(filePath);
            Assert.IsTrue(File.Exists(filePath));

            var loadedFromFile = PipelineConfiguration.Load(filePath);
            Assert.AreEqual("AlphaProductionPipeline", loadedFromFile.Name);
            Assert.AreEqual(1, loadedFromFile.Pipeline.Count);
        }

        [TestMethod]
        public void TestUnifiedAllInOneConfig_WithInlinePipeline()
        {
            // 验证在单个总 YAML 文本中同时包含 engine, broker, data, pipeline
            string allInOneYaml = """
                engine:
                  mode: backtest
                  start_date: "2025-01-01"
                  end_date: "2025-03-31"

                broker:
                  initial_cash: 500000.0
                  fee_rate: 0.0003

                data:
                  root: "./data"
                  timeseries:
                    - table_id: "ashare.kline.1d.raw.baostock"

                pipeline:
                  name: "MyUnifiedStrategy"
                  mode: "sequential"
                  pipeline:
                    - type: "DemoStrategy"
                """;

            var config = EngineConfig.FromYaml(allInOneYaml, _tempDir);

            // 验证引擎参数
            Assert.AreEqual("backtest", config.Engine.Mode);
            Assert.AreEqual(new DateTime(2025, 1, 1), config.Engine.StartDate);
            Assert.AreEqual(500000.0, config.Broker.InitialCash);
            Assert.AreEqual(1, config.Data.Timeseries.Count);

            // 验证一站式内嵌管线
            Assert.IsNotNull(config.Pipeline);
            Assert.AreEqual("MyUnifiedStrategy", config.Pipeline.Name);
            Assert.AreEqual(PipelineMode.Sequential, config.Pipeline.Mode);
            Assert.AreEqual(1, config.Pipeline.Pipeline.Count);
            Assert.AreEqual("DemoStrategy", config.Pipeline.Pipeline[0].Type);
        }

        [TestMethod]
        public void TestUnifiedConfig_WithExternalPipeline()
        {
            // 1. 创建子目录并写入独立的 pipeline.yaml
            string strategiesDir = Path.Combine(_tempDir, "strategies");
            Directory.CreateDirectory(strategiesDir);

            var externalPipeline = new PipelineConfiguration
            {
                Name = "ExternalAlpha",
                Mode = PipelineMode.Parallel,
                Concurrent = true,
                Pipeline = new List<StrategyNodeConfiguration>
                {
                    new() { Type = "Alpha1" },
                    new() { Type = "Alpha2" }
                }
            };
            string pipelinePath = Path.Combine(strategiesDir, "alpha.yaml");
            externalPipeline.Save(pipelinePath);

            // 2. 创建主配置文件，通过 pipeline 多态外链引用相对路径
            string mainYaml = """
                engine:
                  mode: backtest
                broker:
                  initial_cash: 1000000.0
                data:
                  root: "./data"
                pipeline: "strategies/alpha.yaml"
                """;

            string mainConfigPath = Path.Combine(_tempDir, "main.yaml");
            File.WriteAllText(mainConfigPath, mainYaml);

            // 3. 一键加载并断言级联解析
            var config = EngineConfig.Load(mainConfigPath);

            Assert.IsNotNull(config.Pipeline);
            Assert.AreEqual("ExternalAlpha", config.Pipeline.Name);
            Assert.AreEqual(PipelineMode.Parallel, config.Pipeline.Mode);
            Assert.AreEqual(2, config.Pipeline.Pipeline.Count);
            Assert.AreEqual("Alpha1", config.Pipeline.Pipeline[0].Type);
            Assert.AreEqual("Alpha2", config.Pipeline.Pipeline[1].Type);
        }

        [TestMethod]
        public void TestDataConfig_StandaloneSaveAndToYaml()
        {
            var data = new DataConfig
            {
                Root = "./shared_store",
                Timeseries = new List<TimeseriesConfig>
                {
                    new() { TableId = "crypto.kline.1m.binance" }
                },
                Events = new List<EventStreamConfig>
                {
                    new() { Name = "funding_rate", TableId = "crypto.funding.binance" }
                }
            };

            string yaml = data.ToYaml();
            StringAssert.Contains(yaml, "table_id: crypto.kline.1m.binance");
            StringAssert.Contains(yaml, "name: funding_rate");

            string savePath = Path.Combine(_tempDir, "shared_data.yaml");
            data.Save(savePath);
            Assert.IsTrue(File.Exists(savePath));
            string content = File.ReadAllText(savePath);
            StringAssert.Contains(content, "root: ./shared_store");
        }
    }
}
