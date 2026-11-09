using System;
using System.IO;
using CarrotBacktesting.NET.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Config
{
    [TestClass]
    public class EngineConfigLoaderTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CarrotConfigTests_" + Guid.NewGuid().ToString("N"));
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
        public void TestLoadInlineDataConfig_WithRoot()
        {
            string yaml = @"
engine:
  mode: ""backtest""
  start_date: ""2025-01-01""
  end_date: ""2025-03-31""
  initial_cash: 500000.0

data:
  root: ""./my_data""
  timeseries:
    - table_id: ""ashare.kline.1m.raw.custom""
      fields: [""open"", ""close"", ""volume""]
  events:
    - name: ""adjustments""
      table_id: ""ashare.adj_factor.baostock""
";
            string configPath = Path.Combine(_tempDir, "config.yaml");
            File.WriteAllText(configPath, yaml);

            var config = EngineConfigLoader.Load(configPath);

            Assert.IsNotNull(config.Engine);
            Assert.AreEqual("backtest", config.Engine.Mode);
            Assert.AreEqual(new DateTime(2025, 1, 1), config.Engine.StartDate);
            Assert.AreEqual(new DateTime(2025, 3, 31), config.Engine.EndDate);
            Assert.AreEqual(500000.0, config.Engine.InitialCash);

            Assert.IsNotNull(config.Data);
            string expectedRoot = Path.GetFullPath(Path.Combine(_tempDir, "my_data"));
            Assert.AreEqual(expectedRoot, config.Data.Root);

            Assert.AreEqual(1, config.Data.Timeseries.Count);
            Assert.AreEqual("ashare.kline.1m.raw.custom", config.Data.Timeseries[0].TableId);
            CollectionAssert.AreEqual(new[] { "open", "close", "volume" }, config.Data.Timeseries[0].Fields);

            Assert.AreEqual(1, config.Data.Events.Count);
            Assert.AreEqual("adjustments", config.Data.Events[0].Name);
            Assert.AreEqual("ashare.adj_factor.baostock", config.Data.Events[0].TableId);
        }

        [TestMethod]
        public void TestLoadExternalDataConfig_WithRoot()
        {
            // 1. 创建子目录存放共享 data.yaml
            string sharedDir = Path.Combine(_tempDir, "shared");
            Directory.CreateDirectory(sharedDir);

            string dataYaml = @"
root: ""../data_store""
timeseries:
  - table_id: ""ashare.kline.1d.raw.baostock""
events:
  - name: ""dragons""
    table_id: ""ashare.dragon_tiger.eastmoney""
";
            string dataYamlPath = Path.Combine(sharedDir, "data.yaml");
            File.WriteAllText(dataYamlPath, dataYaml);

            // 2. 创建引用外部 data.yaml 的主策略配置文件
            string mainYaml = @"
engine:
  mode: ""living""
  initial_cash: 2000000.0

data_config: ""shared/data.yaml""
";
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, mainYaml);

            // 3. 加载并断言
            var config = EngineConfigLoader.Load(mainConfigPath);

            Assert.AreEqual("living", config.Engine.Mode);
            Assert.AreEqual(2000000.0, config.Engine.InitialCash);

            Assert.IsNotNull(config.Data);
            string expectedRoot = Path.GetFullPath(Path.Combine(sharedDir, "../data_store"));
            Assert.AreEqual(expectedRoot, config.Data.Root);

            Assert.AreEqual(1, config.Data.Timeseries.Count);
            Assert.AreEqual("ashare.kline.1d.raw.baostock", config.Data.Timeseries[0].TableId);

            Assert.AreEqual(1, config.Data.Events.Count);
            Assert.AreEqual("dragons", config.Data.Events[0].Name);
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void TestMissingConfigFileThrows()
        {
            EngineConfigLoader.Load(Path.Combine(_tempDir, "non_existent.yaml"));
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void TestMissingExternalDataConfigThrows()
        {
            string mainYaml = @"
engine:
  mode: ""backtest""
data_config: ""missing/data.yaml""
";
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, mainYaml);

            EngineConfigLoader.Load(mainConfigPath);
        }
    }
}
