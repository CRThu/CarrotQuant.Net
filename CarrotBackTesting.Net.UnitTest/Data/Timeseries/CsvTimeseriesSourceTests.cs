using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sylvan.Data.Csv;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    /// <summary>
    /// 针对 CsvTimeseriesSource 的单元测试。
    /// 物理提取 CSV 行与数据进行对照校验，以确保加载出来的日期及价格与底层物理数据完全对齐。
    /// </summary>
    [TestClass]
    public class CsvTimeseriesSourceTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        [TestMethod]
        public void TestCsvTimeseriesSourceReadAndPhysicalVerification()
        {
            string testDataRoot = GetTestDataRoot();
            string csvRoot = Path.Combine(testDataRoot, "csv");
            string tableId = "ashare.kline.1d.raw.baostock";
            string testSymbol = "sh.600000";

            // 1. 物理读取 CSV 数据，建立物理基准字典
            var physicalData = new Dictionary<MarketTime, double>();
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
                    MarketTime date = csvReader.ParseTime(dateCol);
                    double close = csvReader.GetDouble(closeCol);
                    if (date != MarketTime.MinValue)
                    {
                        physicalData[date] = close;
                    }
                }
            }

            Assert.IsTrue(physicalData.Count > 0, "Physical data should contain rows.");

            // 2. 通过 CsvTimeseriesSource 载入序列
            using var source = TimeseriesSourceFactory.Create(csvRoot, tableId);
            
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
                MarketTime date = kvp.Key;
                double physicalVal = kvp.Value;

                int dateIdx = source.GetDateIndex(date);
                Assert.IsTrue(dateIdx >= 0, $"Date {date:yyyy-MM-dd} should exist in TradeDates.");
                
                double loadedVal = destination[dateIdx];
                Assert.AreEqual(physicalVal, loadedVal, 1e-6, $"Close value mismatch at {date:yyyy-MM-dd}.");
            }
        }

        [TestMethod]
        public void TestCsvTimeseriesSource_WithQuotedStringNumbers_ShouldParseDouble()
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

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

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
        public void TestCsvTimeseriesSource_TimestampColumn_DescendingDates_AndTypeConversions()
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

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

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
        public void TestCsvTimeseriesSource_BufferPoolExpansion()
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

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

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

        [TestMethod]
        public void CsvTimeseriesSource_PartitionNone_ThrowsNotSupportedException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CarrotCsvPartitionNoneTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                string tableId = "none_partition_table";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);

                // 单文件 data.csv，试图作为时序源加载
                string csvContent = "datetime,close\n2025-01-02,10.0\n";
                File.WriteAllText(Path.Combine(tableDir, "data.csv"), csvContent);

                var ex = Assert.ThrowsException<NotSupportedException>(() =>
                {
                    using var source = TimeseriesSourceFactory.Create(tempDir, tableId);
                });
                StringAssert.Contains(ex.Message, "不支持 partition");
                StringAssert.Contains(ex.Message, "Parquet");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void CsvTimeseriesSource_WithTimeZone_ShouldNormalizeDatesToUtc()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "carrot_test_tz_" + Guid.NewGuid().ToString("N"));
            try
            {
                string tableId = "tz_test_table";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);

                // 写入带具体分钟的本地时间 CSV (如北京时间 09:30:00 与 10:00:00)
                string csvContent = "symbol,datetime,close\n" +
                                    "sh.600000,2025-01-02 09:30:00,10.0\n" +
                                    "sh.600000,2025-01-02 10:00:00,11.0\n";
                File.WriteAllText(Path.Combine(tableDir, "sh.600000.csv"), csvContent);

                // 1. 指定 Asia/Shanghai (+8) 时区加载
                using var sourceAsia = (TimeseriesSourceBase)TimeseriesSourceFactory.Create(tempDir, tableId, timeZone: "Asia/Shanghai");
                Assert.IsNotNull(sourceAsia.SourceTimeZone);
                Assert.AreEqual(2, sourceAsia.TradeDates.Count);

                // 北京时间 09:30:00 对应 UTC 01:30:00
                MarketTime expectedUtc1 = new MarketTime(2025, 1, 2, 1, 30, 0);
                MarketTime expectedUtc2 = new MarketTime(2025, 1, 2, 2, 0, 0);
                Assert.AreEqual(expectedUtc1, sourceAsia.TradeDates[0]);
                Assert.AreEqual(expectedUtc2, sourceAsia.TradeDates[1]);
                Assert.AreEqual(DateTimeKind.Utc, sourceAsia.TradeDates[0].ToUtcDateTime().Kind);

                // 2. 验证 ReadSymbolSeries 能够正常按 UTC 索引读取
                double[] dest = new double[2];
                sourceAsia.ReadSymbolSeries("sh.600000", "close", 0, 2, dest);
                Assert.AreEqual(10.0, dest[0]);
                Assert.AreEqual(11.0, dest[1]);

                // 3. 验证 factory 工厂与 TimeseriesConfig.TimeZone 集成
                var tsConfig = new CarrotBacktesting.NET.Config.TimeseriesConfig
                {
                    TableId = tableId,
                    TimeZone = "America/New_York",
                    Cache = false
                };
                var registry = TimeseriesRegistryFactory.Create(new StorageResolver(tempDir), tsConfig);
                // 纽约时间 09:30:00 EST (UTC-5) 对应 UTC 14:30:00
                DateTime expectedNyUtc = new DateTime(2025, 1, 2, 14, 30, 0, DateTimeKind.Utc);
                Assert.AreEqual(expectedNyUtc, registry.Primary.Metadata.TradeDates[0]);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void TestCsvTimeseriesSource_EmptyAndWhitespaceFields_ShouldFillWithNaN()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CarrotCsvEmptyTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                string tableId = "empty_fields_table";
                string tableDir = Path.Combine(tempDir, tableId, "year=2025");
                Directory.CreateDirectory(tableDir);

                // 第 1 行正常
                // 第 2 行 close 留空（连续逗号），volume 为纯空格
                // 第 3 行 close 为纯空格，volume 正常
                string csvContent = "symbol,datetime,open,high,low,close,volume\n" +
                                   "\"sh.600000\",\"2025-01-02\",\"10.0\",\"11.0\",\"9.5\",\"10.5\",\"100000\"\n" +
                                   "\"sh.600000\",\"2025-01-03\",\"10.5\",\"11.5\",\"10.0\",,\"   \"\n" +
                                   "\"sh.600000\",\"2025-01-06\",\"11.0\",\"12.0\",\"10.5\",\"   \",\"200000\"\n";
                File.WriteAllText(Path.Combine(tableDir, "sh.600000.csv"), csvContent);

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

                Assert.AreEqual(1, source.Symbols.Count);
                Assert.AreEqual(3, source.TradeDates.Count);

                double[] closeDest = new double[3];
                source.ReadSymbolSeries("sh.600000", "close", 0, 3, closeDest);
                Assert.AreEqual(10.5, closeDest[0], 1e-4);
                Assert.IsTrue(double.IsNaN(closeDest[1]), "第 2 行连续逗号留空的 close 必须解析为 NaN");
                Assert.IsTrue(double.IsNaN(closeDest[2]), "第 3 行纯空格的 close 必须解析为 NaN");

                double[] volDest = new double[3];
                source.ReadSymbolSeries("sh.600000", "volume", 0, 3, volDest);
                Assert.AreEqual(100000.0, volDest[0], 1e-4);
                Assert.IsTrue(double.IsNaN(volDest[1]), "第 2 行纯空格的 volume 必须解析为 NaN");
                Assert.AreEqual(200000.0, volDest[2], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void TestCsvTimeseriesSource_IntradayMinuteData_MultiSymbolPreservesTimestampsAndValues()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_csv_minute_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string tableId = "ashare.kline.1m";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);

                // CSV 写入日内分钟数据，按标的拆分存储为 sh.600000.csv 与 sz.000001.csv
                string csv600 = """
                datetime,close,volume
                2025-01-06 09:30:00,10.0,1000
                2025-01-06 09:31:00,10.1,1100
                2025-01-06 09:32:00,10.2,1200
                """;
                string csv001 = """
                datetime,close,volume
                2025-01-06 09:30:00,20.0,2000
                2025-01-06 09:32:00,20.2,2200
                """;
                File.WriteAllText(Path.Combine(tableDir, "sh.600000.csv"), csv600);
                File.WriteAllText(Path.Combine(tableDir, "sz.000001.csv"), csv001);

                File.WriteAllText(Path.Combine(tableDir, "metadata.json"), $$"""
                {
                    "table_id": "{{tableId}}",
                    "category": "timeseries",
                    "format": "csv",
                    "layout": "flat",
                    "partition": "symbol",
                    "schema": {
                        "datetime": "datetime",
                        "close": "double",
                        "volume": "double"
                    }
                }
                """);

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

                // 1. 验证时间轴完整保真
                Assert.AreEqual(3, source.TradeDates.Count);
                Assert.AreEqual(new DateTime(2025, 1, 6, 9, 30, 0), source.TradeDates[0]);
                Assert.AreEqual(new DateTime(2025, 1, 6, 9, 31, 0), source.TradeDates[1]);
                Assert.AreEqual(new DateTime(2025, 1, 6, 9, 32, 0), source.TradeDates[2]);

                // 2. 验证多标的
                Assert.AreEqual(2, source.Symbols.Count);

                // 3. 验证第一只股票全量数值
                double[] closes1 = new double[3];
                source.ReadSymbolSeries("sh.600000", "close", 0, 3, closes1);
                Assert.AreEqual(10.0, closes1[0], 1e-4);
                Assert.AreEqual(10.1, closes1[1], 1e-4);
                Assert.AreEqual(10.2, closes1[2], 1e-4);

                // 4. 验证第二只股票在缺失时间步严格填补 NaN
                double[] closes2 = new double[3];
                source.ReadSymbolSeries("sz.000001", "close", 0, 3, closes2);
                Assert.AreEqual(20.0, closes2[0], 1e-4);
                Assert.IsTrue(double.IsNaN(closes2[1]), "缺失分钟必须填充为 NaN");
                Assert.AreEqual(20.2, closes2[2], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void TestCsvTimeseriesSource_DailyData_WithNoSpecialSuffix_BuildsCalendarAndValuesAccurately()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_csv_daily_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string tableId = "stock_daily_bars";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);

                string csv600 = """
                datetime,close
                2025-01-06,10.0
                2025-01-07,11.0
                """;
                string csv001 = """
                datetime,close
                2025-01-06,20.0
                2025-01-07,21.0
                """;
                File.WriteAllText(Path.Combine(tableDir, "sh.600000.csv"), csv600);
                File.WriteAllText(Path.Combine(tableDir, "sz.000001.csv"), csv001);

                File.WriteAllText(Path.Combine(tableDir, "metadata.json"), $$"""
                {
                    "table_id": "{{tableId}}",
                    "category": "timeseries",
                    "format": "csv",
                    "layout": "flat",
                    "partition": "symbol",
                    "schema": {
                        "datetime": "datetime",
                        "close": "double"
                    }
                }
                """);

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

                // 1. 验证日历天数与零点纯日期
                Assert.AreEqual(2, source.TradeDates.Count);
                Assert.AreEqual(new DateTime(2025, 1, 6), source.TradeDates[0]);
                Assert.AreEqual(new DateTime(2025, 1, 7), source.TradeDates[1]);
                Assert.AreEqual(TimeSpan.Zero, source.TradeDates[0].TimeOfDay);

                // 2. 验证多标的数值
                double[] closes1 = new double[2];
                source.ReadSymbolSeries("sh.600000", "close", 0, 2, closes1);
                Assert.AreEqual(10.0, closes1[0], 1e-4);
                Assert.AreEqual(11.0, closes1[1], 1e-4);

                double[] closes2 = new double[2];
                source.ReadSymbolSeries("sz.000001", "close", 0, 2, closes2);
                Assert.AreEqual(20.0, closes2[0], 1e-4);
                Assert.AreEqual(21.0, closes2[1], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void CsvTimeseriesSource_ExplicitPartitionNone_ThrowsNotSupportedException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_csv_explicit_none_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string tableId = "csv_explicit_none";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);

                string csvContent = "symbol,datetime,close\nsh.600000,2025-01-06,10.0\n";
                File.WriteAllText(Path.Combine(tableDir, "all_market.csv"), csvContent);
                File.WriteAllText(Path.Combine(tableDir, "metadata.json"), $$"""
                {
                    "table_id": "{{tableId}}",
                    "category": "timeseries",
                    "format": "csv",
                    "layout": "flat",
                    "partition": "none",
                    "schema": {
                        "symbol": "string",
                        "datetime": "datetime",
                        "close": "double"
                    }
                }
                """);

                var ex = Assert.ThrowsException<NotSupportedException>(() =>
                {
                    using var source = TimeseriesSourceFactory.Create(tempDir, tableId);
                });

                StringAssert.Contains(ex.Message, "不支持 partition");
                StringAssert.Contains(ex.Message, "Parquet");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void CsvTimeseriesSource_NoSymbolColumn_ExtractsFromFileName()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_csv_nosym_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string tableId = "csv_no_sym_col";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);

                // 物理文件内部没有 symbol 列，仅有 datetime 和 close，文件名本身即品种代码
                string csvContent1 = """
                datetime,close
                2025-01-06,10.0
                2025-01-07,11.0
                """;
                string csvContent2 = """
                datetime,close
                2025-01-06,20.0
                2025-01-07,21.0
                """;
                File.WriteAllText(Path.Combine(tableDir, "sh.600000.csv"), csvContent1);
                File.WriteAllText(Path.Combine(tableDir, "sz.000001.csv"), csvContent2);
                File.WriteAllText(Path.Combine(tableDir, "metadata.json"), $$"""
                {
                    "table_id": "{{tableId}}",
                    "category": "timeseries",
                    "format": "csv",
                    "layout": "flat",
                    "partition": "symbol",
                    "schema": {
                        "datetime": "datetime",
                        "close": "double"
                    }
                }
                """);

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

                // 内部无 symbol 列，按文件名隔离
                Assert.AreEqual(2, source.Symbols.Count);
                Assert.AreEqual("sh.600000", source.Symbols[0]);
                Assert.AreEqual("sz.000001", source.Symbols[1]);
                Assert.AreEqual(2, source.TradeDates.Count);

                double[] closes1 = new double[2];
                source.ReadSymbolSeries("sh.600000", "close", 0, 2, closes1);
                Assert.AreEqual(10.0, closes1[0], 1e-4);
                Assert.AreEqual(11.0, closes1[1], 1e-4);

                double[] closes2 = new double[2];
                source.ReadSymbolSeries("sz.000001", "close", 0, 2, closes2);
                Assert.AreEqual(20.0, closes2[0], 1e-4);
                Assert.AreEqual(21.0, closes2[1], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void CsvTimeseriesSource_MissingDatetimeColumn_ThrowsInvalidOperationException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_csv_nodatetime_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string tableId = "csv_no_datetime";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);

                string csvContent = """
                close,volume
                10.0,1000
                """;
                File.WriteAllText(Path.Combine(tableDir, "sh.600000.csv"), csvContent);

                var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                {
                    using var source = TimeseriesSourceFactory.Create(tempDir, tableId);
                });
                StringAssert.Contains(ex.Message, "Required datetime column not found");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public void CsvTimeseriesSource_DuplicateConflictingTimestamps_ThrowsInvalidDataException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_csv_conflict_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string tableId = "csv_conflict_table";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);

                // 同一标的在同一时间戳有两条冲突的记录 (10.0 vs 10.5)
                string csvContent = """
                datetime,close
                2025-01-06 09:30:00,10.0
                2025-01-06 09:30:00,10.5
                """;
                File.WriteAllText(Path.Combine(tableDir, "sh.600000.csv"), csvContent);

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);
                double[] dest = new double[1];

                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    source.ReadSymbolSeries("sh.600000", "close", 0, 1, dest);
                });
                StringAssert.Contains(ex.Message, "存在冲突的重复记录");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
