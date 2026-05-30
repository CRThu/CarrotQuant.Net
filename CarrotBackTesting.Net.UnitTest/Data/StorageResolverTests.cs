using System;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class StorageResolverTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        [TestMethod]
        public void TestStorageResolver_MetadataLoading()
        {
            string testDataRoot = GetTestDataRoot();
            string csvRoot = Path.Combine(testDataRoot, "csv");
            var resolver = new StorageResolver(csvRoot);

            string tableId = "ashare.kline.1d.raw.baostock";
            
            Assert.IsTrue(resolver.HasTable(tableId));
            Assert.AreEqual("csv", resolver.GetFormat(tableId));
            Assert.AreEqual(StorageLayout.Hive, resolver.GetLayout(tableId));
            Assert.AreEqual("timeseries", resolver.GetCategory(tableId));
            Assert.AreEqual("symbol", resolver.GetPartition(tableId));

            var fields = resolver.GetFieldNames(tableId);
            Assert.IsTrue(fields.Contains("close"));
            Assert.AreEqual(typeof(double), resolver.GetFieldType(tableId, "close"));
        }

        [TestMethod]
        public void TestStorageResolver_ResolveFiles_HivePartitioning()
        {
            string testDataRoot = GetTestDataRoot();
            string csvRoot = Path.Combine(testDataRoot, "csv");
            var resolver = new StorageResolver(csvRoot);

            string tableId = "ashare.kline.1d.raw.baostock";

            // Test year pruning
            var files = resolver.ResolvePhysicalFiles(tableId, "sh.600000", new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));
            
            Assert.AreEqual(1, files.Count);
            Assert.IsTrue(files[0].Contains("year=2024"));
            Assert.IsTrue(files[0].EndsWith("sh.600000.csv"));
        }
    }
}
