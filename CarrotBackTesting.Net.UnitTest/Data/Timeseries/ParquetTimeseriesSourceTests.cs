using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Parquet;
using Parquet.Data;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    /// <summary>
    /// 针对 ParquetTimeseriesSource 的单元测试。
    /// 物理提取 Parquet 行与数据进行对照校验，以确保加载出来的日期及价格与底层物理数据完全对齐。
    /// </summary>
    [TestClass]
    public class ParquetTimeseriesSourceTests
    {
        private static string GetTestDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        [TestMethod]
        public void TestParquetTimeseriesSourceReadAndPhysicalVerification()
        {
            string testDataRoot = GetTestDataRoot();
            string parquetRoot = Path.Combine(testDataRoot, "parquet");
            string tableId = "ashare.kline.1d.raw.baostock";
            string testSymbol = "sh.600000";

            // 1. 物理读取年度 Parquet 数据，建立物理基准
            var physicalData = new Dictionary<DateTime, double>();
            var tableDir = Path.Combine(parquetRoot, tableId);
            var yearDirs = Directory.GetDirectories(tableDir, "year=*");

            foreach (var yearDir in yearDirs)
            {
                string parquetFile = Path.Combine(yearDir, "data.parquet");
                if (!File.Exists(parquetFile)) continue;

                using var fs = File.OpenRead(parquetFile);
                var reader = ParquetReader.CreateAsync(fs).GetAwaiter().GetResult();
                try
                {
                    var dataFields = reader.Schema.GetDataFields();
                    var symField = dataFields.First(f => f.Name.Equals("symbol", StringComparison.OrdinalIgnoreCase));
                    var dtField = dataFields.FirstOrDefault(f => f.Name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
                               ?? dataFields.FirstOrDefault(f => f.Name.Equals("timestamp", StringComparison.OrdinalIgnoreCase));
                    Assert.IsNotNull(dtField, "测试数据中未找到 datetime 或 timestamp 字段。");
                    var closeField = dataFields.First(f => f.Name.Equals("close", StringComparison.OrdinalIgnoreCase));

                    for (int i = 0; i < reader.RowGroupCount; i++)
                    {
                        var groupReader = reader.OpenRowGroupReader(i);
                        int rowCount = (int)groupReader.RowCount;
                        
                        var symArr = new ReadOnlyMemory<char>[rowCount];
                        var symDef = new int[rowCount];
                        groupReader.ReadRawAsync<ReadOnlyMemory<char>>(symField, symArr, symDef, null, default).GetAwaiter().GetResult();

                        var closeArr = new double[rowCount];
                        var defLevels = new int[rowCount];
                        groupReader.ReadRawAsync<double>(closeField, closeArr, defLevels, null, default).GetAwaiter().GetResult();

                        var dtArr = new ReadOnlyMemory<char>[rowCount];
                        var dtDef = new int[rowCount];
                        groupReader.ReadRawAsync<ReadOnlyMemory<char>>(dtField!, dtArr, dtDef, null, default).GetAwaiter().GetResult();

                        for (int idx = 0; idx < rowCount; idx++)
                        {
                            if (symDef[idx] > 0 && symArr[idx].Span.Equals(testSymbol.AsSpan(), StringComparison.OrdinalIgnoreCase))
                            {
                                if (dtDef[idx] > 0 && DateTime.TryParse(dtArr[idx].Span, out var rowDate))
                                {
                                    physicalData[rowDate.Date] = closeArr[idx];
                                }
                            }
                        }
                    }
                }
                finally { reader.DisposeAsync().GetAwaiter().GetResult(); }
            }

            Assert.IsTrue(physicalData.Count > 0, "Physical Parquet data should contain rows.");

            // 2. 使用 ParquetTimeseriesSource 加载序列
            using var source = new ParquetTimeseriesSource(parquetRoot, tableId);
            
            // 测试元数据接口
            int symbolIdx = source.GetSymbolIndex(testSymbol);
            Assert.IsTrue(symbolIdx >= 0, "Symbol index should be found.");
            Assert.AreEqual(testSymbol, source.Symbols[symbolIdx]);

            int length = source.TradeDates.Count;
            double[] destination = new double[length];
            source.ReadSymbolSeries(testSymbol, "close", 0, length, destination);

            // 3. 逐个交易日对比数据值
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
        public void TestParquetTimeseriesSourceMultiLevelMonthPartition()
        {
            string testDataRoot = GetTestDataRoot();
            string parquetRoot = Path.Combine(testDataRoot, "parquet");
            string tableId = "ashare.kline.1m.raw.custom";

            using var source = new ParquetTimeseriesSource(parquetRoot, tableId);

            Assert.IsTrue(source.Symbols.Count > 0, "Symbols should not be empty.");
            Assert.IsTrue(source.TradeDates.Count > 0, "TradeDates should not be empty.");
            Assert.IsTrue(source.FieldNames.Contains("close"));
            Assert.IsTrue(source.FieldNames.Contains("open"));
            Assert.IsTrue(source.FieldNames.Contains("volume"));

            string testSymbol = source.Symbols[0];
            int length = source.TradeDates.Count;
            double[] destination = new double[length];
            source.ReadSymbolSeries(testSymbol, "close", 0, length, destination);

            bool hasValidData = destination.Any(v => !double.IsNaN(v) && v > 0);
            Assert.IsTrue(hasValidData, "Destination should contain valid close prices from multi-level month partition parquet.");

            // 1. 测试元数据接口
            Assert.AreEqual(0, source.GetSymbolIndex(testSymbol));
            Assert.AreEqual(-1, source.GetSymbolIndex("unknown_sym"));
            Assert.IsTrue(source.GetDateIndex(source.TradeDates[0]) >= 0);
            Assert.IsTrue(source.GetDateIndex(new DateTime(1990, 1, 1)) < 0);
            Assert.IsNotNull(source.GetFieldType("close"));

            // 2. 泛型读取 (float 与 Int64 timestamp)
            float[] floatDest = new float[length];
            source.ReadSymbolSeries(testSymbol, "close", 0, length, floatDest);
            Assert.AreEqual((float)destination[0], floatDest[0], 1e-4f);

            long[] longDest = new long[length];
            source.ReadSymbolSeries(testSymbol, "timestamp", 0, length, longDest);
            Assert.IsTrue(longDest[0] > 0);

            double[] tsAsDouble = new double[length];
            source.ReadSymbolSeries(testSymbol, "timestamp", 0, length, tsAsDouble);
            Assert.AreEqual((double)longDest[0], tsAsDouble[0], 1.0);

            // 3. 读取不存在的字段（抛出 ArgumentException）
            double[] nanDest = new double[2];
            Assert.ThrowsException<ArgumentException>(() =>
            {
                source.ReadSymbolSeries(testSymbol, "non_existent", 0, 2, nanDest);
            });

            // 4. 边界异常
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            {
                source.ReadSymbolSeries(testSymbol, "close", -1, 1, new double[1]);
            });
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            {
                source.ReadSymbolSeries(testSymbol, "close", 0, length + 1, new double[length + 1]);
            });
            Assert.ThrowsException<ArgumentException>(() =>
            {
                source.ReadSymbolSeries(testSymbol, "close", 0, 1, new double[2]);
            });
        }

        [TestMethod]
        public void ParquetTimeseriesSource_WithTimeZone_ShouldNormalizeDatesToUtc()
        {
            string testDataRoot = GetTestDataRoot();
            string parquetRoot = Path.Combine(testDataRoot, "parquet");
            string tableId = "ashare.kline.1d.raw.baostock";

            // 1. 无时区加载作为对照组
            using var defaultSource = new ParquetTimeseriesSource(parquetRoot, tableId);
            Assert.IsNull(defaultSource.SourceTimeZone);
            Assert.IsTrue(defaultSource.TradeDates.Count > 0);
            DateTime defaultFirstDate = defaultSource.TradeDates[0];

            // 2. 带 Asia/Shanghai (+8) 时区加载
            using var tzSource = new ParquetTimeseriesSource(parquetRoot, tableId, timeZone: "Asia/Shanghai");
            Assert.IsNotNull(tzSource.SourceTimeZone);
            Assert.AreEqual("Asia/Shanghai", tzSource.SourceTimeZone.Id);
            Assert.AreEqual(defaultSource.TradeDates.Count, tzSource.TradeDates.Count);

            // Parquet 物理列存储的是 Unix 毫秒时间戳，在指定时区模式下保留精确的 UTC 时间戳
            Assert.AreEqual(DateTimeKind.Utc, tzSource.TradeDates[0].Kind);
            Assert.AreEqual(new DateTime(2021, 1, 4, 7, 0, 0, DateTimeKind.Utc), tzSource.TradeDates[0]);
        }
    }
}
