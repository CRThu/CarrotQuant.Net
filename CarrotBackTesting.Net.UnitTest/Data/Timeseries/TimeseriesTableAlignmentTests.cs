using System;
using System.Collections.Generic;
using CarrotBacktesting.NET;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBackTesting.Net.UnitTest.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data.Timeseries
{
    [TestClass]
    public class TimeseriesTableAlignmentTests
    {
        [TestMethod]
        public void TimelineRouter_ParseTimeOffset_AllFormats()
        {
            // 标准 TimeSpan 格式
            Assert.AreEqual(new TimeSpan(15, 0, 0), TimeseriesTimelineRouter.ParseTimeOffset("15:00:00"));
            Assert.AreEqual(new TimeSpan(15, 30, 0), TimeseriesTimelineRouter.ParseTimeOffset("15:30"));

            // 简写后缀格式
            Assert.AreEqual(TimeSpan.FromHours(15), TimeseriesTimelineRouter.ParseTimeOffset("15h"));
            Assert.AreEqual(TimeSpan.FromHours(1.5), TimeseriesTimelineRouter.ParseTimeOffset("1.5h"));
            Assert.AreEqual(TimeSpan.FromDays(1), TimeseriesTimelineRouter.ParseTimeOffset("1d"));
            Assert.AreEqual(TimeSpan.FromMinutes(30), TimeseriesTimelineRouter.ParseTimeOffset("30m"));
            Assert.AreEqual(TimeSpan.FromSeconds(45), TimeseriesTimelineRouter.ParseTimeOffset("45s"));

            // 无效输入抛出异常
            Assert.ThrowsException<ArgumentException>(() => TimeseriesTimelineRouter.ParseTimeOffset(""));
            Assert.ThrowsException<ArgumentException>(() => TimeseriesTimelineRouter.ParseTimeOffset("   "));
            Assert.ThrowsException<ArgumentException>(() => TimeseriesTimelineRouter.ParseTimeOffset("invalid_offset"));
        }

        [TestMethod]
        public void TimelineRouter_DailyToDaily_ExactAndHolidayFallback()
        {
            // 主表：01-03 至 01-06 (4 个交易日)
            var primaryDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 3)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 5)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 6)),
            };

            // 子表：缺失 01-04
            var subDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 3)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 5)),
            };

            int[] map = TimeseriesTimelineRouter.BuildAsOfMap(primaryDates, subDates);

            Assert.AreEqual(4, map.Length);
            // 01-03 对应 subDates[0] (01-03)
            Assert.AreEqual(0, map[0]);
            // 01-04 缺失，沿用上一有效数据 01-03 (subDates[0])
            Assert.AreEqual(0, map[1]);
            // 01-05 对应 subDates[1] (01-05)
            Assert.AreEqual(1, map[2]);
            // 01-06 缺失，沿用上一有效数据 01-05 (subDates[1])
            Assert.AreEqual(1, map[3]);
        }

        [TestMethod]
        public void TimelineRouter_MinuteToDaily_NaturalDayRollover()
        {
            // 主表为日内分钟线
            var primaryDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 9, 31, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 14, 59, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 0, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 5, 9, 31, 0)),
            };

            // 子表为纯日期（无时刻）日线
            var subDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 3)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 5)),
            };

            // 缺省时纯日期子表在次日零点闭合生效
            int[] map = TimeseriesTimelineRouter.BuildAsOfMap(primaryDates, subDates);

            // 01-04 日内（未到次日），可见最新日线为 01-03 (Index 0)
            Assert.AreEqual(0, map[0]);
            Assert.AreEqual(0, map[1]);
            Assert.AreEqual(0, map[2]);

            // 01-05 次日可见 01-04 (Index 1)
            Assert.AreEqual(1, map[3]);
        }

        [TestMethod]
        public void TimelineRouter_MinuteToDaily_ExplicitTimeOffset()
        {
            var primaryDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 9, 31, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 14, 59, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 0, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 5, 0)),
            };

            var subDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 3)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4)),
            };

            // 显式指定偏移量 15:00:00 生效
            int[] map = TimeseriesTimelineRouter.BuildAsOfMap(primaryDates, subDates, timeOffset: "15:00:00");

            // 15:00 前看到 01-03 (Index 0)
            Assert.AreEqual(0, map[0]);
            Assert.AreEqual(0, map[1]);

            // 15:00 及之后看到 01-04 (Index 1)
            Assert.AreEqual(1, map[2]);
            Assert.AreEqual(1, map[3]);
        }

        [TestMethod]
        public void TimelineRouter_SubTableWithPreprocessedTimestamp_NoOffsetNeeded()
        {
            var primaryDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 9, 31, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 14, 59, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 0, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 5, 0)),
            };

            // 子表时间戳已包含 15:00:00，无需额外配置 offset
            var subDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 3, 15, 0, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 0, 0)),
            };

            int[] map = TimeseriesTimelineRouter.BuildAsOfMap(primaryDates, subDates, timeOffset: null);

            // 15:00 前看到 01-03 (Index 0)
            Assert.AreEqual(0, map[0]);
            Assert.AreEqual(0, map[1]);

            // 15:00 整点及之后看到 01-04 (Index 1)
            Assert.AreEqual(1, map[2]);
            Assert.AreEqual(1, map[3]);
        }

        [TestMethod]
        public void TimelineRouter_EarlierThanHistory_ReturnsNegativeOne()
        {
            var primaryDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2022, 12, 30)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 3)),
            };

            var subDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 3)),
            };

            int[] map = TimeseriesTimelineRouter.BuildAsOfMap(primaryDates, subDates);

            // 2022-12-30 早于子表首条记录，返回 -1
            Assert.AreEqual(-1, map[0]);
            // 2023-01-03 匹配 Index 0
            Assert.AreEqual(0, map[1]);
        }

        [TestMethod]
        public void TimelineRouter_DailyToMinute_AsOfMapping_ReturnsEndOfDayMinuteBar()
        {
            // 主表：日线收盘时刻 (15:00:00)
            var primaryCloseDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 3, 15, 0, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 0, 0)),
            };

            // 子表：5 分钟线，覆盖 01-03 与 01-04 两天的分钟序列
            var subDates = new List<MarketTime>
            {
                // Day 1 (01-03): Index 0 ~ 2
                MarketTime.FromDateTime(new DateTime(2023, 1, 3, 9, 35, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 3, 14, 55, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 3, 15, 0, 0)),  // Day 1 最后一根 Bar (Index 2)
                
                // Day 2 (01-04): Index 3 ~ 5
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 9, 35, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 14, 55, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 15, 0, 0)),  // Day 2 最后一根 Bar (Index 5)
            };

            // 1. 主表日线推进到 15:00:00 收盘时，应精确映射到当日最后一根 15:00 的分钟 Bar
            int[] map15h = TimeseriesTimelineRouter.BuildAsOfMap(primaryCloseDates, subDates, timeOffset: null);
            Assert.AreEqual(2, map15h[0]); // 01-03 映射到 Index 2 (15:00:00)
            Assert.AreEqual(5, map15h[1]); // 01-04 映射到 Index 5 (15:00:00)

            // 2. 主表若推进到盘中 10:00:00，只能看到当日 9:35 的 Bar，绝不超前透视 15:00 的 Bar
            var primaryIntradayDates = new List<MarketTime>
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 3, 10, 0, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 10, 0, 0)),
            };
            int[] map10h = TimeseriesTimelineRouter.BuildAsOfMap(primaryIntradayDates, subDates, timeOffset: null);
            Assert.AreEqual(0, map10h[0]); // 01-03 盘中 10:00 仅可见 Index 0 (09:35:00)
            Assert.AreEqual(3, map10h[1]); // 01-04 盘中 10:00 仅可见 Index 3 (09:35:00)
        }

        [TestMethod]
        public void TimeseriesTable_MatrixAndSpanRetrieval_Success()
        {
            var symbols = new[] { "sh.600000", "sz.000001" };
            var dates = new[] { MarketTime.FromDateTime(new DateTime(2023, 1, 3)), MarketTime.FromDateTime(new DateTime(2023, 1, 4)) };
            var fieldTypes = new Dictionary<string, Type> { { "close", typeof(double) } };
            var metadata = new DynamicMarketMetadata(symbols, dates, fieldTypes);

            var matrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 2);
            matrix[0, 0] = 10.0;
            matrix[0, 1] = 20.0;
            matrix[1, 0] = 11.0;
            matrix[1, 1] = 21.0;

            var table = new TimeseriesTable(
                name: "daily_quote",
                isPrimary: false,
                metadata: metadata,
                managerFactory: (field, type) =>
                {
                    var mockSource = new MockTimeseriesSource(metadata);
                    return new TimeseriesCacheManager<double>(field, mockSource, mockSource, null, null);
                },
                subRowMap: new[] { -1, 0, 1, 1 });

            Assert.IsTrue(table.HasField("close"));
            Assert.IsFalse(table.HasField("volume"));
            Assert.AreEqual("daily_quote", table.Name);
            Assert.IsFalse(table.IsPrimary);

            Assert.AreEqual(-1, table.GetCurrentRowIndex(0));
            Assert.AreEqual(0, table.GetCurrentRowIndex(1));
            Assert.AreEqual(1, table.GetCurrentRowIndex(2));
            Assert.AreEqual(1, table.GetCurrentRowIndex(3));
            Assert.AreEqual(-1, table.GetCurrentRowIndex(99));
        }

        [TestMethod]
        public void TimeseriesTableContextExtensions_ReadCurrentRowAndValues()
        {
            var symbols = new[] { "sh.600000", "sz.000001" };
            var dates = new[] { MarketTime.FromDateTime(new DateTime(2023, 1, 3)), MarketTime.FromDateTime(new DateTime(2023, 1, 4)) };
            var fieldTypes = new Dictionary<string, Type> { { "close", typeof(double) } };
            var metadata = new DynamicMarketMetadata(symbols, dates, fieldTypes);

            var mockTable = new MockTimeseriesTable(metadata, "sub_daily", isPrimary: false, subRowMap: new[] { 0, 1 });
            var matrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 2);
            matrix[0, 0] = 10.5;
            matrix[0, 1] = 20.5;
            matrix[1, 0] = 11.5;
            matrix[1, 1] = 21.5;
            mockTable.SetMatrix("close", matrix);

            var registry = new MockTimeseriesRegistry(metadata);
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var context = new EngineContext(registry, broker);
            context.UpdateStep(1, dates[1]);

            // 扩展方法调用验证
            Assert.AreEqual(1, mockTable.GetCurrentRowIndex(context));
            var rowSpan = mockTable.GetCurrentRow<double>("close", context);
            Assert.AreEqual(2, rowSpan.Length);
            Assert.AreEqual(11.5, rowSpan[0]);
            Assert.AreEqual(21.5, rowSpan[1]);

            // 通过列索引取值
            Assert.AreEqual(11.5, mockTable.GetCurrentValue<double>("close", 0, context));
            Assert.AreEqual(21.5, mockTable.GetCurrentValue<double>("close", 1, context));

            // 通过代码取值
            Assert.AreEqual(11.5, mockTable.GetCurrentValue<double>("close", "sh.600000", context));
            Assert.AreEqual(21.5, mockTable.GetCurrentValue<double>("close", "sz.000001", context));
        }

        [TestMethod]
        public void TimeseriesRegistry_PrimaryAndSubTable_IndexingAndFederation()
        {
            var symbols = new[] { "sh.600000" };
            var dates = new[] { MarketTime.FromDateTime(new DateTime(2023, 1, 3)) };
            var fieldTypes = new Dictionary<string, Type> { { "close", typeof(double) } };
            var metadata = new DynamicMarketMetadata(symbols, dates, fieldTypes);

            var primaryTable = new TimeseriesTable(
                "kline_1m",
                isPrimary: true,
                metadata,
                (f, t) =>
                {
                    var src = new MockTimeseriesSource(metadata);
                    return new TimeseriesCacheManager<double>(f, src, src, null, null);
                });

            var subTable = new TimeseriesTable(
                "kline_daily",
                isPrimary: false,
                metadata,
                (f, t) =>
                {
                    var src = new MockTimeseriesSource(metadata);
                    return new TimeseriesCacheManager<double>(f, src, src, null, null);
                },
                subRowMap: new[] { 0 });

            using var registry = new TimeseriesRegistry(primaryTable, new[] { subTable });

            Assert.AreSame(primaryTable, registry.Primary);
            Assert.AreEqual("kline_1m", registry.Primary.Name);
            Assert.IsTrue(registry.HasTable("kline_1m"));
            Assert.IsTrue(registry.HasTable("kline_daily"));
            Assert.IsFalse(registry.HasTable("non_existent"));

            Assert.AreSame(subTable, registry["kline_daily"]);
            Assert.AreSame(subTable, registry.GetTable("kline_daily"));

            var tableNames = registry.GetTableNames();
            CollectionAssert.Contains((System.Collections.ICollection)tableNames, "kline_1m");
            CollectionAssert.Contains((System.Collections.ICollection)tableNames, "kline_daily");
        }

        [TestMethod]
        public void TimeseriesRegistry_PureRouterContract_AccessViaPrimaryAndIndexer()
        {
            var symbols = new[] { "sh.600000" };
            var dates = new[] { MarketTime.FromDateTime(new DateTime(2023, 1, 3)) };
            var fieldTypes = new Dictionary<string, Type> { { "close", typeof(double) } };
            var metadata = new DynamicMarketMetadata(symbols, dates, fieldTypes);

            var primaryTable = new MockTimeseriesTable(metadata, "kline_1m", isPrimary: true);
            var primaryMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 1);
            primaryMatrix[0, 0] = 100.0;
            primaryTable.SetMatrix("close", primaryMatrix);

            var subTable = new MockTimeseriesTable(metadata, "kline_1d", isPrimary: false);
            var subMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 1);
            subMatrix[0, 0] = 200.0;
            subTable.SetMatrix("close", subMatrix);

            ITimeseriesRegistry registry = new TimeseriesRegistry(primaryTable, new[] { subTable });

            // 1. 验证主表矩阵与元数据必须通过 Primary 显式访问
            Assert.AreSame(primaryTable, registry.Primary);
            Assert.IsTrue(registry.Primary.HasField("close"));
            Assert.AreEqual(100.0, registry.Primary.GetMatrix<double>("close")[0, 0]);
            Assert.AreEqual("kline_1m", registry.Primary.Metadata.FieldNames[0] == "close" ? registry.Primary.Name : "");

            // 2. 验证子表矩阵通过索引器访问
            Assert.IsTrue(registry.HasTable("kline_1d"));
            Assert.AreEqual(200.0, registry["kline_1d"].GetMatrix<double>("close")[0, 0]);

            // 3. 验证未注册表抛出 KeyNotFoundException
            Assert.IsFalse(registry.HasTable("kline_1h"));
            Assert.ThrowsException<KeyNotFoundException>(() => registry["kline_1h"]);
        }

        [TestMethod]
        public void TimelineRouter_SignedTimeOffset_ParsesAccurately_ForTimezoneOrSettlementAlignment()
        {
            // 验证 time_offset 作为有符号数学平移量，原生支持负数（用于跨时区或期末结算时间戳校准）
            Assert.AreEqual(TimeSpan.FromHours(-8), TimeseriesTimelineRouter.ParseTimeOffset("-8h"));
            Assert.AreEqual(TimeSpan.FromHours(-9), TimeseriesTimelineRouter.ParseTimeOffset("-9h"));
            Assert.AreEqual(TimeSpan.FromHours(-1), TimeseriesTimelineRouter.ParseTimeOffset("-01:00:00"));
            Assert.AreEqual(TimeSpan.FromMinutes(-30), TimeseriesTimelineRouter.ParseTimeOffset("-30m"));
            Assert.AreEqual(TimeSpan.FromDays(-1), TimeseriesTimelineRouter.ParseTimeOffset("-1d"));

            // 验证业务场景：若数据源标记为次日零点 (2023-01-04 00:00:00)，通过 -9h 偏移校准为当日 15:00:00 闭合
            var primaryDates = new[]
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 3, 14, 59, 0)),
                MarketTime.FromDateTime(new DateTime(2023, 1, 3, 15, 0, 0))
            };
            var subDatesWithNextDayMidnight = new[]
            {
                MarketTime.FromDateTime(new DateTime(2023, 1, 4, 0, 0, 0)) // 带有时刻 00:00:00 的次日标记
            };

            // 传入 -9h 偏移：2023-01-04 00:00:00 - 9h = 2023-01-03 15:00:00 闭合
            var map = TimeseriesTimelineRouter.BuildAsOfMap(primaryDates, subDatesWithNextDayMidnight, "-9h");
            Assert.AreEqual(-1, map[0], "14:59 时尚未达到 15:00 闭合点，不可见");
            Assert.AreEqual(0, map[1], "15:00 恰好达到闭合点，可见索引 0");
        }

        [TestMethod]
        public void TimeseriesTable_MatrixCache_ReturnsSameInstance_And_HasField_IsAccurate()
        {
            var dates = new[] { MarketTime.FromDateTime(new DateTime(2023, 1, 1)) };
            var symbols = new[] { "sh.600000" };
            var fieldTypes = new Dictionary<string, Type> { { "close", typeof(double) } };
            var metadata = new DynamicMarketMetadata(symbols, dates, fieldTypes);

            using var source = new MockTimeseriesSource(metadata);
            using var table = TimeseriesTableFactory.Create("test", true, source);

            Assert.IsTrue(table.HasField("close"));
            Assert.IsFalse(table.HasField("open"));

            var matrix1 = table.GetMatrix<double>("close");
            var matrix2 = table.GetMatrix<double>("close");

            Assert.AreSame(matrix1, matrix2, "GetMatrix 必须返回缓存的只读矩阵实例，避免重复创建与重复校验。");
        }

        private sealed class MockTimeseriesSource : ITimeseriesSource
        {
            private readonly IMarketMetadata _meta;
            public MockTimeseriesSource(IMarketMetadata meta) => _meta = meta;
            public IReadOnlyList<string> Symbols => _meta.Symbols;
            public IReadOnlyList<MarketTime> TradeDates => _meta.TradeDates;
            public IReadOnlyList<string> FieldNames => _meta.FieldNames;
            public int GetSymbolIndex(string symbol) => _meta.GetSymbolIndex(symbol);
            public int GetDateIndex(MarketTime date) => _meta.GetDateIndex(date);
            public Type GetFieldType(string fieldName) => _meta.GetFieldType(fieldName);
            public void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged
            {
                destination.Fill(default);
            }
            public void Dispose() { }
        }
    }
}
