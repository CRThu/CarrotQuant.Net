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
            // 1. 创建子目录存放共享 data.yaml (平铺以 root: 开头)
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

            // 2. 创建通过多态 data 字段直接引用外部路径的主配置文件
            string mainYaml = @"
engine:
  mode: ""living""
  initial_cash: 2000000.0

data: ""shared/data.yaml""
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
        public void TestLoadExternalDataConfig_WithWrappedData()
        {
            // 1. 创建带 data: 包装的外部配置文件（兼容容错）
            string sharedDir = Path.Combine(_tempDir, "shared");
            Directory.CreateDirectory(sharedDir);

            string dataYaml = @"
data:
  root: ""../data_store""
  timeseries:
    - table_id: ""ashare.kline.1d.raw.baostock""
";
            string dataYamlPath = Path.Combine(sharedDir, "data_wrapped.yaml");
            File.WriteAllText(dataYamlPath, dataYaml);

            string mainYaml = @"
engine:
  mode: ""backtest""
data: ""shared/data_wrapped.yaml""
";
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, mainYaml);

            var config = EngineConfigLoader.Load(mainConfigPath);

            Assert.IsNotNull(config.Data);
            string expectedRoot = Path.GetFullPath(Path.Combine(sharedDir, "../data_store"));
            Assert.AreEqual(expectedRoot, config.Data.Root);
            Assert.AreEqual(1, config.Data.Timeseries.Count);
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
data: ""missing/data.yaml""
";
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, mainYaml);

            EngineConfigLoader.Load(mainConfigPath);
        }

        [TestMethod]
        public void TestLoad_NullOrEmptyPath_ThrowsArgumentException()
        {
            Assert.ThrowsException<ArgumentException>(() => EngineConfigLoader.Load(""));
            Assert.ThrowsException<ArgumentException>(() => EngineConfigLoader.Load("   "));
            Assert.ThrowsException<ArgumentException>(() => EngineConfigLoader.Load(null!));
        }

        [TestMethod]
        public void TestLoadExternalDataConfig_WithAbsolutePath()
        {
            string sharedDir = Path.Combine(_tempDir, "shared");
            Directory.CreateDirectory(sharedDir);

            string dataYaml = @"
root: ""../data_store""
timeseries:
  - table_id: ""ashare.kline.1d.raw.baostock""
";
            string dataYamlPath = Path.Combine(sharedDir, "data.yaml");
            File.WriteAllText(dataYamlPath, dataYaml);

            // 使用正斜杠格式的绝对路径
            string absDataYamlPath = Path.GetFullPath(dataYamlPath).Replace('\\', '/');

            string mainYaml = $@"
engine:
  mode: ""backtest""
data: ""{absDataYamlPath}""
";
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, mainYaml);

            var config = EngineConfigLoader.Load(mainConfigPath);

            Assert.IsNotNull(config.Data);
            string expectedRoot = Path.GetFullPath(Path.Combine(sharedDir, "../data_store"));
            Assert.AreEqual(expectedRoot, config.Data.Root);
            Assert.AreEqual(1, config.Data.Timeseries.Count);
        }

        [TestMethod]
        public void TestExternalDataConfig_WithAbsoluteRootPath()
        {
            string sharedDir = Path.Combine(_tempDir, "shared");
            Directory.CreateDirectory(sharedDir);

            string absRoot = Path.Combine(_tempDir, "absolute_root").Replace('\\', '/');

            string dataYaml = $@"
root: ""{absRoot}""
timeseries:
  - table_id: ""ashare.kline.1d.raw.baostock""
";
            string dataYamlPath = Path.Combine(sharedDir, "data.yaml");
            File.WriteAllText(dataYamlPath, dataYaml);

            string mainYaml = @"
engine:
  mode: ""backtest""
data: ""shared/data.yaml""
";
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, mainYaml);

            var config = EngineConfigLoader.Load(mainConfigPath);

            Assert.IsNotNull(config.Data);
            Assert.AreEqual(Path.GetFullPath(absRoot), config.Data.Root);
        }

        [TestMethod]
        public void TestLoadConfig_WithOmittedDataSection()
        {
            string mainYaml = @"
engine:
  mode: ""backtest""
  initial_cash: 800000.0
";
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, mainYaml);

            var config = EngineConfigLoader.Load(mainConfigPath);

            Assert.IsNotNull(config.Engine);
            Assert.AreEqual("backtest", config.Engine.Mode);
            Assert.AreEqual(800000.0, config.Engine.InitialCash);
            Assert.IsNotNull(config.Data);
            Assert.AreEqual(string.Empty, config.Data.Root);
            Assert.AreEqual(0, config.Data.Timeseries.Count);
            Assert.AreEqual(0, config.Data.Events.Count);
        }

        [TestMethod]
        public void TestLoadConfig_EmptyYamlFile()
        {
            string mainYaml = "# This is a comment only yaml\n";
            string mainConfigPath = Path.Combine(_tempDir, "empty.yaml");
            File.WriteAllText(mainConfigPath, mainYaml);

            var config = EngineConfigLoader.Load(mainConfigPath);

            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Engine);
            Assert.AreEqual("backtest", config.Engine.Mode);
            Assert.IsNotNull(config.Data);
        }

        [TestMethod]
        public void TestLoadConfig_TimeseriesCacheOptions_DefaultValues()
        {
            string yaml = @"
engine:
  mode: ""backtest""
data:
  root: ""./data""
  timeseries:
    - table_id: ""ashare.kline.1d.raw.baostock""
";
            string configPath = Path.Combine(_tempDir, "default_cache.yaml");
            File.WriteAllText(configPath, yaml);

            var config = EngineConfigLoader.Load(configPath);

            Assert.AreEqual(1, config.Data.Timeseries.Count);
            var ts = config.Data.Timeseries[0];
            Assert.AreEqual("ashare.kline.1d.raw.baostock", ts.TableId);
            Assert.IsTrue(ts.Cache, "Cache 默认值应为 true");
            Assert.AreEqual(4096, ts.PageSize, "PageSize 默认值应为 4096");
            Assert.AreEqual("lazy", ts.Refresh, "Refresh 默认值应为 lazy");
            Assert.AreEqual("lz4", ts.Compression, "Compression 默认值应为 lz4");
        }

        [TestMethod]
        public void TestLoadConfig_TimeseriesCacheOptions_CustomConfigurations()
        {
            string yaml = @"
engine:
  mode: ""backtest""
data:
  root: ""./data""
  timeseries:
    - table_id: ""ashare.kline.1m.raw.custom""
      cache: false
      page_size: 1024
      refresh: ""auto""
      compression: ""none""
    - table_id: ""ashare.kline.1d.raw.baostock""
      cache: true
      page_size: 8192
      refresh: ""force""
";
            string configPath = Path.Combine(_tempDir, "custom_cache.yaml");
            File.WriteAllText(configPath, yaml);

            var config = EngineConfigLoader.Load(configPath);

            Assert.AreEqual(2, config.Data.Timeseries.Count);
            
            var ts0 = config.Data.Timeseries[0];
            Assert.AreEqual("ashare.kline.1m.raw.custom", ts0.TableId);
            Assert.IsFalse(ts0.Cache);
            Assert.AreEqual(1024, ts0.PageSize);
            Assert.AreEqual("auto", ts0.Refresh);
            Assert.AreEqual("none", ts0.Compression);

            var ts1 = config.Data.Timeseries[1];
            Assert.AreEqual("ashare.kline.1d.raw.baostock", ts1.TableId);
            Assert.IsTrue(ts1.Cache);
            Assert.AreEqual(8192, ts1.PageSize);
            Assert.AreEqual("force", ts1.Refresh);
            Assert.AreEqual("lz4", ts1.Compression);
        }
    }
}
