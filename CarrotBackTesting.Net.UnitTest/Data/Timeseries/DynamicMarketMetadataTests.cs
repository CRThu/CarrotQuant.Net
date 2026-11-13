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
            var initialDates = new[] { new DateTime(2025, 1, 1), new DateTime(2025, 1, 2) };
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
        }

        private sealed class ReadOnlyMetadataMock : IMarketMetadata
        {
            public IReadOnlyList<string> Symbols { get; } = new[] { "000001.SZ" };
            public IReadOnlyList<string> FieldNames { get; } = new[] { "close" };
            public IReadOnlyList<DateTime> TradeDates { get; } = new[] { new DateTime(2025, 1, 1) };
            public Type GetFieldType(string fieldName) => typeof(double);
            public int GetSymbolIndex(string symbol) => symbol == "000001.SZ" ? 0 : -1;
            public int GetDateIndex(DateTime date) => date == new DateTime(2025, 1, 1) ? 0 : -1;
        }

        [TestMethod]
        public void TestDefaultIMarketMetadata_AppendMethods_ThrowNotSupported()
        {
            IMarketMetadata readOnlyMeta = new ReadOnlyMetadataMock();

            Assert.ThrowsException<NotSupportedException>(() => readOnlyMeta.AppendDate(new DateTime(2025, 1, 2)));
            Assert.ThrowsException<NotSupportedException>(() => readOnlyMeta.AppendSymbol("600000.SH"));
        }
    }
}
