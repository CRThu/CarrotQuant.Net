using CarrotBacktesting.NET.Abstraction.Data;
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
            var registry = new SimpleFieldRegistry();
            
            var storageRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
            var tableId = "ashare.kline.1d.raw.baostock";
            
            using var source = new CsvTimeseriesSource(storageRoot, registry, tableId);

            var provider = TimeseriesRegistryFactory.Create(storageRoot, registry, source);
            
            Assert.IsNotNull(provider);
            Assert.IsInstanceOfType(provider, typeof(TimeseriesRegistry));

            var providerWithCustomParams = TimeseriesRegistryFactory.Create(
                storageRoot,
                registry,
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
    }
}
