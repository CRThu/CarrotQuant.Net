using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Parquet;
using Parquet.Data;
using Parquet.Serialization;

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

        public class NonNullableSample
        {
            public string Symbol { get; set; } = "sh.600000";
            public DateTime Date { get; set; }
            public double Close { get; set; }
            public float Open { get; set; }
            public long Volume { get; set; }
            public int Amount { get; set; }
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_NonNullableColumns_ShouldNotBeZeroOrNaN()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_non_nullable_" + Guid.NewGuid().ToString("N"));
            string tableDir = Path.Combine(tempDir, "test_table");
            Directory.CreateDirectory(tableDir);

            try
            {
                var samples = new List<NonNullableSample>
                {
                    new()
                    {
                        Symbol = "sh.600000",
                        Date = new DateTime(2025, 1, 2),
                        Close = 12.34,
                        Open = 12.0f,
                        Volume = 10000L,
                        Amount = 100
                    }
                };

                string parquetPath = Path.Combine(tableDir, "data.parquet");
                using (var fs = File.Create(parquetPath))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }

                using var source = new ParquetTimeseriesSource(tempDir, "test_table");
                Assert.AreEqual(1, source.Symbols.Count);
                Assert.AreEqual("sh.600000", source.Symbols[0]);
                Assert.AreEqual(1, source.TradeDates.Count);

                double[] closes = new double[1];
                source.ReadSymbolSeries("sh.600000", "Close", 0, 1, closes);
                Assert.AreEqual(12.34, closes[0], 1e-4);

                float[] opens = new float[1];
                source.ReadSymbolSeries("sh.600000", "Open", 0, 1, opens);
                Assert.AreEqual(12.0f, opens[0], 1e-4f);

                long[] volumes = new long[1];
                source.ReadSymbolSeries("sh.600000", "Volume", 0, 1, volumes);
                Assert.AreEqual(10000L, volumes[0]);

                int[] amounts = new int[1];
                source.ReadSymbolSeries("sh.600000", "Amount", 0, 1, amounts);
                Assert.AreEqual(100, amounts[0]);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        public class NullableKlineSample
        {
            public string Symbol { get; set; } = "sh.600000";
            public DateTime Date { get; set; }
            public double? Close { get; set; }
        }

        public class NullableDateKlineSample
        {
            public string Symbol { get; set; } = "sh.600000";
            public DateTime? Date { get; set; }
            public double Close { get; set; } = 10.0;
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_NullableColumnsWithoutAllowlist_ThrowsInvalidDataException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_no_allow_" + Guid.NewGuid().ToString("N"));
            string tableDir = Path.Combine(tempDir, "test_table_no_allow");
            Directory.CreateDirectory(tableDir);

            try
            {
                var samples = new List<NullableKlineSample>
                {
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 2), Close = null },
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 3), Close = 25.5 }
                };

                string parquetPath = Path.Combine(tableDir, "data.parquet");
                using (var fs = File.Create(parquetPath))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }

                // 未显式配置允许空值，读取时必须抛出方案 C 诊断异常
                using var source = new ParquetTimeseriesSource(tempDir, "test_table_no_allow");
                double[] closes = new double[2];
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    source.ReadSymbolSeries("sh.600000", "Close", 0, 2, closes);
                });
                Assert.IsTrue(ex.Message.Contains("Close") && ex.Message.Contains("共 1/2 行无效") && ex.Message.Contains("[0]"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_NullableColumnsWithAllowlistParam_ShouldHandleNullsAsNaN()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_allow_param_" + Guid.NewGuid().ToString("N"));
            string tableDir = Path.Combine(tempDir, "test_table_allow_param");
            Directory.CreateDirectory(tableDir);

            try
            {
                var samples = new List<NullableKlineSample>
                {
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 2), Close = null },
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 3), Close = 25.5 }
                };

                string parquetPath = Path.Combine(tableDir, "data.parquet");
                using (var fs = File.Create(parquetPath))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }

                // 通过构造参数传入允许空值的字段集合
                var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Close" };
                using var source = new ParquetTimeseriesSource(tempDir, "test_table_allow_param", nullableFields: allowList);
                Assert.AreEqual(2, source.TradeDates.Count);

                double[] closes = new double[2];
                source.ReadSymbolSeries("sh.600000", "Close", 0, 2, closes);
                Assert.IsTrue(double.IsNaN(closes[0]), "Null row should be NaN");
                Assert.AreEqual(25.5, closes[1], 1e-4, "Non-null row must preserve value and not be shifted");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_NullableColumnsWithMetadataJson_ShouldHandleNullsAsNaN()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_allow_meta_" + Guid.NewGuid().ToString("N"));
            string tableDir = Path.Combine(tempDir, "test_table_allow_meta");
            Directory.CreateDirectory(tableDir);

            try
            {
                var samples = new List<NullableKlineSample>
                {
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 2), Close = null },
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 3), Close = 25.5 }
                };

                string parquetPath = Path.Combine(tableDir, "data.parquet");
                using (var fs = File.Create(parquetPath))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }

                // 写入包含 nullable_fields 的 metadata.json
                string metadataPath = Path.Combine(tableDir, "metadata.json");
                string metaJson = """
                {
                    "table_id": "test_table_allow_meta",
                    "category": "timeseries",
                    "format": "parquet",
                    "layout": "flat",
                    "partition": "none",
                    "schema": {
                        "Symbol": "string",
                        "Date": "datetime",
                        "Close": "double"
                    },
                    "nullable_fields": ["Close"]
                }
                """;
                File.WriteAllText(metadataPath, metaJson);

                // 自动由 StorageResolver 读取 metadata.json 并注入白名单
                using var source = new ParquetTimeseriesSource(tempDir, "test_table_allow_meta");
                Assert.AreEqual(2, source.TradeDates.Count);

                double[] closes = new double[2];
                source.ReadSymbolSeries("sh.600000", "Close", 0, 2, closes);
                Assert.IsTrue(double.IsNaN(closes[0]), "Null row should be NaN");
                Assert.AreEqual(25.5, closes[1], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_NullInDateColumn_WithoutAllowlist_ThrowsInvalidDataException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_null_date_" + Guid.NewGuid().ToString("N"));
            string tableDir = Path.Combine(tempDir, "test_table_null_date");
            Directory.CreateDirectory(tableDir);

            try
            {
                var samples = new List<NullableDateKlineSample>
                {
                    new() { Symbol = "sh.600000", Date = null, Close = 10.0 },
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 3), Close = 25.5 }
                };

                string parquetPath = Path.Combine(tableDir, "data.parquet");
                using (var fs = File.Create(parquetPath))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }

                // 主时间戳列如果含有 null 且未显式配置允许空值，在初始化构建索引时立即抛出方案 C 诊断异常
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    using var source = new ParquetTimeseriesSource(tempDir, "test_table_null_date");
                });
                Assert.IsTrue(ex.Message.Contains("Date") && ex.Message.Contains("共 1/2 行无效") && ex.Message.Contains("[0]"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
