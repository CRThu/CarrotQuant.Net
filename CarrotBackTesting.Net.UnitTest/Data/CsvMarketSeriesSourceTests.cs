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

        [TestMethod]
        public void TestCsvMarketSeriesSource_TimestampColumn_DescendingDates_AndTypeConversions()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CarrotCsvTsTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                string tableId = "ts_desc_table";
                string tableDir = Path.Combine(tempDir, tableId, "year=2025");
                Directory.CreateDirectory(tableDir);

                // 两行数据，日期倒序，第一列使用 timestamp (毫秒戳)
                long ts1 = new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                long ts2 = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

                string csvContent = $"symbol,timestamp,open,close,volume\nsh.600000,{ts1},12.5,13.0,1000\nsh.600000,{ts2},10.0,11.5,500\n";
                File.WriteAllText(Path.Combine(tableDir, "sh.600000.csv"), csvContent);

                var registry = new SimpleFieldRegistry();
                using var source = new CsvMarketSeriesSource(tempDir, registry, tableId);

                Assert.AreEqual(1, source.Symbols.Count);
                Assert.AreEqual(2, source.TradeDates.Count);
                Assert.AreEqual(new DateTime(2025, 1, 2), source.TradeDates[0]);
                Assert.AreEqual(new DateTime(2025, 1, 3), source.TradeDates[1]);

                // 1. 元数据接口
                Assert.AreEqual(0, source.GetSymbolIndex("sh.600000"));
                Assert.AreEqual(-1, source.GetSymbolIndex("unknown"));
                Assert.IsTrue(source.GetDateIndex(new DateTime(2025, 1, 2)) >= 0);
                Assert.IsTrue(source.GetDateIndex(new DateTime(2020, 1, 1)) < 0);
                Assert.IsNotNull(source.GetFieldType("close"));

                // 2. 泛型转换读取 (float, int, long, double)
                float[] floatDest = new float[2];
                source.ReadSymbolSeries("sh.600000", "close", 0, 2, floatDest);
                Assert.AreEqual(11.5f, floatDest[0], 1e-4f);
                Assert.AreEqual(13.0f, floatDest[1], 1e-4f);

                int[] intDest = new int[2];
                source.ReadSymbolSeries("sh.600000", "volume", 0, 2, intDest);
                Assert.AreEqual(500, intDest[0]);
                Assert.AreEqual(1000, intDest[1]);

                long[] longDest = new long[2];
                source.ReadSymbolSeries("sh.600000", "volume", 0, 2, longDest);
                Assert.AreEqual(500L, longDest[0]);
                Assert.AreEqual(1000L, longDest[1]);

                // 3. 边界异常校验
                Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                {
                    source.ReadSymbolSeries("sh.600000", "close", -1, 1, new double[1]);
                });
                Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                {
                    source.ReadSymbolSeries("sh.600000", "close", 1, 2, new double[2]); // 1 + 2 > 2
                });
                Assert.ThrowsException<ArgumentException>(() =>
                {
                    source.ReadSymbolSeries("sh.600000", "close", 0, 1, new double[2]); // length mismatch
                });

                // 4. 读取不存在的字段（跳过填充保持 NaN）
                double[] nanDest = new double[2];
                source.ReadSymbolSeries("sh.600000", "non_existent_col", 0, 2, nanDest);
                Assert.IsTrue(double.IsNaN(nanDest[0]));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void TestCsvMarketSeriesSource_BufferPoolExpansion()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CarrotCsvPoolTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                string tableId = "pool_expand_table";
                string tableDir = Path.Combine(tempDir, tableId, "year=2025");
                Directory.CreateDirectory(tableDir);

                // 生成 35 行数据（超出初始 capacity=16，触发池化数组倍增扩容分支）
                using var sw = new StreamWriter(Path.Combine(tableDir, "sh.600000.csv"));
                sw.WriteLine("symbol,datetime,close");
                for (int i = 1; i <= 35; i++)
                {
                    sw.WriteLine($"sh.600000,2025-01-{i:D2},10.0");
                }
                sw.Flush();
                sw.Close();

                var registry = new SimpleFieldRegistry();
                using var source = new CsvMarketSeriesSource(tempDir, registry, tableId);

                // 请求只取 5 天，但底层文件扫描时读入 35 行触发扩容
                double[] dest = new double[5];
                source.ReadSymbolSeries("sh.600000", "close", 0, 5, dest);
                Assert.AreEqual(10.0, dest[0]);
                Assert.AreEqual(10.0, dest[4]);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
