using System;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    /// <summary>
    /// 时序子系统（Timeseries）集成测试。
    /// 分别针对纯代码配置（Code Configuration）与 YAML 文件配置（YAML Configuration）进行端到端真实数据载入验证。
    /// </summary>
    [TestClass]
    public class TimeseriesIntegrationTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "Carrot_TsIntegration_" + Guid.NewGuid().ToString("N"));
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
        public void Timeseries_CodeConfig_Csv_ShouldLoadAndProvideMatrix()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var config = new TimeseriesConfig
            {
                TableId = "ashare.kline.1d.raw.baostock",
                Cache = false,
                PageSize = 4096,
                MaxPages = 4,
                Prefetch = false
            };

            var resolver = new StorageResolver(csvRoot);
            using var registry = (IDisposable)TimeseriesRegistryFactory.Create(resolver, config);
            var tsRegistry = (ITimeseriesRegistry)registry;

            Assert.IsNotNull(tsRegistry);
            Assert.IsTrue(tsRegistry.HasField("close"));
            Assert.IsTrue(tsRegistry.Metadata.Symbols.Count > 0);
            Assert.IsTrue(tsRegistry.Metadata.TradeDates.Count > 0);

            // 验证时步推进
            tsRegistry.StepTo(0);

            var closeMatrix = tsRegistry.GetMatrix<double>("close");
            Assert.IsNotNull(closeMatrix);
            Assert.AreEqual(tsRegistry.Metadata.TradeDates.Count, closeMatrix.RowCount);
            Assert.AreEqual(tsRegistry.Metadata.Symbols.Count, closeMatrix.Width);

            double firstVal = closeMatrix[0, 0];
            Assert.IsFalse(double.IsInfinity(firstVal));
        }

        [TestMethod]
        public void Timeseries_CodeConfig_Parquet_ShouldLoadAndProvideMatrix()
        {
            string parquetRoot = Path.Combine(GetTestDataRoot(), "parquet");
            var config = new TimeseriesConfig
            {
                TableId = "ashare.kline.1d.raw.baostock",
                Cache = false,
                PageSize = 4096,
                MaxPages = 4,
                Prefetch = false
            };

            var resolver = new StorageResolver(parquetRoot);
            using var registry = (IDisposable)TimeseriesRegistryFactory.Create(resolver, config);
            var tsRegistry = (ITimeseriesRegistry)registry;

            Assert.IsNotNull(tsRegistry);
            Assert.IsTrue(tsRegistry.HasField("close"));
            Assert.IsTrue(tsRegistry.Metadata.Symbols.Count > 0);
            Assert.IsTrue(tsRegistry.Metadata.TradeDates.Count > 0);

            var closeMatrix = tsRegistry.GetMatrix<double>("close");
            Assert.IsNotNull(closeMatrix);
            Assert.AreEqual(tsRegistry.Metadata.TradeDates.Count, closeMatrix.RowCount);
            Assert.AreEqual(tsRegistry.Metadata.Symbols.Count, closeMatrix.Width);
        }

        [TestMethod]
        public void Timeseries_YamlConfig_ShouldLoadFromYamlAndProvideMatrix()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");

            // 输入为标准 YAML 文本，避免通过领域模型自转产生的循环依赖
            string yamlPath = Path.Combine(_tempDir, "strategy_ts.yaml");
            File.WriteAllText(yamlPath, $"""
                engine:
                  mode: backtest
                  start_date: "2025-01-01 00:00:00"
                  end_date: "2025-12-31 00:00:00"
                data:
                  root: "{csvRoot.Replace('\\', '/')}"
                  timeseries:
                    - table_id: ashare.kline.1d.raw.baostock
                      cache: false
                      page_size: 4096
                      max_pages: 4
                      prefetch: false
                """);

            // 1. 通过 SessionConfig.Load 解析生成的 YAML 配置文件
            var sessionConfig = SessionConfig.Load(yamlPath);
            Assert.IsNotNull(sessionConfig.Data);
            Assert.AreEqual(1, sessionConfig.Data.Timeseries.Count);

            // 2. 根据解析出的配置模型装配 TimeseriesRegistry
            var resolver = new StorageResolver(sessionConfig.Data.Root);
            using var registry = (IDisposable)TimeseriesRegistryFactory.Create(resolver, sessionConfig.Data.Timeseries[0]);
            var tsRegistry = (ITimeseriesRegistry)registry;

            // 3. 验证数据读取
            Assert.IsTrue(tsRegistry.HasField("close"));
            var matrix = tsRegistry.GetMatrix<double>("close");
            Assert.IsNotNull(matrix);
            Assert.IsTrue(matrix.RowCount > 0);
            Assert.IsTrue(matrix.Width > 0);
        }
    }
}
