using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    /// <summary>
    /// 复权因子事件数据模型（用户定义）。
    /// </summary>
    public record AdjustmentFactor(double BackAdjFactor);

    [TestClass]
    public class EventProviderTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        #region 1. CSV 加载测试

        [TestMethod]
        public void CsvEventProvider_ShouldLoadAndLookup()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var provider = EventProviderBuilder.Build<AdjustmentFactor>(resolver, "ashare.adj_factor.baostock");

            // 验证: sh.600000 在 2021-07-21 有复权因子 10.832619
            bool found = provider.TryGet(new DateTime(2021, 7, 21), "sh.600000", out var value);
            Assert.IsTrue(found, "Should find adj_factor for sh.600000 on 2021-07-21.");
            Assert.IsNotNull(value);
            Assert.AreEqual(10.832619, value!.BackAdjFactor, 1e-6);
        }

        [TestMethod]
        public void CsvEventProvider_ShouldReturnFalseForMissingDate()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var provider = EventProviderBuilder.Build<AdjustmentFactor>(resolver, "ashare.adj_factor.baostock");

            bool found = provider.TryGet(new DateTime(2021, 1, 1), "sh.600000", out _);
            Assert.IsFalse(found, "Should not find adj_factor on non-event date.");
        }

        [TestMethod]
        public void CsvEventProvider_DailySnapshot_ShouldReturnAllSymbols()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var provider = EventProviderBuilder.Build<AdjustmentFactor>(resolver, "ashare.adj_factor.baostock");

            // 2021-07-21 只有 sh.600000 一条记录
            bool found = provider.TryGetDaily(new DateTime(2021, 7, 21), out var snapshot);
            Assert.IsTrue(found);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(1, snapshot!.Count);
            Assert.IsTrue(snapshot.ContainsKey("sh.600000"));
        }

        [TestMethod]
        public void CsvEventProvider_MultipleYears_ShouldMerge()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var provider = EventProviderBuilder.Build<AdjustmentFactor>(resolver, "ashare.adj_factor.baostock");

            // 2021 和其他年份的数据都应该被加载
            bool found2021 = provider.TryGet(new DateTime(2021, 5, 28), "sh.600008", out var val2021);
            Assert.IsTrue(found2021, "Should load data from year=2021.");
            Assert.AreEqual(6.556426, val2021!.BackAdjFactor, 1e-6);
        }

        #endregion

        #region 2. Parquet 加载测试

        [TestMethod]
        public void ParquetEventProvider_ShouldLoadAndLookup()
        {
            string parquetRoot = Path.Combine(GetTestDataRoot(), "parquet");
            var resolver = new StorageResolver(parquetRoot);

            var provider = EventProviderBuilder.Build<AdjustmentFactor>(resolver, "ashare.adj_factor.baostock");

            bool found = provider.TryGet(new DateTime(2021, 7, 21), "sh.600000", out var value);
            Assert.IsTrue(found, "Should find adj_factor for sh.600000 on 2021-07-21.");
            Assert.IsNotNull(value);
            Assert.AreEqual(10.832619, value!.BackAdjFactor, 1e-6);
        }

        [TestMethod]
        public void ParquetEventProvider_DailySnapshot_ShouldReturnAllSymbols()
        {
            string parquetRoot = Path.Combine(GetTestDataRoot(), "parquet");
            var resolver = new StorageResolver(parquetRoot);

            var provider = EventProviderBuilder.Build<AdjustmentFactor>(resolver, "ashare.adj_factor.baostock");

            bool found = provider.TryGetDaily(new DateTime(2021, 7, 21), out var snapshot);
            Assert.IsTrue(found);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(1, snapshot!.Count);
            Assert.IsTrue(snapshot.ContainsKey("sh.600000"));
        }

        #endregion

        #region 3. CSV vs Parquet 一致性测试

        [TestMethod]
        public void CsvAndParquet_ShouldProduceSameResults()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            string parquetRoot = Path.Combine(GetTestDataRoot(), "parquet");
            var csvResolver = new StorageResolver(csvRoot);
            var parquetResolver = new StorageResolver(parquetRoot);

            var csvProvider = EventProviderBuilder.Build<AdjustmentFactor>(csvResolver, "ashare.adj_factor.baostock");
            var parquetProvider = EventProviderBuilder.Build<AdjustmentFactor>(parquetResolver, "ashare.adj_factor.baostock");

            // 逐条对比
            var testDates = new[]
            {
                new DateTime(2021, 5, 28), new DateTime(2021, 6, 4),
                new DateTime(2021, 7, 14), new DateTime(2021, 7, 21),
                new DateTime(2021, 8, 13)
            };
            var testSymbols = new[] { "sh.600008", "sh.600007", "sh.600006", "sh.600000", "sh.600004" };

            for (int i = 0; i < testDates.Length; i++)
            {
                csvProvider.TryGet(testDates[i], testSymbols[i], out var csvVal);
                parquetProvider.TryGet(testDates[i], testSymbols[i], out var pqVal);

                Assert.IsNotNull(csvVal, $"CSV: missing {testSymbols[i]} on {testDates[i]:yyyy-MM-dd}");
                Assert.IsNotNull(pqVal, $"Parquet: missing {testSymbols[i]} on {testDates[i]:yyyy-MM-dd}");
                Assert.AreEqual(csvVal!.BackAdjFactor, pqVal!.BackAdjFactor, 1e-6,
                    $"Mismatch for {testSymbols[i]} on {testDates[i]:yyyy-MM-dd}");
            }
        }

        #endregion
    }

    [TestClass]
    public class EventRegistryTests
    {
        [TestMethod]
        public void RegisterAndRetrieve_ShouldWork()
        {
            var registry = new EventRegistry();
            var provider = new EventProvider<AdjustmentFactor>(
                new Dictionary<DateTime, Dictionary<string, AdjustmentFactor>>());

            registry.Register("adjustments", provider);

            Assert.IsTrue(registry.HasStream("adjustments"));
            var retrieved = registry.GetProvider<AdjustmentFactor>("adjustments");
            Assert.AreSame(provider, retrieved);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void GetProvider_NonExistent_ShouldThrow()
        {
            var registry = new EventRegistry();
            registry.GetProvider<AdjustmentFactor>("non_existent");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidCastException))]
        public void GetProvider_WrongType_ShouldThrow()
        {
            var registry = new EventRegistry();
            var provider = new EventProvider<AdjustmentFactor>(
                new Dictionary<DateTime, Dictionary<string, AdjustmentFactor>>());

            registry.Register("adjustments", provider);
            registry.GetProvider<string>("adjustments");
        }

        [TestMethod]
        public void HasStream_ShouldReturnCorrectly()
        {
            var registry = new EventRegistry();
            Assert.IsFalse(registry.HasStream("adjustments"));

            var provider = new EventProvider<AdjustmentFactor>(
                new Dictionary<DateTime, Dictionary<string, AdjustmentFactor>>());
            registry.Register("adjustments", provider);

            Assert.IsTrue(registry.HasStream("adjustments"));
            Assert.IsFalse(registry.HasStream("other"));
        }
    }
}
