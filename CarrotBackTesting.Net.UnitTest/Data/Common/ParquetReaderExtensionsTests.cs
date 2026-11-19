using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Parquet.Serialization;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class ParquetReaderExtensionsTests
    {
        private static string GetTestParquetFile()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "test_data_root",
                "parquet",
                "ashare.kline.1d.raw.baostock",
                "year=2024",
                "data.parquet"
            );
        }

        [TestMethod]
        public void OpenParquetReaderSync_And_DisposeSync_ShouldManageLifecycle()
        {
            string filePath = GetTestParquetFile();
            using var fs = File.OpenRead(filePath);

            // 验证安全同步包装器
            var reader = ParquetReaderExtensions.OpenParquetReaderSync(fs);
            Assert.IsNotNull(reader);
            Assert.IsNotNull(reader.Schema);
            Assert.IsTrue(reader.RowGroupCount > 0);

            // 验证安全同步释放器
            reader.DisposeSync();
        }

        public class AllTypesSample
        {
            public int IntVal { get; set; }
            public long LongVal { get; set; }
            public float FloatVal { get; set; }
            public double DoubleVal { get; set; }
            public bool BoolVal { get; set; }
            public DateTime DtVal { get; set; }
            public string StrVal { get; set; } = "";
        }

        [TestMethod]
        public async Task ReadStringColumn_AllTypes_ShouldConvertAccurately()
        {
            var data = new List<AllTypesSample>
            {
                new()
                {
                    IntVal = 100,
                    LongVal = 9999999999L,
                    FloatVal = 3.14f,
                    DoubleVal = 2.71828,
                    BoolVal = true,
                    DtVal = new DateTime(2025, 1, 2, 9, 30, 0),
                    StrVal = "SampleText"
                },
                new()
                {
                    IntVal = -5,
                    LongVal = -100L,
                    FloatVal = -1.5f,
                    DoubleVal = -0.5,
                    BoolVal = false,
                    DtVal = new DateTime(2025, 5, 20, 15, 0, 0),
                    StrVal = ""
                }
            };

            using var ms = new MemoryStream();
            await ParquetSerializer.SerializeAsync(data, ms);
            ms.Position = 0;

            var reader = ParquetReaderExtensions.OpenParquetReaderSync(ms);
            try
            {
                using var groupReader = reader.OpenRowGroupReader(0);
                int rowCount = (int)groupReader.RowCount;
                Assert.AreEqual(2, rowCount);

                var schema = reader.Schema;

                // 1. Int
                var intField = schema.FindDataFieldIgnoreCase("IntVal")!;
                var intStrs = groupReader.ReadStringColumn(intField, rowCount);
                Assert.AreEqual("100", intStrs[0]);
                Assert.AreEqual("-5", intStrs[1]);

                // 2. Long
                var longField = schema.FindDataFieldIgnoreCase("LongVal")!;
                var longStrs = groupReader.ReadStringColumn(longField, rowCount);
                Assert.AreEqual("9999999999", longStrs[0]);
                Assert.AreEqual("-100", longStrs[1]);

                // 3. Float
                var floatField = schema.FindDataFieldIgnoreCase("FloatVal")!;
                var floatStrs = groupReader.ReadStringColumn(floatField, rowCount);
                Assert.IsTrue(float.TryParse(floatStrs[0], System.Globalization.CultureInfo.InvariantCulture, out float f0));
                Assert.AreEqual(3.14f, f0, 1e-4f);

                // 4. Bool
                var boolField = schema.FindDataFieldIgnoreCase("BoolVal")!;
                var boolStrs = groupReader.ReadStringColumn(boolField, rowCount);
                Assert.AreEqual("true", boolStrs[0]);
                Assert.AreEqual("false", boolStrs[1]);

                // 5. DateTime
                var dtField = schema.FindDataFieldIgnoreCase("DtVal")!;
                var dtStrs = groupReader.ReadStringColumn(dtField, rowCount);
                Assert.AreEqual("2025-01-02 09:30:00", dtStrs[0]);

                // 6. String (ReadOnlyMemory<char>)
                var strField = schema.FindDataFieldIgnoreCase("StrVal")!;
                var strStrs = groupReader.ReadStringColumn(strField, rowCount);
                Assert.AreEqual("SampleText", strStrs[0]);
            }
            finally
            {
                reader.DisposeSync();
            }
        }

        public class DateColumnSample
        {
            public DateTime DtCol { get; set; }
            public long UnixMillisCol { get; set; }
            public string StrDateCol { get; set; } = "";
        }

        [TestMethod]
        public async Task ReadTimeColumn_AllFormats_ShouldPreserveFullPrecision()
        {
            var testDate = new DateTime(2025, 6, 18, 14, 30, 45);
            long unixMillis = new DateTimeOffset(2025, 6, 18, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

            var data = new List<DateColumnSample>
            {
                new()
                {
                    DtCol = testDate,
                    UnixMillisCol = unixMillis,
                    StrDateCol = "2025-06-18 10:00:00"
                },
                new()
                {
                    DtCol = testDate.AddDays(1),
                    UnixMillisCol = unixMillis + 86400000L,
                    StrDateCol = "invalid-date-string" // 测试解析失败回退 MarketTime.MinValue
                }
            };

            using var ms = new MemoryStream();
            await ParquetSerializer.SerializeAsync(data, ms);
            ms.Position = 0;

            var reader = ParquetReaderExtensions.OpenParquetReaderSync(ms);
            try
            {
                using var groupReader = reader.OpenRowGroupReader(0);
                int rowCount = (int)groupReader.RowCount;
                Assert.AreEqual(2, rowCount);

                var schema = reader.Schema;

                // 1. DateTime 保留完整精度
                var dtField = schema.FindDataFieldIgnoreCase("DtCol")!;
                var dates1 = groupReader.ReadTimeColumn(dtField, rowCount);
                Assert.AreEqual((MarketTime)testDate, dates1[0]);

                // 2. Unix Milliseconds (long)
                var tsField = schema.FindDataFieldIgnoreCase("UnixMillisCol")!;
                var dates2 = groupReader.ReadTimeColumn(tsField, rowCount);
                Assert.AreEqual(new MarketTime(2025, 6, 18, 0, 0, 0), dates2[0]);

                // 3. String Date 保留完整精度
                var strDateField = schema.FindDataFieldIgnoreCase("StrDateCol")!;
                var dates3 = groupReader.ReadTimeColumn(strDateField, rowCount);
                Assert.AreEqual(new MarketTime(2025, 6, 18, 10, 0, 0), dates3[0]);
                Assert.AreEqual(MarketTime.MinValue, dates3[1], "非法格式日期文本必须安全回退至 MarketTime.MinValue");
            }
            finally
            {
                reader.DisposeSync();
            }
        }

        [TestMethod]
        public async Task ReadTimeColumn_UnsupportedType_ThrowsNotSupportedException()
        {
            var data = new List<AllTypesSample>
            {
                new() { IntVal = 10, DoubleVal = 3.14 }
            };

            using var ms = new MemoryStream();
            await ParquetSerializer.SerializeAsync(data, ms);
            ms.Position = 0;

            var reader = ParquetReaderExtensions.OpenParquetReaderSync(ms);
            try
            {
                using var groupReader = reader.OpenRowGroupReader(0);
                var intField = reader.Schema.FindDataFieldIgnoreCase("IntVal")!;
                Assert.ThrowsException<NotSupportedException>(() =>
                {
                    groupReader.ReadTimeColumn(intField, (int)groupReader.RowCount);
                });
            }
            finally
            {
                reader.DisposeSync();
            }
        }

        public class NullableSample
        {
            public double? NullableDouble { get; set; }
            public int? NullableInt { get; set; }
            public string? NullableStr { get; set; }
        }

        [TestMethod]
        public async Task ReadStringColumn_NullableAndNullValues_ShouldReturnEmptyString()
        {
            var data = new List<NullableSample>
            {
                new() { NullableDouble = null, NullableInt = null, NullableStr = null },
                new() { NullableDouble = 42.5, NullableInt = 100, NullableStr = "hello" }
            };

            using var ms = new MemoryStream();
            await ParquetSerializer.SerializeAsync(data, ms);
            ms.Position = 0;

            var reader = ParquetReaderExtensions.OpenParquetReaderSync(ms);
            try
            {
                using var groupReader = reader.OpenRowGroupReader(0);
                int rowCount = (int)groupReader.RowCount;

                var dblField = reader.Schema.FindDataFieldIgnoreCase("NullableDouble")!;
                var intField = reader.Schema.FindDataFieldIgnoreCase("NullableInt")!;
                var strField = reader.Schema.FindDataFieldIgnoreCase("NullableStr")!;

                // 1. 未配置白名单时，任何含有 null 的列必须直接抛出方案 C 诊断异常
                var ex1 = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    groupReader.ReadStringColumn(dblField, rowCount);
                });
                Assert.IsTrue(ex1.Message.Contains("NullableDouble") && ex1.Message.Contains("共 1/2 行无效") && ex1.Message.Contains("[0]"));

                var ex2 = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    groupReader.ReadStringColumn(intField, rowCount);
                });
                Assert.IsTrue(ex2.Message.Contains("NullableInt") && ex2.Message.Contains("共 1/2 行无效") && ex2.Message.Contains("[0]"));

                var ex3 = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    groupReader.ReadStringColumn(strField, rowCount);
                });
                Assert.IsTrue(ex3.Message.Contains("NullableStr") && ex3.Message.Contains("共 1/2 行无效") && ex3.Message.Contains("[0]"));

                // 2. 显式配置白名单后，允许 null 并正确回退为空字符串
                var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NullableDouble", "NullableInt", "NullableStr" };
                var dblStrs = groupReader.ReadStringColumn(dblField, rowCount, allowList);
                Assert.AreEqual("", dblStrs[0]);
                Assert.AreEqual("42.5", dblStrs[1]);

                var intStrs = groupReader.ReadStringColumn(intField, rowCount, allowList);
                Assert.AreEqual("", intStrs[0]);
                Assert.AreEqual("100", intStrs[1]);

                var strStrs = groupReader.ReadStringColumn(strField, rowCount, allowList);
                Assert.AreEqual("", strStrs[0]);
                Assert.AreEqual("hello", strStrs[1]);
            }
            finally
            {
                reader.DisposeSync();
            }
        }

        public class NullableDateSample
        {
            public DateTime? NullableDt { get; set; }
        }

        [TestMethod]
        public async Task ReadTimeColumn_WithNull_ThrowsWhenNotAllowed_AndSucceedsWhenAllowed()
        {
            var data = new List<NullableDateSample>
            {
                new() { NullableDt = null },
                new() { NullableDt = new DateTime(2025, 1, 2) }
            };

            using var ms = new MemoryStream();
            await ParquetSerializer.SerializeAsync(data, ms);
            ms.Position = 0;

            var reader = ParquetReaderExtensions.OpenParquetReaderSync(ms);
            try
            {
                using var groupReader = reader.OpenRowGroupReader(0);
                int rowCount = (int)groupReader.RowCount;
                var dtField = reader.Schema.FindDataFieldIgnoreCase("NullableDt")!;

                // 1. 未声明白名单：抛出方案 C 诊断异常
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    groupReader.ReadTimeColumn(dtField, rowCount);
                });
                Assert.IsTrue(ex.Message.Contains("NullableDt") && ex.Message.Contains("共 1/2 行无效") && ex.Message.Contains("[0]"));

                // 2. 显式声明白名单：允许并通过并填 MinValue
                var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NullableDt" };
                var dates = groupReader.ReadTimeColumn(dtField, rowCount, allow);
                Assert.AreEqual(MarketTime.MinValue, dates[0]);
                Assert.AreEqual(new MarketTime(2025, 1, 2), dates[1]);
            }
            finally
            {
                reader.DisposeSync();
            }
        }

        [TestMethod]
        public void FindDataFieldIgnoreCase_LocatesFieldCaseInsensitively()
        {
            string filePath = GetTestParquetFile();
            Assert.IsTrue(File.Exists(filePath), $"Test file not found: {filePath}");

            using var fs = File.OpenRead(filePath);
            var reader = ParquetReader.CreateAsync(fs).GetAwaiter().GetResult();
            try
            {
                var symField = reader.Schema.FindDataFieldIgnoreCase("symbol");
                Assert.IsNotNull(symField);
                Assert.AreEqual("symbol", symField.Name);

                var symUpper = reader.Schema.FindDataFieldIgnoreCase("SYMBOL");
                Assert.IsNotNull(symUpper);
                Assert.AreEqual("symbol", symUpper.Name);

                var closeField = reader.Schema.FindDataFieldIgnoreCase("CLOSE");
                Assert.IsNotNull(closeField);
                Assert.AreEqual("close", closeField.Name);

                var candidates = reader.Schema.FindDataFieldIgnoreCase("non_existent", "datetime", "timestamp");
                Assert.IsNotNull(candidates);
                Assert.AreEqual("datetime", candidates.Name);

                var notFound = reader.Schema.FindDataFieldIgnoreCase("non_existent_column");
                Assert.IsNull(notFound);
            }
            finally
            {
                reader.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [TestMethod]
        public void ReadStringColumn_ConvertsValuesSuccessfully()
        {
            string filePath = GetTestParquetFile();
            using var fs = File.OpenRead(filePath);
            var reader = ParquetReader.CreateAsync(fs).GetAwaiter().GetResult();
            try
            {
                using var groupReader = reader.OpenRowGroupReader(0);
                int rowCount = (int)groupReader.RowCount;
                Assert.IsTrue(rowCount > 0);

                var symField = reader.Schema.FindDataFieldIgnoreCase("symbol")!;
                string[] symbols = groupReader.ReadStringColumn(symField, rowCount);
                Assert.AreEqual(rowCount, symbols.Length);
                Assert.IsFalse(string.IsNullOrWhiteSpace(symbols[0]));

                var closeField = reader.Schema.FindDataFieldIgnoreCase("close")!;
                string[] closes = groupReader.ReadStringColumn(closeField, rowCount);
                Assert.AreEqual(rowCount, closes.Length);
                Assert.IsTrue(double.TryParse(closes[0], out double closeVal));
                Assert.IsTrue(closeVal > 0);
            }
            finally
            {
                reader.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [TestMethod]
        public void ReadTimeColumn_ParsesDatePrecisionSuccessfully()
        {
            string filePath = GetTestParquetFile();
            using var fs = File.OpenRead(filePath);
            var reader = ParquetReader.CreateAsync(fs).GetAwaiter().GetResult();
            try
            {
                using var groupReader = reader.OpenRowGroupReader(0);
                int rowCount = (int)groupReader.RowCount;
                Assert.IsTrue(rowCount > 0);

                var dtField = reader.Schema.FindDataFieldIgnoreCase("date", "datetime", "timestamp")!;
                MarketTime[] dates = groupReader.ReadTimeColumn(dtField, rowCount);
                Assert.AreEqual(rowCount, dates.Length);
                Assert.AreEqual(2024, dates[0].Year);
                // 未指定时区：保真保留原始钟表时间 15:00:00（绝不读宿主机本地时区）
                Assert.AreEqual(new TimeSpan(15, 0, 0), dates[0].TimeOfDay);

                // 若传入数据源业务时区（Asia/Shanghai），则准确归一化为 07:00:00 UTC
                var tzShanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
                MarketTime[] localDates = groupReader.ReadTimeColumn(dtField, rowCount, tzShanghai);
                Assert.AreEqual(new TimeSpan(7, 0, 0), localDates[0].TimeOfDay);

                // 若传入 UTC 时区，同样归一化为 07:00:00 UTC
                MarketTime[] utcDates = groupReader.ReadTimeColumn(dtField, rowCount, TimeZoneInfo.Utc);
                Assert.AreEqual(new TimeSpan(7, 0, 0), utcDates[0].TimeOfDay);
            }
            finally
            {
                reader.DisposeSync();
            }
        }

        public class TimezoneSample
        {
            public string TradeTime { get; set; } = "";
        }

        [TestMethod]
        public async Task ReadTimeColumn_WithTimezoneOffsets_ShouldPreserveWallClockTimeAndBeHostTimezoneAgnostic()
        {
            // 构造包含显式时区偏移 (+08:00, -05:00) 的 Parquet 数据流
            var data = new List<TimezoneSample>
            {
                new() { TradeTime = "2024-01-02T15:00:00.000+08:00" },
                new() { TradeTime = "2024-01-02T09:30:00.000-05:00" }
            };

            using var ms = new MemoryStream();
            await ParquetSerializer.SerializeAsync(data, ms);
            ms.Position = 0;

            var reader = ParquetReaderExtensions.OpenParquetReaderSync(ms);
            try
            {
                using var groupReader = reader.OpenRowGroupReader(0);
                var dtField = reader.Schema.FindDataFieldIgnoreCase("TradeTime")!;
                MarketTime[] dates = groupReader.ReadTimeColumn(dtField, 2);

                Assert.AreEqual(2, dates.Length);
                Assert.AreEqual(new MarketTime(2024, 1, 2, 15, 0, 0), dates[0]);
                Assert.AreEqual(new TimeSpan(15, 0, 0), dates[0].TimeOfDay);

                Assert.AreEqual(new MarketTime(2024, 1, 2, 9, 30, 0), dates[1]);
                Assert.AreEqual(new TimeSpan(9, 30, 0), dates[1].TimeOfDay);

                // 指定 UTC 时区读取
                MarketTime[] utcDates = groupReader.ReadTimeColumn(dtField, 2, TimeZoneInfo.Utc);
                Assert.AreEqual(new MarketTime(2024, 1, 2, 7, 0, 0), utcDates[0]);
                Assert.AreEqual(new MarketTime(2024, 1, 2, 14, 30, 0), utcDates[1]);
            }
            finally
            {
                reader.DisposeSync();
            }
        }

        public class MultiTypeTimeSample
        {
            public DateTime DtCol { get; set; }
            public DateTimeOffset DtoCol { get; set; }
            public long TsCol { get; set; }
            public string StrCol { get; set; } = "";
        }

        [TestMethod]
        public async Task ReadTimeColumn_CrossTypes_WithSourceTimeZone_ShouldAlignToIdenticalUtcInstant()
        {
            // 同一绝对物理时刻：北京时间 2024-01-02 15:00:00 -> UTC 2024-01-02 07:00:00
            // Unix 毫秒时间戳: 1704178800000
            long unixMs = 1704178800000L;
            var tzShanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

            var data = new List<MultiTypeTimeSample>
            {
                new()
                {
                    DtCol = new DateTime(2024, 1, 2, 15, 0, 0, DateTimeKind.Unspecified),
                    DtoCol = new DateTimeOffset(2024, 1, 2, 15, 0, 0, TimeSpan.FromHours(8)),
                    TsCol = unixMs,
                    StrCol = "2024-01-02T15:00:00.000+08:00"
                }
            };

            using var ms = new MemoryStream();
            await ParquetSerializer.SerializeAsync(data, ms);
            ms.Position = 0;

            var reader = ParquetReaderExtensions.OpenParquetReaderSync(ms);
            try
            {
                using var groupReader = reader.OpenRowGroupReader(0);
                var schema = reader.Schema;

                var dtField = schema.FindDataFieldIgnoreCase("DtCol")!;
                var tsField = schema.FindDataFieldIgnoreCase("TsCol")!;
                var strField = schema.FindDataFieldIgnoreCase("StrCol")!;

                MarketTime[] datesDt = groupReader.ReadTimeColumn(dtField, 1, tzShanghai);
                MarketTime[] datesTs = groupReader.ReadTimeColumn(tsField, 1, tzShanghai);
                MarketTime[] datesStr = groupReader.ReadTimeColumn(strField, 1, tzShanghai);

                MarketTime expectedUtc = new MarketTime(2024, 1, 2, 7, 0, 0);

                // 验证各种数据类型在配置了同一 sourceTimeZone 时，100% 严格对齐至完全相同的物理时刻
                Assert.AreEqual(expectedUtc, datesDt[0]);
                Assert.AreEqual(expectedUtc, datesTs[0]);
                Assert.AreEqual(expectedUtc, datesStr[0]);

                Assert.AreEqual(datesDt[0].Ticks, datesTs[0].Ticks);
                Assert.AreEqual(datesDt[0].Ticks, datesStr[0].Ticks);
            }
            finally
            {
                reader.DisposeSync();
            }
        }
    }
}
