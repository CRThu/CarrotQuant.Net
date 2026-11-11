using System;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class TimeseriesSourceFactoryTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        [TestMethod]
        public void Create_WithCsvTable_ShouldReturnCsvTimeseriesSource()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var registry = new SimpleFieldRegistry();
            string tableId = "ashare.kline.1d.raw.baostock";

            using var source = TimeseriesSourceFactory.Create(csvRoot, registry, tableId);

            Assert.IsNotNull(source);
            Assert.IsInstanceOfType<CsvTimeseriesSource>(source);
            Assert.IsTrue(source.Symbols.Count > 0);
            Assert.IsTrue(source.TradeDates.Count > 0);
        }

        [TestMethod]
        public void Create_WithParquetTable_ShouldReturnParquetTimeseriesSource()
        {
            string parquetRoot = Path.Combine(GetTestDataRoot(), "parquet");
            var registry = new SimpleFieldRegistry();
            string tableId = "ashare.kline.1d.raw.baostock";

            using var source = TimeseriesSourceFactory.Create(parquetRoot, registry, tableId);

            Assert.IsNotNull(source);
            Assert.IsInstanceOfType<ParquetTimeseriesSource>(source);
            Assert.IsTrue(source.Symbols.Count > 0);
            Assert.IsTrue(source.TradeDates.Count > 0);
        }

        [TestMethod]
        public void Create_WithNullArguments_ShouldThrowArgumentException()
        {
            var registry = new SimpleFieldRegistry();
            var resolver = new StorageResolver(Path.Combine(GetTestDataRoot(), "csv"));

            Assert.ThrowsException<ArgumentNullException>(() =>
                TimeseriesSourceFactory.Create((IStorageResolver)null!, registry, "tableId"));

            Assert.ThrowsException<ArgumentNullException>(() =>
                TimeseriesSourceFactory.Create(resolver, null!, "tableId"));

            Assert.ThrowsException<ArgumentException>(() =>
                TimeseriesSourceFactory.Create(resolver, registry, "   "));
        }
    }
}
