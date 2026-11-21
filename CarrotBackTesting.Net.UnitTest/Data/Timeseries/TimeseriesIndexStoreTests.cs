using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class TimeseriesIndexStoreTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CarrotIndexStoreTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        [TestMethod]
        public void TestConstructor_ValidatesParameters()
        {
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesIndexStore("", "t1"));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesIndexStore("   ", "t1"));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesIndexStore(_tempDir, ""));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesIndexStore(_tempDir, "   "));

            var store = new TimeseriesIndexStore(_tempDir, "tbl_idx");
            Assert.AreEqual("tbl_idx", store.TableId);
            Assert.IsTrue(store.GetSymbolsIndexPath().EndsWith("symbols.idx"));
            Assert.IsTrue(store.GetTimeIndexPath().EndsWith("time.idx"));
        }

        [TestMethod]
        public void TestSymbols_WriteReadAndExpand()
        {
            var store = new TimeseriesIndexStore(_tempDir, "tbl_symbols");
            Assert.AreEqual(0, store.ReadSymbols().Count);

            // 1. 初次写入
            var symbols = new[] { "sh.600000", "sh.600001", "sz.000001" };
            store.WriteSymbols(symbols);

            var read = store.ReadSymbols();
            Assert.AreEqual(3, read.Count);
            Assert.AreEqual("sh.600000", read[0]);
            Assert.AreEqual("sz.000001", read[2]);

            // 2. 幂等再次写入同一内容
            store.WriteSymbols(symbols);
            Assert.AreEqual(3, store.ReadSymbols().Count);

            // 3. 子集写入：保留更宽的索引
            store.WriteSymbols(new[] { "sh.600000", "sh.600001" });
            Assert.AreEqual(3, store.ReadSymbols().Count);

            // 4. 前缀扩充写入：成功拓宽
            store.WriteSymbols(new[] { "sh.600000", "sh.600001", "sz.000001", "sz.000002" });
            var readExpanded = store.ReadSymbols();
            Assert.AreEqual(4, readExpanded.Count);
            Assert.AreEqual("sz.000002", readExpanded[3]);

            // 5. 冲突写入：抛出 InvalidOperationException
            Assert.ThrowsException<InvalidOperationException>(() =>
                store.WriteSymbols(new[] { "sh.600000", "sz.999999" }));
        }

        [TestMethod]
        public void TestTradeDates_WriteReadAndAppend()
        {
            var store = new TimeseriesIndexStore(_tempDir, "tbl_dates");
            Assert.AreEqual(0, store.ReadTradeDates().Count);

            var baseTime = new MarketTime(2024, 1, 1);
            var dates = new[]
            {
                baseTime,
                baseTime.AddDays(1),
                baseTime.AddDays(2)
            };

            // 1. 全量写入
            store.WriteTradeDates(dates);
            var read = store.ReadTradeDates();
            Assert.AreEqual(3, read.Count);
            Assert.AreEqual(baseTime, read[0]);
            Assert.AreEqual(baseTime.AddDays(2), read[2]);

            // 2. 幂等重写
            store.WriteTradeDates(dates);
            Assert.AreEqual(3, store.ReadTradeDates().Count);

            // 3. 追加新行
            var newDates = new[]
            {
                baseTime.AddDays(3),
                baseTime.AddDays(4)
            };
            store.AppendTradeDates(newDates, targetRowCount: 5);

            var readAppended = store.ReadTradeDates();
            Assert.AreEqual(5, readAppended.Count);
            Assert.AreEqual(baseTime.AddDays(4), readAppended[4]);

            // 4. 幂等重复追加已存在的行：跳过
            store.AppendTradeDates(newDates, targetRowCount: 5);
            Assert.AreEqual(5, store.ReadTradeDates().Count);

            // 5. 清理整表
            store.ClearTable();
            Assert.IsFalse(Directory.Exists(store.GetTableDir()));
            Assert.AreEqual(0, store.ReadTradeDates().Count);
        }

        private class CustomReadOnlyList<T> : IReadOnlyList<T>
        {
            private readonly T[] _items;
            public CustomReadOnlyList(params T[] items) => _items = items;
            public int Count => _items.Length;
            public T this[int index] => _items[index];
            public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
        }

        [TestMethod]
        public void TestTradeDates_ConflictPrefix_ThrowsInvalidOperationException()
        {
            var store = new TimeseriesIndexStore(_tempDir, "tbl_dates_conflict");
            var baseTime = new MarketTime(2024, 1, 1);
            var initialDates = new[] { baseTime, baseTime.AddDays(1), baseTime.AddDays(2) };
            store.WriteTradeDates(initialDates);

            // 1. 尝试写入更短/同长但是 Ticks 冲突的时间轴
            var conflictingDatesShort = new[] { baseTime, baseTime.AddDays(5) };
            var ex1 = Assert.ThrowsException<InvalidOperationException>(() => store.WriteTradeDates(conflictingDatesShort));
            StringAssert.Contains(ex1.Message, "Time index conflict");

            // 2. 尝试写入更长但是前缀发生冲突的时间轴
            var conflictingDatesLong = new[] { baseTime, baseTime.AddDays(5), baseTime.AddDays(2), baseTime.AddDays(3) };
            var ex2 = Assert.ThrowsException<InvalidOperationException>(() => store.WriteTradeDates(conflictingDatesLong));
            StringAssert.Contains(ex2.Message, "Time index conflict");
        }

        [TestMethod]
        public void TestSymbols_ConflictPrefix_ThrowsInvalidOperationException()
        {
            var store = new TimeseriesIndexStore(_tempDir, "tbl_symbols_conflict");
            var initialSymbols = new[] { "sh.600000", "sz.000001" };
            store.WriteSymbols(initialSymbols);

            // 前缀发生冲突：第 1 项从 sz.000001 变成 sz.000002
            var conflictingSymbols = new[] { "sh.600000", "sz.000002", "sh.600001" };
            var ex = Assert.ThrowsException<InvalidOperationException>(() => store.WriteSymbols(conflictingSymbols));
            StringAssert.Contains(ex.Message, "Symbols conflict");
        }

        [TestMethod]
        public void TestTradeDatesAndSymbols_CustomReadOnlyList_ArrayPoolFallbackPath()
        {
            var store = new TimeseriesIndexStore(_tempDir, "tbl_custom_list");
            var baseTime = new MarketTime(2024, 1, 1);

            // 使用非数组、非 List<T> 的通用只读集合触发 ArrayPool 租借序列化
            var customDates = new CustomReadOnlyList<MarketTime>(baseTime, baseTime.AddDays(1), baseTime.AddDays(2));
            store.WriteTradeDates(customDates);

            var readDates = store.ReadTradeDates();
            Assert.AreEqual(3, readDates.Count);
            Assert.AreEqual(baseTime, readDates[0]);
            Assert.AreEqual(baseTime.AddDays(2), readDates[2]);

            var customSymbols = new CustomReadOnlyList<string>("sh.600000", "sz.000001");
            store.WriteSymbols(customSymbols);

            var readSymbols = store.ReadSymbols();
            Assert.AreEqual(2, readSymbols.Count);
            Assert.AreEqual("sh.600000", readSymbols[0]);
            Assert.AreEqual("sz.000001", readSymbols[1]);
        }
    }
}
