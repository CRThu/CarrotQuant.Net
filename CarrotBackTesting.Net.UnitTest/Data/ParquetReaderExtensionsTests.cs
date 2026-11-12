using System;
using System.IO;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Parquet;

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
