using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sylvan.Data.Csv;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    /// <summary>
    /// 针对 CsvMarketSeriesSource 的单元测试。
    /// 物理提取 CSV 行与数据进行对照校验，以确保加载出来的日期及价格与底层物理数据完全对齐。
    /// </summary>
    [TestClass]
    public class CsvMarketSeriesSourceTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        [TestMethod]
        public void TestCsvMarketSeriesSourceReadAndPhysicalVerification()
        {
            string testDataRoot = GetTestDataRoot();
            string csvRoot = Path.Combine(testDataRoot, "csv");
            string tableId = "ashare.kline.1d.raw.baostock";
            string testSymbol = "sh.600000";

            // 1. 物理读取 CSV 数据，建立物理基准字典
            var physicalData = new Dictionary<DateTime, double>();
            var tableDir = Path.Combine(csvRoot, tableId);
            var yearDirs = Directory.GetDirectories(tableDir, "year=*");

            foreach (var yearDir in yearDirs)
            {
                string csvFile = Path.Combine(yearDir, $"{testSymbol}.csv");
                if (!File.Exists(csvFile)) continue;

                using var csvReader = CsvDataReader.Create(csvFile);
                int dateCol = csvReader.GetOrdinal("datetime");
                int closeCol = csvReader.GetOrdinal("close");

                while (csvReader.Read())
                {
                    DateTime date = DateTime.MinValue;
                    if (csvReader.GetFieldType(dateCol) == typeof(DateTime))
                    {
                        date = csvReader.GetDateTime(dateCol).Date;
                    }
                    else
                    {
                        string dtStr = csvReader.GetString(dateCol);
                        if (DateTime.TryParse(dtStr, out var parsedDate))
                        {
                            date = parsedDate.Date;
                        }
                    }

                    double close = csvReader.GetDouble(closeCol);
                    if (date != DateTime.MinValue)
                    {
                        physicalData[date] = close;
                    }
                }
            }

            Assert.IsTrue(physicalData.Count > 0, "Physical data should contain rows.");

            // 2. 通过 CsvMarketSeriesSource 载入序列
            var registry = new SimpleFieldRegistry();
            using var source = new CsvMarketSeriesSource(csvRoot, registry, tableId);
            
            // 测试元数据接口
            int symbolIdx = source.GetSymbolIndex(testSymbol);
            Assert.IsTrue(symbolIdx >= 0, "Symbol index should be found.");
            Assert.AreEqual(testSymbol, source.Symbols[symbolIdx]);
            
            int length = source.TradeDates.Count;
            double[] destination = new double[length];
            source.ReadSymbolSeries(testSymbol, "close", 0, length, destination);

            // 3. 逐交易日对比物理数值
            foreach (var kvp in physicalData)
            {
                DateTime date = kvp.Key;
                double physicalVal = kvp.Value;

                int dateIdx = source.GetDateIndex(date);
                Assert.IsTrue(dateIdx >= 0, $"Date {date:yyyy-MM-dd} should exist in TradeDates.");
                
                double loadedVal = destination[dateIdx];
                Assert.AreEqual(physicalVal, loadedVal, 1e-6, $"Close value mismatch at {date:yyyy-MM-dd}.");
            }
        }

        [TestMethod]
        public void TestCsvMarketSeriesSource_WithQuotedStringNumbers_ShouldParseDouble()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CarrotCsvStringTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                string tableId = "quoted_numbers_table";
                string tableDir = Path.Combine(tempDir, tableId, "year=2025");
                Directory.CreateDirectory(tableDir);

                // CSV 含带引号与空格的数值字符串
                string csvContent = "symbol,datetime,open,high,low,close,volume\n\"sh.600000\",\"2025-01-02\",\"10.50\",\" 11.20 \",\"9.80\",\"10.95\",\"100000\"\n";
                File.WriteAllText(Path.Combine(tableDir, "sh.600000.csv"), csvContent);

                var registry = new SimpleFieldRegistry();
                using var source = new CsvMarketSeriesSource(tempDir, registry, tableId);

                Assert.AreEqual(1, source.Symbols.Count);
                Assert.AreEqual(1, source.TradeDates.Count);

                double[] destination = new double[1];
                source.ReadSymbolSeries("sh.600000", "close", 0, 1, destination);
                Assert.AreEqual(10.95, destination[0], 1e-4);

                source.ReadSymbolSeries("sh.600000", "high", 0, 1, destination);
                Assert.AreEqual(11.20, destination[0], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
