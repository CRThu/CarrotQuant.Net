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
    /// 东财行业实体模型。
    /// </summary>
    public record EastmoneyIndustry(string BoardName, string BoardCode, string StockName);

    [TestClass]
    public class FieldRegistryTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        #region 1. 免定义动态维表与倒排选股测试

        [TestMethod]
        public void TestDynamicFieldTable_ChainedIndexers_AndInvertedSelection()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney");

            // 1. 链式索引器: table[symbol][col]
            string boardName = table["sh.601958"]["board_name"];
            string stockName = table["sh.601958"]["stock_name"];
            Assert.AreEqual("钼", boardName);
            Assert.AreEqual("金钼股份", stockName);

            // 2. 行对象多类型安全读取
            IDataRow row = table["sh.601958"];
            Assert.AreEqual("BK1623", row["board_code"]);
            Assert.AreEqual("钼", row.GetString("board_name"));

            // 3. 动态倒排选股: table.GetKeys(value, col) - 指定列反查
            var poolWithCol = table.GetKeys("钼", "board_name");
            Assert.IsTrue(poolWithCol.Count >= 2);
            CollectionAssert.Contains((System.Collections.ICollection)poolWithCol, "sh.601958");
            CollectionAssert.Contains((System.Collections.ICollection)poolWithCol, "sz.001257");

            // 4. 动态倒排选股: table.GetKeys(value) - 全表全列无列名秒级反查
            var poolNoCol = table.GetKeys("钼");
            Assert.IsTrue(poolNoCol.Count >= 2);
            CollectionAssert.Contains((System.Collections.ICollection)poolNoCol, "sh.601958");
            CollectionAssert.Contains((System.Collections.ICollection)poolNoCol, "sz.001257");
            CollectionAssert.AreEquivalent((System.Collections.ICollection)poolWithCol, (System.Collections.ICollection)poolNoCol);

            // 5. 倒排反查防御: 空串或未命中返回空列表
            Assert.AreEqual(0, table.GetKeys("").Count);
            Assert.AreEqual(0, table.GetKeys("不存在的行业").Count);
            Assert.AreEqual(0, table.GetKeys("钼", "不存在的列").Count);

            // 6. 严格遵循原数据，不擅自清洗/改写
            var poolWithWhitespace = table.GetKeys(" 钼 ", "board_name");
            Assert.AreEqual(0, poolWithWhitespace.Count, "Should strictly match raw data without implicit trim.");

            // 7. 防篡改只读隔离
            Assert.IsFalse(poolWithCol is List<string>, "Inverted index return should be immutable/readonly collection.");
        }

        [TestMethod]
        public void TestDynamicFieldTable_MultiRowsConcept_AndGetValues()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = FieldSourceFactory.Create(resolver, "ashare.concept.eastmoney");

            // 1. 多概念行查询
            bool found = table.TryGetRows("sh.600000", out var rows);
            Assert.IsTrue(found);
            Assert.IsTrue(rows.Count > 0);

            var rowsDirect = table.GetRows("sh.600000");
            Assert.AreEqual(rows.Count, rowsDirect.Count);

            // 2. 1:N 便捷多值提取 GetValues(symbol, col)
            var boardNames = table.GetValues("sh.600000", "board_name");
            Assert.IsTrue(boardNames.Count > 0);
            Assert.AreEqual(rows.Count, boardNames.Count);

            // 3. 倒排选股池（无列名）
            var techStocks = table.GetKeys("科技风格");
            Assert.IsTrue(techStocks.Count > 0);

            // 4. 便捷提取防御
            Assert.AreEqual(0, table.GetValues("non_existent", "board_name").Count);
            Assert.AreEqual(0, table.GetValues("sh.600000", "non_existent_col").Count);
        }

        [TestMethod]
        public void TestDynamicFieldTable_EnumerableAndLinq()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney");

            // 1. Keys 属性与 Count 属性
            Assert.IsTrue(table.Count > 0);
            Assert.AreEqual(table.Count, table.Keys.Count());

            // 2. foreach 遍历 Keys
            int countFromKeys = 0;
            foreach (var key in table.Keys)
            {
                Assert.IsFalse(string.IsNullOrEmpty(key));
                countFromKeys++;
            }
            Assert.AreEqual(table.Count, countFromKeys);

            // 3. foreach 直接遍历 table (KeyValuePair)
            int countFromEnum = 0;
            foreach (var (symbol, symbolRows) in table)
            {
                Assert.IsFalse(string.IsNullOrEmpty(symbol));
                Assert.IsTrue(symbolRows.Count > 0);
                countFromEnum++;
            }
            Assert.AreEqual(table.Count, countFromEnum);

            // 4. LINQ 操作
            var shStocks = table.Keys.Where(k => k.StartsWith("sh.", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.IsTrue(shStocks.Count > 0);
        }

        #endregion

        #region 2. 强类型即时点查与流式 LINQ 测试 (单一内核赋能)

        [TestMethod]
        public void TestTypedFieldTable_InstantProjection_AndStreamingLinq()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var table = FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney");

            // 1. 强类型即时点查: table.Get<T>(symbol)
            EastmoneyIndustry ind = table.Get<EastmoneyIndustry>("sh.601958");
            Assert.AreEqual("钼", ind.BoardName);
            Assert.AreEqual("金钼股份", ind.StockName);
            Assert.AreEqual("BK1623", ind.BoardCode);

            // 2. 强类型安全尝试: table.TryGet<T>(symbol)
            bool found = table.TryGet<EastmoneyIndustry>("sh.601958", out var item);
            Assert.IsTrue(found);
            Assert.AreEqual("钼", item!.BoardName);

            bool notFound = table.TryGet<EastmoneyIndustry>("non_existent", out var nullItem);
            Assert.IsFalse(notFound);
            Assert.IsNull(nullItem);

            // 3. 强类型谓词倒排选股: table.GetKeys<T>(predicate)
            var pool = table.GetKeys<EastmoneyIndustry>(x => x.BoardName == "钼");
            Assert.IsTrue(pool.Count >= 2);
            CollectionAssert.Contains((System.Collections.ICollection)pool, "sh.601958");
            CollectionAssert.Contains((System.Collections.ICollection)pool, "sz.001257");

            // 4. 强类型流式 LINQ: table.As<T>()
            var molyItems = table.As<EastmoneyIndustry>()
                .Where(x => x.BoardName == "钼")
                .Select(x => x.StockName)
                .ToList();
            Assert.IsTrue(molyItems.Count >= 2);
            CollectionAssert.Contains(molyItems, "金钼股份");

            // 5. 强类型带代码键值对流式 LINQ: table.AsPairs<T>()
            var molyPairs = table.AsPairs<EastmoneyIndustry>()
                .Where(kv => kv.Value.BoardName == "钼")
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            Assert.IsTrue(molyPairs.ContainsKey("sh.601958"));
            Assert.AreEqual("金钼股份", molyPairs["sh.601958"].StockName);
        }

        [TestMethod]
        public void TestTypedFieldTable_ParquetLoading_ShouldMatchCsv()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            string parquetRoot = Path.Combine(GetTestDataRoot(), "parquet");

            var csvResolver = new StorageResolver(csvRoot);
            var parquetResolver = new StorageResolver(parquetRoot);

            var csvTable = FieldSourceFactory.Create(csvResolver, "ashare.industry.eastmoney");
            var parquetTable = FieldSourceFactory.Create(parquetResolver, "ashare.industry.eastmoney");

            var csvInd = csvTable.Get<EastmoneyIndustry>("sh.601958");
            var pqInd = parquetTable.Get<EastmoneyIndustry>("sh.601958");

            Assert.AreEqual(csvInd.BoardName, pqInd.BoardName);
            Assert.AreEqual(csvInd.BoardCode, pqInd.BoardCode);
            Assert.AreEqual(csvInd.StockName, pqInd.StockName);

            var csvPool = csvTable.GetKeys<EastmoneyIndustry>(x => x.BoardName == "钼");
            var pqPool = parquetTable.GetKeys<EastmoneyIndustry>(x => x.BoardName == "钼");
            Assert.AreEqual(csvPool.Count, pqPool.Count);
            CollectionAssert.AreEquivalent((System.Collections.ICollection)csvPool, (System.Collections.ICollection)pqPool);
        }

        #endregion

        #region 3. FieldRegistry 与装配工厂测试

        [TestMethod]
        public void TestFieldRegistry_RegistrationAndProjection()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var registry = new FieldRegistry();
            var table = FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney");

            registry.Register("industry", table);

            // 索引器直接访问动态表
            Assert.AreEqual("钼", registry["industry"]["sh.601958"]["board_name"]);

            // 强类型即时投影
            Assert.AreEqual("钼", registry["industry"].Get<EastmoneyIndustry>("sh.601958").BoardName);

            Assert.IsTrue(registry.HasTable("industry"));
            Assert.IsFalse(registry.HasTable("non_existent"));
        }

        [TestMethod]
        public void TestFieldSourceFactory_MissingKeyColumn_ShouldThrowException()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            Assert.ThrowsException<KeyNotFoundException>(() =>
            {
                FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney", keyColumn: "non_existent_key");
            });
        }

        [TestMethod]
        public void TestFieldRegistryFactory_CreateFromConfigs()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);

            var configs = new List<CarrotBacktesting.NET.Config.FieldTableConfig>
            {
                new() { Name = "industry", TableId = "ashare.industry.eastmoney", KeyColumn = "symbol" },
                new() { Name = "concept", TableId = "ashare.concept.eastmoney", KeyColumn = "symbol" }
            };

            var registry = FieldRegistryFactory.Create(resolver, configs);

            Assert.IsNotNull(registry);
            Assert.IsTrue(registry.HasTable("industry"));
            Assert.IsTrue(registry.HasTable("concept"));
            Assert.AreEqual("钼", registry["industry"]["sh.601958"]["board_name"]);
            Assert.IsTrue(registry["concept"].ContainsKey("sh.600000"));

            var emptyRegistry = FieldRegistryFactory.Create(resolver, null);
            Assert.IsNotNull(emptyRegistry);
            Assert.AreEqual(0, emptyRegistry.GetTableNames().Count);
        }

        [TestMethod]
        public void TestFieldTable_MissingKey_ThrowsKeyNotFoundException()
        {
            string csvRoot = Path.Combine(GetTestDataRoot(), "csv");
            var resolver = new StorageResolver(csvRoot);
            var table = FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney");
            var registry = new FieldRegistry();
            registry.Register("industry", table);

            // 1. 点查不存在的标的抛出 KeyNotFoundException
            Assert.ThrowsException<KeyNotFoundException>(() =>
            {
                var _ = table["non_existent_symbol"];
            });

            // 2. 强类型点查不存在的标的抛出 KeyNotFoundException
            Assert.ThrowsException<KeyNotFoundException>(() =>
            {
                table.Get<EastmoneyIndustry>("non_existent_symbol");
            });

            // 3. 注册中心点查不存在的维表抛出 KeyNotFoundException
            Assert.ThrowsException<KeyNotFoundException>(() =>
            {
                var _ = registry["non_existent_table"];
            });
        }

        #endregion
    }
}
