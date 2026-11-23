using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data.Timeseries
{
    [TestClass]
    public class TimeseriesTableTests
    {
        private sealed class DummySource : ITimeseriesSource
        {
            public bool IsDisposed { get; private set; }
            public IReadOnlyList<string> Symbols { get; set; } = new[] { "sh.600000", "sz.000001" };
            public IReadOnlyList<string> FieldNames { get; set; } = new[] { "close", "volume" };
            public IReadOnlyList<MarketTime> TradeDates { get; set; } = new MarketTime[]
            {
                new(2025, 1, 2),
                new(2025, 1, 3),
                new(2025, 1, 6)
            };

            public Type GetFieldType(string fieldName) => fieldName.Equals("volume", StringComparison.OrdinalIgnoreCase) ? typeof(long) : typeof(double);
            public int GetSymbolIndex(string symbol) => ((List<string>)Symbols).IndexOf(symbol);
            public int GetDateIndex(MarketTime date) => ((List<MarketTime>)TradeDates).IndexOf(date);

            public void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged
            {
                if (typeof(T) == typeof(double))
                {
                    var dest = System.Runtime.InteropServices.MemoryMarshal.Cast<T, double>(destination);
                    for (int i = 0; i < length; i++)
                    {
                        dest[i] = (startIndex + i + 1) * 10.0;
                    }
                }
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class ImmutableMetadata : IMarketMetadata
        {
            public IReadOnlyList<string> Symbols => new[] { "sh.600000" };
            public IReadOnlyList<string> FieldNames => new[] { "close" };
            public IReadOnlyList<MarketTime> TradeDates => new MarketTime[] { new(2025, 1, 2) };
            public Type GetFieldType(string fieldName) => typeof(double);
            public int GetSymbolIndex(string symbol) => symbol == "sh.600000" ? 0 : -1;
            public int GetDateIndex(MarketTime date) => 0;
        }

        [TestMethod]
        public void TimeseriesTable_BasicPropertiesAndHasField()
        {
            var source = new DummySource();
            using var table = TimeseriesTableFactory.Create("test_table", isPrimary: true, source: source);

            Assert.AreEqual("test_table", table.Name);
            Assert.IsTrue(table.IsPrimary);
            Assert.IsNotNull(table.Metadata);
            Assert.IsTrue(table.HasField("close"));
            Assert.IsTrue(table.HasField("CLOSE")); // 大小写不敏感
            Assert.IsTrue(table.HasField("volume"));
            Assert.IsFalse(table.HasField("non_existent"));
            Assert.IsFalse(table.HasField(""));
            Assert.IsFalse(table.HasField(null!));
        }

        [TestMethod]
        public void TimeseriesTable_GetMatrix_TypeMismatch_And_NotFound_Throws()
        {
            var source = new DummySource();
            using var table = TimeseriesTableFactory.Create("test_table", isPrimary: true, source: source);

            // 字段不存在
            Assert.ThrowsException<KeyNotFoundException>(() => table.GetMatrix<double>("invalid_field"));

            // 类型不匹配（volume 声明为 long，请求 double）
            Assert.ThrowsException<InvalidOperationException>(() => table.GetMatrix<double>("volume"));
        }

        [TestMethod]
        public void TimeseriesTable_GetCurrentRow_Valid_And_OutOfBounds()
        {
            var source = new DummySource();
            using var table = TimeseriesTableFactory.Create("test_table", isPrimary: true, source: source);

            // 正常步进读取
            var row0 = table.GetCurrentRow<double>("close", 0);
            Assert.AreEqual(2, row0.Length);
            Assert.AreEqual(10.0, row0[0]);

            // 负数索引防御（返回空 Span）
            var rowNegative = table.GetCurrentRow<double>("close", -1);
            Assert.IsTrue(rowNegative.IsEmpty);

            // 越界索引防御（超过 RowCount 返回空 Span）
            var rowOutOfBounds = table.GetCurrentRow<double>("close", 999);
            Assert.IsTrue(rowOutOfBounds.IsEmpty);
        }

        [TestMethod]
        public void TimeseriesTable_GetCurrentValue_Valid_And_OutOfBounds_ReturnsNaN()
        {
            var source = new DummySource();
            using var table = TimeseriesTableFactory.Create("test_table", isPrimary: true, source: source);

            // 正常取值
            double val0 = table.GetCurrentValue<double>("close", 0, 0);
            Assert.AreEqual(10.0, val0, 1e-4);

            // 负数行索引 -> NaN
            Assert.IsTrue(double.IsNaN(table.GetCurrentValue<double>("close", 0, -1)));

            // 负数列索引 -> NaN
            Assert.IsTrue(double.IsNaN(table.GetCurrentValue<double>("close", -1, 0)));

            // 越界行号 -> NaN
            Assert.IsTrue(double.IsNaN(table.GetCurrentValue<double>("close", 0, 100)));

            // 越界列号 -> NaN
            Assert.IsTrue(double.IsNaN(table.GetCurrentValue<double>("close", 100, 0)));
        }

        [TestMethod]
        public void TimeseriesTable_Dispose_CascadesAndGuards()
        {
            var source = new DummySource();
            var table = TimeseriesTableFactory.Create("test_table", isPrimary: true, source: source, ownsSource: true);

            _ = table.GetMatrix<double>("close");
            Assert.IsFalse(source.IsDisposed);

            table.Dispose();
            Assert.IsTrue(source.IsDisposed, "ownsSource: true 时 Dispose 必须级联释放底层数据源。");

            // Dispose 后访问抛出 ObjectDisposedException
            Assert.ThrowsException<ObjectDisposedException>(() => table.GetMatrix<double>("close"));
            Assert.ThrowsException<ObjectDisposedException>(() => table.AppendCol("sh.600001"));
        }

        [TestMethod]
        public void TimeseriesTable_AppendCol_DynamicallyExpandsMetadata()
        {
            var source = new DummySource();
            using var table = TimeseriesTableFactory.Create("test_table", isPrimary: true, source: source);

            Assert.AreEqual(2, table.Metadata.Symbols.Count);
            table.AppendCol("bj.920001");
            Assert.AreEqual(3, table.Metadata.Symbols.Count);
            Assert.AreEqual("bj.920001", table.Metadata.Symbols[2]);
        }

        [TestMethod]
        public void TimeseriesTableFactory_CreateManagerFactory_RespectsCacheOptions()
        {
            var source = new DummySource();

            // 1. 禁用缓存
            var noCacheOpt = new TimeseriesCacheOptions { EnableCache = false };
            var factoryNoCache = TimeseriesTableFactory.CreateManagerFactory(source, null, "primary", noCacheOpt);
            using var mgrNoCache = factoryNoCache("close", typeof(double));
            Assert.IsNotNull(mgrNoCache);
            Assert.AreEqual("close", mgrNoCache.FieldName);

            // 2. 启用缓存但无 storageRoot 时安全降级为纯内存
            var memoryOnlyOpt = new TimeseriesCacheOptions { EnableCache = true, StorageRoot = "" };
            var factoryMemory = TimeseriesTableFactory.CreateManagerFactory(source, null, "primary", memoryOnlyOpt);
            using var mgrMemory = factoryMemory("close", typeof(double));
            Assert.IsNotNull(mgrMemory);
        }

        [TestMethod]
        public void TimeseriesCacheOptions_ToCacheOptions_Derivation()
        {
            var config = new TimeseriesConfig
            {
                TableId = "ashare.kline.1d",
                PageSize = 2048,
                Cache = true,
                Refresh = CacheRefreshMode.Force,
                MaxPages = 8,
                Prefetch = true
            };

            // 回测模式：Prefetch 保持 true
            var backtestOpt = config.ToCacheOptions("D:/test_root", isLive: false);
            Assert.AreEqual("D:/test_root", backtestOpt.StorageRoot);
            Assert.IsTrue(backtestOpt.EnableCache);
            Assert.AreEqual(2048, backtestOpt.PageSize);
            Assert.AreEqual(CacheRefreshMode.Force, backtestOpt.RefreshMode);
            Assert.AreEqual(8, backtestOpt.MaxPages);
            Assert.IsTrue(backtestOpt.Prefetch);
            Assert.IsFalse(backtestOpt.IsLive);

            // 实盘模式：IsLive 为 true，驱动 CacheManager 时强制一票否决预读
            var liveOpt = config.ToCacheOptions("D:/test_root", isLive: true);
            Assert.IsTrue(liveOpt.IsLive);

            var source = new DummySource();
            using var liveManager = new TimeseriesCacheManager<double>("close", source, source, options: liveOpt);
            Assert.IsFalse(liveManager.CanPrefetch, "实盘模式下一票否决必须关闭异步预读。");
        }
    }
}
