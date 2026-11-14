using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class TimeseriesRegistryFactoryTests
    {
        [TestMethod]
        public void TestFactoryCreation()
        {
            var storageRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
            var tableId = "ashare.kline.1d.raw.baostock";
            
            using var source = new CsvTimeseriesSource(storageRoot, tableId);

            var provider = TimeseriesRegistryFactory.Create(storageRoot, source);
            
            Assert.IsNotNull(provider);
            Assert.IsInstanceOfType(provider, typeof(TimeseriesRegistry));

            var providerWithCustomParams = TimeseriesRegistryFactory.Create(
                storageRoot,
                source,
                pageSize: 2048,
                enableCache: false,
                refreshMode: "lazy",
                tableId: tableId,
                compression: "lz4",
                commitBatchSize: 32,
                maxPages: 8,
                prefetch: false,
                isLive: true);

            Assert.IsNotNull(providerWithCustomParams);
            Assert.IsInstanceOfType(providerWithCustomParams, typeof(TimeseriesRegistry));
            ((System.IDisposable)provider).Dispose();
            ((System.IDisposable)providerWithCustomParams).Dispose();
        }

        [TestMethod]
        public void TimeseriesRegistryFactory_CreateFromConfig_ShouldWorkAndSupportStepTo()
        {
            var storageRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
            var config = new TimeseriesConfig
            {
                TableId = "ashare.kline.1d.raw.baostock",
                Cache = false,
                PageSize = 4096,
                MaxPages = 4,
                Prefetch = false
            };

            var resolver = new StorageResolver(storageRoot);
            var registry = TimeseriesRegistryFactory.Create(resolver, config);

            Assert.IsNotNull(registry);
            Assert.IsTrue(registry.HasField("close"));
            Assert.IsTrue(registry.Metadata.Symbols.Count > 0);

            // 验证 ITimeseriesRegistry.StepTo 契约方法可被直接调用
            registry.StepTo(0);

            var matrix = registry.GetMatrix<double>("close");
            Assert.IsNotNull(matrix);
            Assert.IsTrue(matrix.RowCount > 0);

            ((System.IDisposable)registry).Dispose();

            // 测试 storageRoot 直接重载
            var registry2 = TimeseriesRegistryFactory.Create(storageRoot, config);
            Assert.IsNotNull(registry2);
            ((System.IDisposable)registry2).Dispose();
        }

        public class FactoryNullableKlineSample
        {
            public string Symbol { get; set; } = "sh.600000";
            public DateTime Date { get; set; }
            public double? Close { get; set; }
        }

        [TestMethod]
        public async Task TimeseriesRegistryFactory_WithNullableFieldsInConfig_AllowsNullInParquet()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_factory_nullable_" + Guid.NewGuid().ToString("N"));
            string tableDir = Path.Combine(tempDir, "test_table");
            Directory.CreateDirectory(tableDir);

            try
            {
                var samples = new List<FactoryNullableKlineSample>
                {
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 2), Close = null },
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 3), Close = 30.0 }
                };

                string parquetPath = Path.Combine(tableDir, "data.parquet");
                using (var fs = File.Create(parquetPath))
                {
                    await Parquet.Serialization.ParquetSerializer.SerializeAsync(samples, fs);
                }

                // 1. 未配置 NullableFields 时，读取时必须抛出方案 C 诊断异常
                var strictConfig = new TimeseriesConfig
                {
                    TableId = "test_table",
                    Cache = false
                };
                var strictRegistry = TimeseriesRegistryFactory.Create(tempDir, strictConfig);
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    strictRegistry.GetMatrix<double>("Close");
                });
                Assert.IsTrue(ex.Message.Contains("Close") && ex.Message.Contains("共 1/2 行无效"));
                ((System.IDisposable)strictRegistry).Dispose();

                // 2. 在 TimeseriesConfig 中声明 NullableFields 白名单，允许并映射为 NaN
                var allowConfig = new TimeseriesConfig
                {
                    TableId = "test_table",
                    Cache = false,
                    NullableFields = new List<string> { "Close" }
                };
                var allowRegistry = TimeseriesRegistryFactory.Create(tempDir, allowConfig);
                var matrix = allowRegistry.GetMatrix<double>("Close");
                Assert.IsNotNull(matrix);
                Assert.AreEqual(2, matrix.RowCount);
                Assert.IsTrue(double.IsNaN(matrix[0, 0]));
                Assert.AreEqual(30.0, matrix[1, 0], 1e-4);
                ((System.IDisposable)allowRegistry).Dispose();
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
