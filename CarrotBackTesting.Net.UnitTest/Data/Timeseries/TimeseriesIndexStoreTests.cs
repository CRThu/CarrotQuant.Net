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
    }
}
