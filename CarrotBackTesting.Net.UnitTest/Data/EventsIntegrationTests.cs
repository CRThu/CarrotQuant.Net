using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBackTesting.Net.UnitTest.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    public record AdjFactorRecord(double BackAdjFactor);

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

            // 使用 TestYamlConfigBuilder 动态构造规范的测试 YAML 配置文件
            string yamlPath = new TestYamlConfigBuilder()
                .WithEngineSettings(mode: "backtest")
                .WithRoot(csvRoot)
                .AddEvent(name: "adjustments", tableId: "ashare.adj_factor.baostock")
                .Save(_tempDir, "strategy_events.yaml");

            // 1. 通过 EngineConfigLoader 解析 YAML 配置文件
            var engineConfig = EngineConfigLoader.Load(yamlPath);
            Assert.IsNotNull(engineConfig.Data);
            Assert.AreEqual(1, engineConfig.Data.Events.Count);

            // 2. 根据解析出的配置模型装配 EventRegistry
            var resolver = new StorageResolver(engineConfig.Data.Root);
            var registry = EventRegistryFactory.Create(resolver, engineConfig.Data.Events);

            // 3. 验证数据读取
            Assert.IsTrue(registry.HasTable("adjustments"));
            var adjTable = registry["adjustments"];
            Assert.IsTrue(adjTable.Count > 0);
        }
    }
}
