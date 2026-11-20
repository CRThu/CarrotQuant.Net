using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class DynamicMarketMetadataTests
    {
        [TestMethod]
        public void TestDynamicMarketMetadata_AppendDate_And_AppendSymbol()
        {
            var initialSymbols = new[] { "000001.SZ", "600000.SH" };
            var initialDates = new MarketTime[] { new(2025, 1, 1), new(2025, 1, 2) };
            var fieldTypes = new Dictionary<string, Type> { { "close", typeof(double) } };

            var metadata = new DynamicMarketMetadata(initialSymbols, initialDates, fieldTypes);

            Assert.AreEqual(2, metadata.Symbols.Count);
            Assert.AreEqual(2, metadata.TradeDates.Count);
            Assert.AreEqual(0, metadata.GetSymbolIndex("000001.SZ"));
            Assert.AreEqual(1, metadata.GetSymbolIndex("600000.SH"));
            Assert.AreEqual(-1, metadata.GetSymbolIndex("bj.920001"));
            Assert.AreEqual(0, metadata.GetDateIndex(new DateTime(2025, 1, 1)));
            Assert.AreEqual(-1, metadata.GetDateIndex(new DateTime(2025, 1, 3)));

            // 1. 追加日期
            int newDateIdx = metadata.AppendDate(new DateTime(2025, 1, 3));
            Assert.AreEqual(2, newDateIdx);
            Assert.AreEqual(3, metadata.TradeDates.Count);
            Assert.AreEqual(2, metadata.GetDateIndex(new DateTime(2025, 1, 3)));

            // 幂等追加同一日期
            int repeatDateIdx = metadata.AppendDate(new DateTime(2025, 1, 3));
            Assert.AreEqual(2, repeatDateIdx);
            Assert.AreEqual(3, metadata.TradeDates.Count);

            // 2. 追加股票代码（新股上市）
            int newSymIdx = metadata.AppendSymbol("bj.920001");
            Assert.AreEqual(2, newSymIdx);
            Assert.AreEqual(3, metadata.Symbols.Count);
            Assert.AreEqual(2, metadata.GetSymbolIndex("bj.920001"));

            // 幂等追加同一标的
            int repeatSymIdx = metadata.AppendSymbol("bj.920001");
            Assert.AreEqual(2, repeatSymIdx);
            Assert.AreEqual(3, metadata.Symbols.Count);

            // 字段类型
            Assert.AreEqual(typeof(double), metadata.GetFieldType("close"));
            Assert.AreEqual(typeof(double), metadata.GetFieldType("unknown"));

            // 3. 验证零堆分配快照引用恒等性（Zero-Allocation Snapshot）
            var symbols1 = metadata.Symbols;
            var symbols2 = metadata.Symbols;
            Assert.AreSame(symbols1, symbols2, "多次获取 Symbols 应直接返回不可变快照引用，严禁重复 ToArray 分配！");

            var dates1 = metadata.TradeDates;
            var dates2 = metadata.TradeDates;
            Assert.AreSame(dates1, dates2, "多次获取 TradeDates 应直接返回不可变快照引用，严禁重复 ToArray 分配！");
        }

        private sealed class ReadOnlyMetadataMock : IMarketMetadata
        {
            public IReadOnlyList<string> Symbols { get; } = new[] { "000001.SZ" };
            public IReadOnlyList<string> FieldNames { get; } = new[] { "close" };
            public IReadOnlyList<MarketTime> TradeDates { get; } = new MarketTime[] { new MarketTime(new DateTime(2025, 1, 1).Ticks) };
            public Type GetFieldType(string fieldName) => typeof(double);
            public int GetSymbolIndex(string symbol) => symbol == "000001.SZ" ? 0 : -1;
            public int GetDateIndex(MarketTime date) => date.Ticks == new DateTime(2025, 1, 1).Ticks ? 0 : -1;
        }

        [TestMethod]
        public void TestMarketMetadata_ReadOnlyVsMutable_ContractSeparation()
        {
            IMarketMetadata readOnlyMeta = new ReadOnlyMetadataMock();
            Assert.IsFalse(readOnlyMeta is IMutableMarketMetadata, "只读元数据契约严禁混入突变状态机方法");

            var dynamicMeta = new DynamicMarketMetadata(new[] { "000001.SZ" }, new[] { new MarketTime(new DateTime(2025, 1, 1).Ticks) });
            Assert.IsTrue(dynamicMeta is IMutableMarketMetadata, "可变元数据实例必须显式实现 IMutableMarketMetadata 专有写契约");

            var mutable = (IMutableMarketMetadata)dynamicMeta;
            int newIdx = mutable.AppendSymbol("600000.SH");
            Assert.AreEqual(1, newIdx);
        }
    }
}
