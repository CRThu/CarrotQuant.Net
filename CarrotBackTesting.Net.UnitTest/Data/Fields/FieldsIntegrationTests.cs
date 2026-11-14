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
    public record IndustryModel(string BoardCode, string BoardName, string Symbol, string StockName);

    public class NullableFieldSample
    {
        public string Symbol { get; set; } = "sh.600000";
        public string? Concept { get; set; }
    }

    /// <summary>
    /// 标的维表子系统（Fields）集成测试。
    /// 分别针对纯代码配置（Code Configuration）与 YAML 文件配置（YAML Configuration）进行端到端真实数据载入验证。
    /// </summary>
    [TestClass]
    public class FieldsIntegrationTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "Carrot_FieldsIntegration_" + Guid.NewGuid().ToString("N"));
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
        public void Fields_CodeConfig_Csv_ShouldLoadAndQueryAndInvert()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var configs = new List<FieldTableConfig>
            {
                new() { Name = "industry", TableId = "ashare.industry.eastmoney", KeyColumn = "symbol" },
                new() { Name = "concept", TableId = "ashare.concept.eastmoney", KeyColumn = "symbol" }
            };

            var resolver = new StorageResolver(csvRoot);
            var registry = FieldRegistryFactory.Create(resolver, configs);

            Assert.IsNotNull(registry);
            Assert.IsTrue(registry.HasTable("industry"));
            Assert.IsTrue(registry.HasTable("concept"));

            var industryTable = registry["industry"];
            Assert.IsTrue(industryTable.Count > 0);

            // 1. 验证双向倒排选股
            var molyStocks = industryTable.GetKeys("钼");
            Assert.IsTrue(molyStocks.Count > 0);
            CollectionAssert.Contains(molyStocks.ToList(), "sh.601958");

            // 2. 验证强类型点查
            Assert.IsTrue(industryTable.TryGet<IndustryModel>("sh.601958", out var model));
            Assert.IsNotNull(model);
            Assert.AreEqual("金钼股份", model.StockName);
            Assert.AreEqual("钼", model.BoardName);

            // 3. 验证 AsPairs<T>() 流式 LINQ
            var pairs = industryTable.AsPairs<IndustryModel>().Take(10).ToList();
            Assert.AreEqual(10, pairs.Count);
        }

        [TestMethod]
        public void Fields_CodeConfig_Parquet_ShouldLoadAndQueryAndInvert()
        {
            string parquetRoot = Path.Combine(GetTestDataRoot(), "parquet");
            var configs = new List<FieldTableConfig>
            {
                new() { Name = "industry", TableId = "ashare.industry.eastmoney", KeyColumn = "symbol" }
            };

            var resolver = new StorageResolver(parquetRoot);
            var registry = FieldRegistryFactory.Create(resolver, configs);

            Assert.IsNotNull(registry);
            Assert.IsTrue(registry.HasTable("industry"));

            var industryTable = registry["industry"];
            Assert.IsTrue(industryTable.Count > 0);

            // 验证倒排圈选
            var tungstenStocks = industryTable.GetKeys("钨");
            Assert.IsTrue(tungstenStocks.Count > 0);

            // 验证强类型谓词倒排选股
            var matched = industryTable.GetKeys<IndustryModel>(x => x.BoardName == "钨");
            Assert.IsTrue(matched.Count > 0);
        }

        [TestMethod]
        public void Fields_YamlConfig_ShouldLoadFromYamlAndQuery()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");

            // 输入为标准 YAML 文本，避免通过领域模型自转产生的循环依赖
            string yamlPath = Path.Combine(_tempDir, "strategy_fields.yaml");
            File.WriteAllText(yamlPath, $"""
                engine:
                  mode: backtest
                data:
                  root: "{csvRoot.Replace('\\', '/')}"
                  fields:
                    - name: industry
                      table_id: ashare.industry.eastmoney
                      key_column: symbol
                """);

            // 1. 通过 SessionConfig.Load 解析 YAML 配置文件
            var sessionConfig = SessionConfig.Load(yamlPath);
            Assert.IsNotNull(sessionConfig.Data);
            Assert.AreEqual(1, sessionConfig.Data.Fields.Count);

            // 2. 根据解析出的配置模型装配 FieldRegistry
            var resolver = new StorageResolver(sessionConfig.Data.Root);
            var registry = FieldRegistryFactory.Create(resolver, sessionConfig.Data.Fields);

            // 3. 验证数据读取
            Assert.IsTrue(registry.HasTable("industry"));
            var industryTable = registry["industry"];
            Assert.IsTrue(industryTable.Count > 0);
            var stocks = industryTable.GetKeys("钼");
            Assert.IsTrue(stocks.Count > 0);
        }

        [TestMethod]
        public async Task Fields_ParquetWithNulls_WithoutAllowlist_ThrowsInvalidDataException()
        {
            string tableDir = Path.Combine(_tempDir, "field_test_no_allow");
            Directory.CreateDirectory(tableDir);

            var samples = new List<NullableFieldSample>
            {
                new() { Symbol = "sh.600000", Concept = null },
                new() { Symbol = "sh.600001", Concept = "Bank" }
            };

            string parquetPath = Path.Combine(tableDir, "data.parquet");
            using (var fs = File.Create(parquetPath))
            {
                await ParquetSerializer.SerializeAsync(samples, fs);
            }

            var resolver = new StorageResolver(_tempDir);
            var ex = Assert.ThrowsException<InvalidDataException>(() =>
            {
                FieldSourceFactory.Create(resolver, "field_test_no_allow");
            });

            Assert.IsTrue(ex.Message.Contains("Concept") && ex.Message.Contains("共 1/2 行无效") && ex.Message.Contains("[0]"));
        }

        [TestMethod]
        public async Task Fields_ParquetWithNulls_WithAllowlistParam_ShouldSucceed()
        {
            string tableDir = Path.Combine(_tempDir, "field_test_allow_param");
            Directory.CreateDirectory(tableDir);

            var samples = new List<NullableFieldSample>
            {
                new() { Symbol = "sh.600000", Concept = null },
                new() { Symbol = "sh.600001", Concept = "Bank" }
            };

            string parquetPath = Path.Combine(tableDir, "data.parquet");
            using (var fs = File.Create(parquetPath))
            {
                await ParquetSerializer.SerializeAsync(samples, fs);
            }

            var resolver = new StorageResolver(_tempDir);
            var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Concept" };
            var table = FieldSourceFactory.Create(resolver, "field_test_allow_param", nullableFields: allowList);

            Assert.IsNotNull(table);
            Assert.AreEqual(2, table.Count);
            Assert.AreEqual("", table["sh.600000"]["Concept"]); // 空值安全映射为空字符串
            Assert.AreEqual("Bank", table["sh.600001"]["Concept"]);
        }

        [TestMethod]
        public async Task Fields_ParquetWithNulls_WithMetadataJson_ShouldSucceed()
        {
            string tableDir = Path.Combine(_tempDir, "field_test_metadata");
            Directory.CreateDirectory(tableDir);

            var samples = new List<NullableFieldSample>
            {
                new() { Symbol = "sh.600000", Concept = null }
            };

            string parquetPath = Path.Combine(tableDir, "data.parquet");
            using (var fs = File.Create(parquetPath))
            {
                await ParquetSerializer.SerializeAsync(samples, fs);
            }

            string metaPath = Path.Combine(tableDir, "metadata.json");
            File.WriteAllText(metaPath, """
                {
                  "table_id": "field_test_metadata",
                  "category": "fields",
                  "format": "parquet",
                  "nullable_fields": ["Concept"]
                }
                """);

            var resolver = new StorageResolver(_tempDir);
            var table = FieldSourceFactory.Create(resolver, "field_test_metadata");

            Assert.IsNotNull(table);
            Assert.AreEqual(1, table.Count);
            Assert.AreEqual("", table["sh.600000"]["Concept"]);
        }

        [TestMethod]
        public async Task Fields_ParquetWithNulls_WithYamlConfig_ShouldSucceed()
        {
            string tableDir = Path.Combine(_tempDir, "field_test_yaml");
            Directory.CreateDirectory(tableDir);

            var samples = new List<NullableFieldSample>
            {
                new() { Symbol = "sh.600000", Concept = null }
            };

            string parquetPath = Path.Combine(tableDir, "data.parquet");
            using (var fs = File.Create(parquetPath))
            {
                await ParquetSerializer.SerializeAsync(samples, fs);
            }

            string yamlPath = Path.Combine(_tempDir, "fields_config.yaml");
            File.WriteAllText(yamlPath, $"""
                engine:
                  mode: backtest
                data:
                  root: "{_tempDir.Replace('\\', '/')}"
                  fields:
                    - name: concepts
                      table_id: field_test_yaml
                      key_column: symbol
                      nullable_fields:
                        - concept
                """);

            var sessionConfig = SessionConfig.Load(yamlPath);
            var resolver = new StorageResolver(sessionConfig.Data.Root);
            var registry = FieldRegistryFactory.Create(resolver, sessionConfig.Data.Fields);

            Assert.IsTrue(registry.HasTable("concepts"));
            var table = registry["concepts"];
            Assert.AreEqual(1, table.Count);
            Assert.AreEqual("", table["sh.600000"]["Concept"]);
        }
    }
}
