using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class DataProviderFactoryTests
    {
        [TestMethod]
        public void TestFactoryCreation()
        {
            var registry = new SimpleFieldRegistry();
            
            var storageRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
            var tableId = "ashare.kline.1d.raw.baostock";
            
            using var source = new CsvMarketSeriesSource(storageRoot, registry, tableId);

            var provider = DataProviderFactory.Create(storageRoot, registry, source);
            
            Assert.IsNotNull(provider);
            Assert.IsInstanceOfType(provider, typeof(BufferedDataProvider));
        }
    }
}
