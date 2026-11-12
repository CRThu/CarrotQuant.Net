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
    /// 复权因子事件数据模型。
    /// </summary>
    public record AdjustmentFactor(double BackAdjFactor);

    [TestClass]
    public class EventTableTests
    {
        internal static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        #region 1. 索引器与截面遍历测试

        [TestMethod]
        public void CsvEventTable_ChainedIndexers_ShouldWork()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = EventSourceFactory.Create(resolver, "ashare.adj_factor.baostock");
            var dt = new DateTime(2021, 7, 21);

            // 1. 强类型即时点查: table.Get<T>(date, symbol).Property
            AdjustmentFactor item = table.Get<AdjustmentFactor>(dt, "sh.600000");
            Assert.IsNotNull(item);
            Assert.AreEqual(10.832619, item.BackAdjFactor, 1e-6);

            // 2. 免定义动态复合索引器: table[date, symbol][col]
            IDataRow row = table[dt, "sh.600000"];
            Assert.IsNotNull(row);
            Assert.AreEqual(10.832619, row.GetDouble("back_adj_factor"), 1e-6);

            // 3. 注册中心索引下钻: Events[stream][date, symbol][col]
            var registry = new EventRegistry();
            registry.Register("adj", table);
            string strVal = registry["adj"][dt, "sh.600000"]["back_adj_factor"];
            Assert.AreEqual("10.832619", strVal);

            // 4. 注册中心强类型点查
            var regItem = registry["adj"].Get<AdjustmentFactor>(dt, "sh.600000");
            Assert.AreEqual(10.832619, regItem.BackAdjFactor, 1e-6);
        }

        [TestMethod]
        public void CsvEventTable_DaySnapshot_ShouldSupportIteration()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = EventSourceFactory.Create(resolver, "ashare.adj_factor.baostock");
            var dt = new DateTime(2021, 7, 21);

            // 获取当日截面字典 table[date]
            IReadOnlyDictionary<string, IReadOnlyList<IDataRow>> dayTable = table[dt];
            Assert.IsNotNull(dayTable);
            Assert.IsTrue(dayTable.Count > 0);

            // 遍历当日发生事件的所有标的代码
            var symbols = dayTable.Keys.ToList();
            Assert.AreEqual(1, symbols.Count);
            Assert.AreEqual("sh.600000", symbols[0]);

            // 点查与多行获取
            bool found = dayTable.TryGetValue("sh.600000", out var rows);
            Assert.IsTrue(found);
            Assert.AreEqual(1, rows!.Count);
            Assert.AreEqual(10.832619, rows[0].GetDouble("back_adj_factor"), 1e-6);

            // foreach 直接遍历 dayTable (KeyValuePair<string, IReadOnlyList<IDataRow>>)
            int dayEnumCount = 0;
            foreach (var (sym, list) in dayTable)
            {
                Assert.AreEqual("sh.600000", sym);
                Assert.AreEqual(1, list.Count);
                dayEnumCount++;
            }
            Assert.AreEqual(dayTable.Count, dayEnumCount);
        }

        [TestMethod]
        public void CsvEventTable_TableEnumerableAndLinq_ShouldWork()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = EventSourceFactory.Create(resolver, "ashare.adj_factor.baostock");

            // 1. Dates 属性与 Count 属性
            Assert.IsTrue(table.Count > 0);
            Assert.AreEqual(table.Count, table.Dates.Count());

            // 2. foreach 遍历 Dates 并验证时间单调递增有序
            int dateCount = 0;
            DateTime prevDate = DateTime.MinValue;
            foreach (var date in table.Dates)
            {
                Assert.IsTrue(date.Year >= 2015);
                Assert.IsTrue(date >= prevDate, $"Dates should be sorted chronologically: {date} vs {prevDate}");
                prevDate = date;
                dateCount++;
            }
            Assert.AreEqual(table.Count, dateCount);

            // 3. foreach 直接遍历 Table (KeyValuePair<DateTime, IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>>)
            int tableEnumCount = 0;
            DateTime prevTableDate = DateTime.MinValue;
            foreach (var (date, dayMap) in table)
            {
                Assert.IsTrue(date >= prevTableDate, $"Table enumerator should be ordered chronologically: {date} vs {prevTableDate}");
                prevTableDate = date;
                Assert.IsTrue(dayMap.Count > 0);
                tableEnumCount++;
            }
            Assert.AreEqual(table.Count, tableEnumCount);

            // 4. LINQ 筛选日期与流式投影
            var datesInJuly = table.Dates.Where(d => d.Month == 7).ToList();
            Assert.IsTrue(datesInJuly.Count > 0);

            var julyFactors = datesInJuly.SelectMany(d => table.As<AdjustmentFactor>(d)).ToList();
            Assert.IsTrue(julyFactors.Count > 0);
            Assert.IsTrue(julyFactors.All(f => f.BackAdjFactor > 0));
        }

        [TestMethod]
        public void CsvEventTable_ShouldReturnFalseForMissingDate()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = EventSourceFactory.Create(resolver, "ashare.adj_factor.baostock");

            bool found = table.TryGet(new DateTime(2021, 1, 1), "sh.600000", out _);
            Assert.IsFalse(found, "Should not find adj_factor on non-event date.");
            Assert.AreEqual(0, table.GetRows(new DateTime(2021, 1, 1), "sh.600000").Count);

            bool foundTyped = table.TryGet<AdjustmentFactor>(new DateTime(2021, 1, 1), "sh.600000", out _);
            Assert.IsFalse(foundTyped);
        }

        [TestMethod]
        public void CsvEventTable_MultipleYears_ShouldMerge()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = EventSourceFactory.Create(resolver, "ashare.adj_factor.baostock");

            // 2021 和其他年份的数据都应该被加载
            bool found2021 = table.TryGetFirst(new DateTime(2021, 5, 28), "sh.600008", out var val2021);
            Assert.IsTrue(found2021, "Should load data from year=2021.");
            Assert.AreEqual(6.556426, val2021!.GetDouble("back_adj_factor"), 1e-6);

            var typed2021 = table.Get<AdjustmentFactor>(new DateTime(2021, 5, 28), "sh.600008");
            Assert.AreEqual(6.556426, typed2021.BackAdjFactor, 1e-6);
        }

        [TestMethod]
        public void DynamicEventTable_FullApiCoverage_ShouldWork()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var dynTable = EventSourceFactory.Create(resolver, "ashare.adj_factor.baostock");
            var dt = new DateTime(2021, 7, 21);

            // 1. Table 级别 API 测试
            Assert.AreEqual("ashare.adj_factor.baostock", dynTable.Name);
            Assert.IsTrue(dynTable.ContainsDate(dt));
            Assert.IsFalse(dynTable.ContainsDate(new DateTime(1990, 1, 1)));

            bool foundDay = dynTable.TryGetDay(dt, out var dayTable);
            Assert.IsTrue(foundDay);
            Assert.IsNotNull(dayTable);

            bool foundRows = dynTable.TryGet(dt, "sh.600000", out var list);
            Assert.IsTrue(foundRows);
            Assert.AreEqual(1, list.Count);

            bool foundFirst = dynTable.TryGetFirst(dt, "sh.600000", out var firstRow);
            Assert.IsTrue(foundFirst);
            Assert.IsNotNull(firstRow);
            Assert.AreEqual(10.832619, firstRow!.GetDouble("back_adj_factor"), 1e-6);

            var rowsDirect = dynTable.GetRows(dt, "sh.600000");
            Assert.AreEqual(1, rowsDirect.Count);

            // 2. DayTable 级别 API 测试
            Assert.IsTrue(dayTable!.ContainsKey("sh.600000"));
            Assert.IsFalse(dayTable.ContainsKey("non_existent"));

            Assert.IsTrue(dayTable.TryGetValue("sh.600000", out var dRows));
            Assert.IsNotNull(dRows);
            Assert.AreEqual(1, dRows.Count);

            Assert.IsTrue(dayTable.Keys.Any());

            // 3. 全局广播事件覆盖
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "type", 0 } };
            var globalData = new Dictionary<DateTime, List<DataRow>>
            {
                { dt, new List<DataRow> { new DataRow("", dt, new[] { "holiday" }, colMap) } }
            };
            var emptySymbolData = new Dictionary<DateTime, Dictionary<string, List<DataRow>>>();
            var globalTable = new EventTable("market_news", new[] { "type" }, emptySymbolData, globalData);

            Assert.IsTrue(globalTable.TryGetGlobal(dt, out var gRows));
            Assert.AreEqual(1, gRows.Count);
            Assert.AreEqual("holiday", gRows[0].GetString("type"));
        }

        [TestMethod]
        public void StreamingLinq_Projections_ShouldWork()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = EventSourceFactory.Create(resolver, "ashare.adj_factor.baostock");
            var dt = new DateTime(2021, 7, 21);

            // 1. 当日截面流式投影 As<T>(date)
            var dayStream = table.As<AdjustmentFactor>(dt).ToList();
            Assert.AreEqual(1, dayStream.Count);
            Assert.AreEqual(10.832619, dayStream[0].BackAdjFactor, 1e-6);

            // 2. 单标的历史流式投影 As<T>(symbol)
            var symStream = table.As<AdjustmentFactor>("sh.600000").ToList();
            Assert.IsTrue(symStream.Count > 0);
            Assert.IsTrue(symStream.Any(f => Math.Abs(f.BackAdjFactor - 10.832619) < 1e-6));

            // 3. 全量事件流式投影 As<T>() 与 LINQ
            var allStream = table.As<AdjustmentFactor>().Take(10).ToList();
            Assert.AreEqual(10, allStream.Count);

            var maxFactor = table.As<AdjustmentFactor>().Max(f => f.BackAdjFactor);
            Assert.IsTrue(maxFactor > 0);
        }

        #endregion

        #region 2. Parquet 加载与链式索引测试

        [TestMethod]
        public void ParquetEventTable_ChainedIndexers_ShouldWork()
        {
            string parquetRoot = Path.Combine(GetTestDataRoot(), "parquet");
            var resolver = new StorageResolver(parquetRoot);

            var table = EventSourceFactory.Create(resolver, "ashare.adj_factor.baostock");
            var dt = new DateTime(2021, 7, 21);

            var item = table.Get<AdjustmentFactor>(dt, "sh.600000");
            Assert.IsNotNull(item);
            Assert.AreEqual(10.832619, item.BackAdjFactor, 1e-6);

            var row = table[dt, "sh.600000"];
            Assert.AreEqual(10.832619, row.GetDouble("back_adj_factor"), 1e-6);
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

            var csvTable = EventSourceFactory.Create(csvResolver, "ashare.adj_factor.baostock");
            var parquetTable = EventSourceFactory.Create(parquetResolver, "ashare.adj_factor.baostock");

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
                bool csvFound = csvTable.TryGet(testDates[i], testSymbols[i], out var csvRows);
                bool pqFound = parquetTable.TryGet(testDates[i], testSymbols[i], out var pqRows);

                Assert.IsTrue(csvFound, $"CSV: missing {testSymbols[i]} on {testDates[i]:yyyy-MM-dd}");
                Assert.IsTrue(pqFound, $"Parquet: missing {testSymbols[i]} on {testDates[i]:yyyy-MM-dd}");

                var csvVal = csvTable.Get<AdjustmentFactor>(testDates[i], testSymbols[i]);
                var pqVal = parquetTable.Get<AdjustmentFactor>(testDates[i], testSymbols[i]);

                Assert.AreEqual(csvVal.BackAdjFactor, pqVal.BackAdjFactor, 1e-6,
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
            var emptySymbolData = new Dictionary<DateTime, Dictionary<string, List<DataRow>>>();
            var table = new EventTable("adjustments", new[] { "back_adj_factor" }, emptySymbolData);

            registry.Register("adjustments", table);

            Assert.IsTrue(registry.HasTable("adjustments"));
            var retrieved = registry.GetTable("adjustments");
            Assert.AreSame(table, retrieved);

            var viaIndexer = registry["adjustments"];
            Assert.AreSame(table, viaIndexer);

            var names = registry.GetTableNames();
            Assert.AreEqual(1, names.Count);
            Assert.AreEqual("adjustments", names[0]);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void GetTable_NonExistent_ShouldThrow()
        {
            var registry = new EventRegistry();
            registry.GetTable("non_existent");
        }

        [TestMethod]
        public void HasTable_ShouldReturnCorrectly()
        {
            var registry = new EventRegistry();
            Assert.IsFalse(registry.HasTable("adjustments"));

            var emptySymbolData = new Dictionary<DateTime, Dictionary<string, List<DataRow>>>();
            var table = new EventTable("adjustments", new[] { "back_adj_factor" }, emptySymbolData);
            registry.Register("adjustments", table);

            Assert.IsTrue(registry.HasTable("adjustments"));
            Assert.IsFalse(registry.HasTable("other"));
        }

        [TestMethod]
        public void EventRegistryFactory_BatchAssemble_ShouldWork()
        {
            string csvRoot = Path.Combine(EventTableTests.GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var configs = new[]
            {
                new CarrotBacktesting.NET.Config.EventStreamConfig
                {
                    Name = "adj",
                    TableId = "ashare.adj_factor.baostock"
                }
            };

            var registry = EventRegistryFactory.Create(resolver, configs);
            Assert.IsTrue(registry.HasTable("adj"));
            var table = registry["adj"];
            Assert.IsNotNull(table);
            Assert.IsTrue(table.Count > 0);
        }
    }
}
