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
        public async Task ReadDateColumn_AllFormats_ShouldTruncateToDatePrecision()
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
                    StrDateCol = "invalid-date-string" // 测试解析失败回退 DateTime.MinValue
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

                // 1. DateTime
                var dtField = schema.FindDataFieldIgnoreCase("DtCol")!;
                var dates1 = groupReader.ReadDateColumn(dtField, rowCount);
                Assert.AreEqual(new DateTime(2025, 6, 18), dates1[0]);
                Assert.AreEqual(TimeSpan.Zero, dates1[0].TimeOfDay);

                // 2. Unix Milliseconds (long)
                var tsField = schema.FindDataFieldIgnoreCase("UnixMillisCol")!;
                var dates2 = groupReader.ReadDateColumn(tsField, rowCount);
                Assert.AreEqual(new DateTime(2025, 6, 18), dates2[0]);

                // 3. String Date
                var strDateField = schema.FindDataFieldIgnoreCase("StrDateCol")!;
                var dates3 = groupReader.ReadDateColumn(strDateField, rowCount);
                Assert.AreEqual(new DateTime(2025, 6, 18), dates3[0]);
                Assert.AreEqual(DateTime.MinValue, dates3[1], "非法格式日期文本必须安全回退至 DateTime.MinValue");
            }
            finally
            {
                reader.DisposeSync();
            }
        }

        [TestMethod]
        public async Task ReadDateColumn_UnsupportedType_ThrowsNotSupportedException()
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
                    groupReader.ReadDateColumn(intField, (int)groupReader.RowCount);
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
                var dblStrs = groupReader.ReadStringColumn(dblField, rowCount);
                Assert.AreEqual("", dblStrs[0]);
                Assert.AreEqual("42.5", dblStrs[1]);

                var intField = reader.Schema.FindDataFieldIgnoreCase("NullableInt")!;
                var intStrs = groupReader.ReadStringColumn(intField, rowCount);
                Assert.AreEqual("", intStrs[0]);
                Assert.AreEqual("100", intStrs[1]);

                var strField = reader.Schema.FindDataFieldIgnoreCase("NullableStr")!;
                var strStrs = groupReader.ReadStringColumn(strField, rowCount);
                Assert.AreEqual("", strStrs[0]);
                Assert.AreEqual("hello", strStrs[1]);
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
        public void ReadDateColumn_ParsesDatePrecisionSuccessfully()
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
                DateTime[] dates = groupReader.ReadDateColumn(dtField, rowCount);
                Assert.AreEqual(rowCount, dates.Length);
                Assert.AreEqual(2024, dates[0].Year);
                Assert.AreEqual(TimeSpan.Zero, dates[0].TimeOfDay); // 确保截断为纯 Date 精度
            }
            finally
            {
                reader.DisposeAsync().GetAwaiter().GetResult();
            }
        }
    }
}
