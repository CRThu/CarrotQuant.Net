using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    public record IndustryModel(string BoardCode, string BoardName, string Symbol, string StockName);

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
    }
}
