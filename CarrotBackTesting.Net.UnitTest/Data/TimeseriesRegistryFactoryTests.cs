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
    }
}
