using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Config
{
    [TestClass]
    public class SessionConfigLoaderTests
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
            string configPath = Path.Combine(_tempDir, "config.yaml");
            // 输入为标准规范的 YAML 文本，彻底解耦待测类自身反向生成的循环依赖
            File.WriteAllText(configPath, """
                engine:
                  mode: backtest
                  start_date: "2025-01-01 00:00:00"
                  end_date: "2025-03-31 00:00:00"
                broker:
                  initial_cash: 500000.0
                data:
                  root: "./my_data"
                  timeseries:
                    - table_id: ashare.kline.1m.raw.custom
                      fields:
                        - open
                        - close
                        - volume
                  events:
                    - name: adjustments
                      table_id: ashare.adj_factor.baostock
                """);

            var config = SessionConfigLoader.Load(configPath);

            Assert.IsNotNull(config.Engine);
            Assert.AreEqual("backtest", config.Engine.Mode);
            Assert.AreEqual(new DateTime(2025, 1, 1), config.Engine.StartDate);
            Assert.AreEqual(new DateTime(2025, 3, 31), config.Engine.EndDate);

            Assert.IsNotNull(config.Broker);
            Assert.AreEqual(500000.0, config.Broker.InitialCash);

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

            string dataYamlPath = Path.Combine(sharedDir, "data.yaml");
            File.WriteAllText(dataYamlPath, """
                root: "../data_store"
                timeseries:
                  - table_id: ashare.kline.1d.raw.baostock
                events:
                  - name: dragons
                    table_id: ashare.dragon_tiger.eastmoney
                """);

            // 2. 创建通过多态 data 字段直接引用外部路径的主配置文件
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, """
                engine:
                  mode: living
                broker:
                  initial_cash: 2000000.0
                data: "shared/data.yaml"
                """);

            // 3. 加载并断言
            var config = SessionConfigLoader.Load(mainConfigPath);

            Assert.AreEqual("living", config.Engine.Mode);
            Assert.AreEqual(2000000.0, config.Broker.InitialCash);

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

            string dataYamlPath = Path.Combine(sharedDir, "data_wrapped.yaml");
            File.WriteAllText(dataYamlPath, """
                data:
                  root: "../data_store"
                  timeseries:
                    - table_id: "ashare.kline.1d.raw.baostock"
                """);

            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, """
                engine:
                  mode: backtest
                data: "shared/data_wrapped.yaml"
                """);

            var config = SessionConfigLoader.Load(mainConfigPath);

            Assert.IsNotNull(config.Data);
            string expectedRoot = Path.GetFullPath(Path.Combine(sharedDir, "../data_store"));
            Assert.AreEqual(expectedRoot, config.Data.Root);
            Assert.AreEqual(1, config.Data.Timeseries.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void TestMissingConfigFileThrows()
        {
            SessionConfigLoader.Load(Path.Combine(_tempDir, "non_existent.yaml"));
        }

        [TestMethod]
        [ExpectedException(typeof(FileNotFoundException))]
        public void TestMissingExternalDataConfigThrows()
        {
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, """
                engine:
                  mode: backtest
                data: "missing/data.yaml"
                """);

            SessionConfigLoader.Load(mainConfigPath);
        }

        [TestMethod]
        public void TestLoad_NullOrEmptyPath_ThrowsArgumentException()
        {
            Assert.ThrowsException<ArgumentException>(() => SessionConfigLoader.Load(""));
            Assert.ThrowsException<ArgumentException>(() => SessionConfigLoader.Load("   "));
            Assert.ThrowsException<ArgumentException>(() => SessionConfigLoader.Load(null!));
        }

        [TestMethod]
        public void TestLoadExternalDataConfig_WithAbsolutePath()
        {
            string sharedDir = Path.Combine(_tempDir, "shared");
            Directory.CreateDirectory(sharedDir);

            string dataYamlPath = Path.Combine(sharedDir, "data.yaml");
            File.WriteAllText(dataYamlPath, """
                root: "../data_store"
                timeseries:
                  - table_id: ashare.kline.1d.raw.baostock
                """);

            string absDataYamlPath = Path.GetFullPath(dataYamlPath).Replace('\\', '/');
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, $"""
                engine:
                  mode: backtest
                data: "{absDataYamlPath}"
                """);

            var config = SessionConfigLoader.Load(mainConfigPath);

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
            string dataYamlPath = Path.Combine(sharedDir, "data.yaml");
            File.WriteAllText(dataYamlPath, $"""
                root: "{absRoot}"
                timeseries:
                  - table_id: ashare.kline.1d.raw.baostock
                """);

            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, """
                engine:
                  mode: backtest
                data: "shared/data.yaml"
                """);

            var config = SessionConfigLoader.Load(mainConfigPath);

            Assert.IsNotNull(config.Data);
            Assert.AreEqual(Path.GetFullPath(absRoot), config.Data.Root);
        }

        [TestMethod]
        public void TestLoadConfig_WithOmittedDataSection()
        {
            string mainConfigPath = Path.Combine(_tempDir, "strategy.yaml");
            File.WriteAllText(mainConfigPath, """
                engine:
                  mode: backtest
                broker:
                  initial_cash: 800000.0
                """);

            var config = SessionConfigLoader.Load(mainConfigPath);

            Assert.IsNotNull(config.Engine);
            Assert.AreEqual("backtest", config.Engine.Mode);
            Assert.AreEqual(800000.0, config.Broker.InitialCash);
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

            var config = SessionConfigLoader.Load(mainConfigPath);

            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Engine);
            Assert.AreEqual("backtest", config.Engine.Mode);
            Assert.IsNotNull(config.Data);
        }

        [TestMethod]
        public void TestLoadConfig_TimeseriesCacheOptions_DefaultValues()
        {
            string configPath = Path.Combine(_tempDir, "default_cache.yaml");
            File.WriteAllText(configPath, """
                engine:
                  mode: backtest
                data:
                  root: "./data"
                  timeseries:
                    - table_id: ashare.kline.1d.raw.baostock
                """);

            var config = SessionConfigLoader.Load(configPath);

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
            string configPath = Path.Combine(_tempDir, "custom_cache.yaml");
            File.WriteAllText(configPath, """
                engine:
                  mode: backtest
                data:
                  root: "./data"
                  timeseries:
                    - table_id: ashare.kline.1m.raw.custom
                      cache: false
                      page_size: 1024
                      refresh: auto
                      compression: none
                    - table_id: ashare.kline.1d.raw.baostock
                      cache: true
                      page_size: 8192
                      refresh: force
                      compression: lz4
                """);

            var config = SessionConfigLoader.Load(configPath);

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

        [TestMethod]
        public void TestLoadConfig_WithFieldsSection_InlineConfiguration()
        {
            string configPath = Path.Combine(_tempDir, "fields_test.yaml");
            File.WriteAllText(configPath, """
                engine:
                  mode: backtest
                data:
                  root: "./data"
                  fields:
                    - name: industry
                      table_id: ashare.industry.eastmoney
                      key_column: symbol
                      value_columns:
                        - board_name
                    - name: concept
                      table_id: ashare.concept.eastmoney
                      key_column: symbol
                      value_columns:
                        - board_name
                """);

            var config = SessionConfigLoader.Load(configPath);

            Assert.IsNotNull(config.Data);
            Assert.IsNotNull(config.Data.Fields);
            Assert.AreEqual(2, config.Data.Fields.Count);

            var f0 = config.Data.Fields[0];
            Assert.AreEqual("industry", f0.Name);
            Assert.AreEqual("ashare.industry.eastmoney", f0.TableId);
            Assert.AreEqual("symbol", f0.KeyColumn);
            Assert.IsNotNull(f0.ValueColumns);
            Assert.AreEqual(1, f0.ValueColumns.Count);
            Assert.AreEqual("board_name", f0.ValueColumns[0]);

            var f1 = config.Data.Fields[1];
            Assert.AreEqual("concept", f1.Name);
            Assert.AreEqual("ashare.concept.eastmoney", f1.TableId);
            Assert.AreEqual("symbol", f1.KeyColumn);
            Assert.IsNotNull(f1.ValueColumns);
            Assert.AreEqual(1, f1.ValueColumns.Count);
            Assert.AreEqual("board_name", f1.ValueColumns[0]);
        }

        [TestMethod]
        public void TestSessionConfig_SaveAndLoad_Roundtrip()
        {
            var original = new SessionConfig
            {
                Engine = new EngineConfig
                {
                    Mode = "backtest",
                    StartDate = new DateTime(2025, 1, 1),
                    EndDate = new DateTime(2025, 12, 31)
                },
                Broker = new BrokerConfig
                {
                    InitialCash = 888888.0
                },
                Data = new DataConfig
                {
                    Root = _tempDir,
                    Timeseries =
                    [
                        new() { TableId = "ashare.kline.1d.raw.baostock", Cache = true, PageSize = 4096, Compression = "lz4" }
                    ],
                    Events =
                    [
                        new() { Name = "adjustments", TableId = "ashare.adj_factor.baostock" }
                    ],
                    Fields =
                    [
                        new() { Name = "industry", TableId = "ashare.industry.eastmoney", KeyColumn = "symbol" }
                    ]
                }
            };

            string filePath = Path.Combine(_tempDir, "roundtrip.yaml");
            original.Save(filePath);

            var loaded = SessionConfigLoader.Load(filePath);

            Assert.AreEqual(original.Engine.Mode, loaded.Engine.Mode);
            Assert.AreEqual(original.Engine.StartDate, loaded.Engine.StartDate);
            Assert.AreEqual(original.Engine.EndDate, loaded.Engine.EndDate);
            Assert.AreEqual(original.Broker.InitialCash, loaded.Broker.InitialCash);

            Assert.AreEqual(1, loaded.Data.Timeseries.Count);
            Assert.AreEqual("ashare.kline.1d.raw.baostock", loaded.Data.Timeseries[0].TableId);
            Assert.IsTrue(loaded.Data.Timeseries[0].Cache);

            Assert.AreEqual(1, loaded.Data.Events.Count);
            Assert.AreEqual("adjustments", loaded.Data.Events[0].Name);

            Assert.AreEqual(1, loaded.Data.Fields.Count);
            Assert.AreEqual("industry", loaded.Data.Fields[0].Name);
        }

        [TestMethod]
        public void TestLoadConfig_WithExternalDataYaml_GeneratedFromStandardDataTypes()
        {
            // 使用标准 C# 数据结构（Dictionary/List）构造测试数据，杜绝领域模型自转 YAML 造成的同源循环论证
            var standardDataDict = new Dictionary<string, object>
            {
                ["root"] = "./shared_data",
                ["timeseries"] = new List<object>
                {
                    new Dictionary<string, object> { ["table_id"] = "kline_1d" }
                }
            };

            string sharedPath = Path.Combine(_tempDir, "shared_data.yaml");
            YamlSerializer.Save(standardDataDict, sharedPath);

            // 主配置引用该共享文件
            string mainPath = Path.Combine(_tempDir, "main.yaml");
            File.WriteAllText(mainPath, $"""
                engine:
                  mode: living
                data: "{sharedPath.Replace('\\', '/')}"
                """);

            var loaded = SessionConfigLoader.Load(mainPath);
            Assert.AreEqual("living", loaded.Engine.Mode);
            Assert.AreEqual(1, loaded.Data.Timeseries.Count);
            Assert.AreEqual("kline_1d", loaded.Data.Timeseries[0].TableId);
        }

        [TestMethod]
        public void Test_ExhaustiveAllParameters_YamlAndCode_Coverage()
        {
            string fullYamlPath = Path.Combine(_tempDir, "exhaustive.yaml");
            File.WriteAllText(fullYamlPath, $"""
                engine:
                  mode: living
                  start_date: "2025-02-01 09:30:00"
                  end_date: "2025-11-30 15:00:00"
                broker:
                  initial_cash: 3000000.5
                data:
                  root: "{Path.GetFullPath(_tempDir).Replace('\\', '/')}"
                  timeseries:
                    - table_id: ashare.kline.1d.raw.baostock
                      cache: true
                      page_size: 2048
                      refresh: auto
                      compression: none
                      commit_batch_size: 32
                      max_pages: 8
                      prefetch: true
                      fields:
                        - open
                        - high
                        - low
                        - close
                        - volume
                  events:
                    - name: adj
                      table_id: ashare.adj_factor.baostock
                      fields:
                        - back_adj_factor
                  fields:
                    - name: ind
                      table_id: ashare.industry.eastmoney
                      key_column: symbol
                      value_columns:
                        - board_name
                        - stock_name
                """);

            string yamlText = File.ReadAllText(fullYamlPath);

            // 2. 校验生成的 YAML 文本中是否包含了所有符合规范的蛇形命名字段
            StringAssert.Contains(yamlText, "mode: living");
            StringAssert.Contains(yamlText, "initial_cash: 3000000.5");
            StringAssert.Contains(yamlText, "table_id: ashare.kline.1d.raw.baostock");
            StringAssert.Contains(yamlText, "cache: true");
            StringAssert.Contains(yamlText, "page_size: 2048");
            StringAssert.Contains(yamlText, "refresh: auto");
            StringAssert.Contains(yamlText, "compression: none");
            StringAssert.Contains(yamlText, "commit_batch_size: 32");
            StringAssert.Contains(yamlText, "max_pages: 8");
            StringAssert.Contains(yamlText, "prefetch: true");
            StringAssert.Contains(yamlText, "name: adj");
            StringAssert.Contains(yamlText, "table_id: ashare.adj_factor.baostock");
            StringAssert.Contains(yamlText, "key_column: symbol");

            // 3. 通过 SessionConfigLoader 反序列化并逐个字段严格断言
            var loaded = SessionConfigLoader.Load(fullYamlPath);

            // EngineConfig 参数 (3/3)
            Assert.AreEqual("living", loaded.Engine.Mode);
            Assert.AreEqual(new DateTime(2025, 2, 1, 9, 30, 0), loaded.Engine.StartDate);
            Assert.AreEqual(new DateTime(2025, 11, 30, 15, 0, 0), loaded.Engine.EndDate);
            Assert.AreEqual(3000000.5, loaded.Broker.InitialCash, 1e-4);

            // DataConfig 参数 (4/4)
            Assert.AreEqual(Path.GetFullPath(_tempDir), loaded.Data.Root);
            Assert.AreEqual(1, loaded.Data.Timeseries.Count);
            Assert.AreEqual(1, loaded.Data.Events.Count);
            Assert.AreEqual(1, loaded.Data.Fields.Count);

            // TimeseriesConfig 参数 (9/9)
            var ts = loaded.Data.Timeseries[0];
            Assert.AreEqual("ashare.kline.1d.raw.baostock", ts.TableId);
            Assert.IsTrue(ts.Cache);
            Assert.AreEqual(2048, ts.PageSize);
            Assert.AreEqual("auto", ts.Refresh);
            Assert.AreEqual("none", ts.Compression);
            Assert.AreEqual(32, ts.CommitBatchSize);
            Assert.AreEqual(8, ts.MaxPages);
            Assert.IsTrue(ts.Prefetch);
            Assert.IsNotNull(ts.Fields);
            CollectionAssert.AreEqual(new[] { "open", "high", "low", "close", "volume" }, ts.Fields);

            // EventStreamConfig 参数 (3/3)
            var ev = loaded.Data.Events[0];
            Assert.AreEqual("adj", ev.Name);
            Assert.AreEqual("ashare.adj_factor.baostock", ev.TableId);
            Assert.IsNotNull(ev.Fields);
            CollectionAssert.AreEqual(new[] { "back_adj_factor" }, ev.Fields);

            // FieldTableConfig 参数 (4/4)
            var fd = loaded.Data.Fields[0];
            Assert.AreEqual("ind", fd.Name);
            Assert.AreEqual("ashare.industry.eastmoney", fd.TableId);
            Assert.AreEqual("symbol", fd.KeyColumn);
            Assert.IsNotNull(fd.ValueColumns);
            CollectionAssert.AreEqual(new[] { "board_name", "stock_name" }, fd.ValueColumns);
        }

        [TestMethod]
        public void TestLoadBrokerConfig_FullParameters()
        {
            string configPath = Path.Combine(_tempDir, "broker_full.yaml");
            File.WriteAllText(configPath, """
                engine:
                  mode: backtest
                  start_date: "2025-01-01 00:00:00"
                  end_date: "2025-12-31 00:00:00"
                broker:
                  initial_cash: 5000000.0
                  settle_days: 0
                  allow_short: true
                  fee_rate: 0.0001
                  min_fee: 0.0
                  slippage: 0.0005
                  volume_limit: 0.15
                  check_limit: true
                  multiplier: 10.0
                  margin_rate: 0.1
                data:
                  root: "./test_data"
                """);

            var config = SessionConfigLoader.Load(configPath);

            Assert.IsNotNull(config.Broker);
            Assert.AreEqual(5000000.0, config.Broker.InitialCash, 1e-4);
            Assert.AreEqual(0, config.Broker.SettleDays);
            Assert.IsTrue(config.Broker.AllowShort);
            Assert.AreEqual(0.0001, config.Broker.FeeRate, 1e-6);
            Assert.AreEqual(0.0, config.Broker.MinFee, 1e-4);
            Assert.AreEqual(0.0005, config.Broker.Slippage, 1e-6);
            Assert.AreEqual(0.15, config.Broker.VolumeLimit, 1e-4);
            Assert.IsTrue(config.Broker.CheckLimit);
            Assert.AreEqual(10.0, config.Broker.Multiplier, 1e-4);
            Assert.AreEqual(0.1, config.Broker.MarginRate, 1e-4);
        }

        [TestMethod]
        public void TestBrokerConfig_SerializationAndHookIgnored()
        {
            var config = new SessionConfig
            {
                Engine = new EngineConfig { Mode = "backtest" },
                Broker = new BrokerConfig
                {
                    InitialCash = 2000000.0,
                    SettleDays = 1,
                    AllowShort = false,
                    FeeRate = 0.0003,
                    MinFee = 5.0,
                    Slippage = 0.001,
                    VolumeLimit = 0.1,
                    CheckLimit = true,
                    Multiplier = 1.0,
                    MarginRate = 1.0,

                    // 挂载动态 Hook 委托
                    OnCheckLimit = (order, bar) => true,
                    OnCalculateFee = (order, turnover) => turnover * 0.0003,
                    OnCheckVolume = (order, bar) => true,
                    OnCalculateSlippage = (order, price, bar) => price * 1.001,
                    OnGetSettleDays = symbol => 1,
                    OnGetMultiplier = (symbol, ctx) => 1.0,
                    OnGetMarginRate = (symbol, ctx) => 1.0
                },
                Data = new DataConfig { Root = _tempDir }
            };

            // 序列化为 YAML 文本
            string yaml = config.ToYaml();

            // 验证生成的 YAML 包含 broker 的纯数值属性，且由于 [YamlIgnore] 绝对不包含委托或报错
            StringAssert.Contains(yaml, "broker:");
            StringAssert.Contains(yaml, "initial_cash: 2000000");
            StringAssert.Contains(yaml, "settle_days: 1");
            StringAssert.Contains(yaml, "fee_rate: 0.0003");
            StringAssert.Contains(yaml, "min_fee: 5");
            StringAssert.Contains(yaml, "slippage: 0.001");
            StringAssert.Contains(yaml, "volume_limit: 0.1");
            StringAssert.Contains(yaml, "check_limit: true");
            Assert.IsFalse(yaml.Contains("on_check_limit"));
            Assert.IsFalse(yaml.Contains("on_calculate_fee"));

            // 保存后重新加载，断言数据完整闭环
            string savedPath = Path.Combine(_tempDir, "saved_broker.yaml");
            config.Save(savedPath);
            var reloaded = SessionConfigLoader.Load(savedPath);

            Assert.AreEqual(2000000.0, reloaded.Broker.InitialCash, 1e-4);
            Assert.AreEqual(1, reloaded.Broker.SettleDays);
            Assert.AreEqual(0.0003, reloaded.Broker.FeeRate, 1e-6);
            Assert.AreEqual(5.0, reloaded.Broker.MinFee, 1e-4);
            Assert.AreEqual(0.001, reloaded.Broker.Slippage, 1e-6);
            Assert.AreEqual(0.1, reloaded.Broker.VolumeLimit, 1e-4);
            Assert.IsTrue(reloaded.Broker.CheckLimit);
        }

        [TestMethod]
        public void TestNullableFields_YamlDeserializationAndRoundtrip()
        {
            string yaml = """
                data:
                  root: "./my_data"
                  timeseries:
                    - table_id: ashare.kline.1d.raw.baostock
                      nullable_fields:
                        - close
                        - volume
                  events:
                    - name: dividends
                      table_id: ashare.events.dividend
                      nullable_fields:
                        - symbol
                  fields:
                    - name: industry
                      table_id: ashare.industry.eastmoney
                      nullable_fields:
                        - board_name
                """;

            var config = SessionConfig.FromYaml(yaml, _tempDir);
            Assert.IsNotNull(config.Data);
            Assert.AreEqual(1, config.Data.Timeseries.Count);
            Assert.IsNotNull(config.Data.Timeseries[0].NullableFields);
            CollectionAssert.AreEqual(new[] { "close", "volume" }, config.Data.Timeseries[0].NullableFields);

            Assert.AreEqual(1, config.Data.Events.Count);
            Assert.IsNotNull(config.Data.Events[0].NullableFields);
            CollectionAssert.AreEqual(new[] { "symbol" }, config.Data.Events[0].NullableFields);

            Assert.AreEqual(1, config.Data.Fields.Count);
            Assert.IsNotNull(config.Data.Fields[0].NullableFields);
            CollectionAssert.AreEqual(new[] { "board_name" }, config.Data.Fields[0].NullableFields);

            // 往返序列化
            string serializedYaml = config.ToYaml();
            Assert.IsTrue(serializedYaml.Contains("nullable_fields"));

            var reloaded = SessionConfig.FromYaml(serializedYaml, _tempDir);
            CollectionAssert.AreEqual(new[] { "close", "volume" }, reloaded.Data.Timeseries[0].NullableFields);
            CollectionAssert.AreEqual(new[] { "symbol" }, reloaded.Data.Events[0].NullableFields);
            CollectionAssert.AreEqual(new[] { "board_name" }, reloaded.Data.Fields[0].NullableFields);
        }
    }
}
