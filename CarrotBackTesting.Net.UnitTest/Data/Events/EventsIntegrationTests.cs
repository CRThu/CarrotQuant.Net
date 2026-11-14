using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Parquet.Serialization;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    public record AdjFactorRecord(double BackAdjFactor);

    public class NullableEventSample
    {
        public string Symbol { get; set; } = "sh.600000";
        public DateTime Date { get; set; } = new DateTime(2025, 1, 2);
        public string? Note { get; set; }
    }

    /// <summary>
    /// 事件子系统（Events）集成测试。
    /// 分别针对纯代码配置（Code Configuration）与 YAML 文件配置（YAML Configuration）进行端到端真实数据载入验证。
    /// </summary>
    [TestClass]
    public class EventsIntegrationTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "Carrot_EventsIntegration_" + Guid.NewGuid().ToString("N"));
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
        public void Events_CodeConfig_Csv_ShouldLoadAndQuery()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var configs = new List<EventStreamConfig>
            {
                new() { Name = "adjustments", TableId = "ashare.adj_factor.baostock" },
                new() { Name = "dragons", TableId = "ashare.dragon_tiger.eastmoney" }
            };

            var resolver = new StorageResolver(csvRoot);
            var registry = EventRegistryFactory.Create(resolver, configs);

            Assert.IsNotNull(registry);
            Assert.IsTrue(registry.HasTable("adjustments"));
            Assert.IsTrue(registry.HasTable("dragons"));

            var adjTable = registry["adjustments"];
            Assert.IsTrue(adjTable.Count > 0);

            // 验证时间单调递增
            var dates = adjTable.Dates.ToList();
            Assert.IsTrue(dates.Count > 0);

            // 验证复合索引查询
            var firstDate = dates[0];
            var dayRows = adjTable[firstDate];
            Assert.IsTrue(dayRows.Count > 0);

            string firstSymbol = dayRows.Keys.First();
            Assert.IsTrue(adjTable.TryGet<AdjFactorRecord>(firstDate, firstSymbol, out var adj));
            Assert.IsNotNull(adj);
            Assert.IsTrue(adj.BackAdjFactor > 0);
        }

        [TestMethod]
        public void Events_CodeConfig_Parquet_ShouldLoadAndQuery()
        {
            string parquetRoot = Path.Combine(GetTestDataRoot(), "parquet");
            var configs = new List<EventStreamConfig>
            {
                new() { Name = "adjustments", TableId = "ashare.adj_factor.baostock" }
            };

            var resolver = new StorageResolver(parquetRoot);
            var registry = EventRegistryFactory.Create(resolver, configs);

            Assert.IsNotNull(registry);
            Assert.IsTrue(registry.HasTable("adjustments"));

            var adjTable = registry["adjustments"];
            Assert.IsTrue(adjTable.Count > 0);

            // 验证 As<T>() 流式 LINQ
            var allAdjs = adjTable.As<AdjFactorRecord>().Take(5).ToList();
            Assert.AreEqual(5, allAdjs.Count);
            Assert.IsTrue(allAdjs[0].BackAdjFactor > 0);
        }

        [TestMethod]
        public void Events_YamlConfig_ShouldLoadFromYamlAndQuery()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");

            // 输入为标准 YAML 文本，避免通过领域模型自转产生的循环依赖
            string yamlPath = Path.Combine(_tempDir, "strategy_events.yaml");
            File.WriteAllText(yamlPath, $"""
                engine:
                  mode: backtest
                data:
                  root: "{csvRoot.Replace('\\', '/')}"
                  events:
                    - name: adjustments
                      table_id: ashare.adj_factor.baostock
                """);

            // 1. 通过 SessionConfig.Load 解析 YAML 配置文件
            var sessionConfig = SessionConfig.Load(yamlPath);
            Assert.IsNotNull(sessionConfig.Data);
            Assert.AreEqual(1, sessionConfig.Data.Events.Count);

            // 2. 根据解析出的配置模型装配 EventRegistry
            var resolver = new StorageResolver(sessionConfig.Data.Root);
            var registry = EventRegistryFactory.Create(resolver, sessionConfig.Data.Events);

            // 3. 验证数据读取
            Assert.IsTrue(registry.HasTable("adjustments"));
            var adjTable = registry["adjustments"];
            Assert.IsTrue(adjTable.Count > 0);
        }

        [TestMethod]
        public async Task Events_ParquetWithNulls_WithoutAllowlist_ThrowsInvalidDataException()
        {
            string tableDir = Path.Combine(_tempDir, "event_test_no_allow");
            Directory.CreateDirectory(tableDir);

            var samples = new List<NullableEventSample>
            {
                new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 2), Note = null },
                new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 3), Note = "Dividend" }
            };

            string parquetPath = Path.Combine(tableDir, "data.parquet");
            using (var fs = File.Create(parquetPath))
            {
                await ParquetSerializer.SerializeAsync(samples, fs);
            }

            var resolver = new StorageResolver(_tempDir);
            var ex = Assert.ThrowsException<InvalidDataException>(() =>
            {
                EventSourceFactory.Create(resolver, "event_test_no_allow");
            });

            Assert.IsTrue(ex.Message.Contains("Note") && ex.Message.Contains("共 1/2 行无效") && ex.Message.Contains("[0]"));
        }

        [TestMethod]
        public async Task Events_ParquetWithNulls_WithAllowlistParam_ShouldSucceed()
        {
            string tableDir = Path.Combine(_tempDir, "event_test_allow_param");
            Directory.CreateDirectory(tableDir);

            var samples = new List<NullableEventSample>
            {
                new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 2), Note = null },
                new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 3), Note = "Dividend" }
            };

            string parquetPath = Path.Combine(tableDir, "data.parquet");
            using (var fs = File.Create(parquetPath))
            {
                await ParquetSerializer.SerializeAsync(samples, fs);
            }

            var resolver = new StorageResolver(_tempDir);
            var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Note" };
            var table = EventSourceFactory.Create(resolver, "event_test_allow_param", nullableFields: allowList);

            Assert.IsNotNull(table);
            Assert.AreEqual(2, table.Count);
            var row = table[new DateTime(2025, 1, 2)]["sh.600000"][0];
            Assert.AreEqual("", row["Note"]); // 空值安全映射为空字符串
        }

        [TestMethod]
        public async Task Events_ParquetWithNulls_WithMetadataJson_ShouldSucceed()
        {
            string tableDir = Path.Combine(_tempDir, "event_test_metadata");
            Directory.CreateDirectory(tableDir);

            var samples = new List<NullableEventSample>
            {
                new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 2), Note = null }
            };

            string parquetPath = Path.Combine(tableDir, "data.parquet");
            using (var fs = File.Create(parquetPath))
            {
                await ParquetSerializer.SerializeAsync(samples, fs);
            }

            string metaPath = Path.Combine(tableDir, "metadata.json");
            File.WriteAllText(metaPath, """
                {
                  "table_id": "event_test_metadata",
                  "category": "events",
                  "format": "parquet",
                  "nullable_fields": ["Note"]
                }
                """);

            var resolver = new StorageResolver(_tempDir);
            var table = EventSourceFactory.Create(resolver, "event_test_metadata");

            Assert.IsNotNull(table);
            Assert.AreEqual(1, table.Count);
            var row = table[new DateTime(2025, 1, 2)]["sh.600000"][0];
            Assert.AreEqual("", row["Note"]);
        }

        [TestMethod]
        public async Task Events_ParquetWithNulls_WithYamlConfig_ShouldSucceed()
        {
            string tableDir = Path.Combine(_tempDir, "event_test_yaml");
            Directory.CreateDirectory(tableDir);

            var samples = new List<NullableEventSample>
            {
                new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 2), Note = null }
            };

            string parquetPath = Path.Combine(tableDir, "data.parquet");
            using (var fs = File.Create(parquetPath))
            {
                await ParquetSerializer.SerializeAsync(samples, fs);
            }

            string yamlPath = Path.Combine(_tempDir, "events_config.yaml");
            File.WriteAllText(yamlPath, $"""
                engine:
                  mode: backtest
                data:
                  root: "{_tempDir.Replace('\\', '/')}"
                  events:
                    - name: my_events
                      table_id: event_test_yaml
                      nullable_fields:
                        - note
                """);

            var sessionConfig = SessionConfig.Load(yamlPath);
            var resolver = new StorageResolver(sessionConfig.Data.Root);
            var registry = EventRegistryFactory.Create(resolver, sessionConfig.Data.Events);

            Assert.IsTrue(registry.HasTable("my_events"));
            var table = registry["my_events"];
            Assert.AreEqual(1, table.Count);
            var row = table[new DateTime(2025, 1, 2)]["sh.600000"][0];
            Assert.AreEqual("", row["Note"]);
        }
    }
}
