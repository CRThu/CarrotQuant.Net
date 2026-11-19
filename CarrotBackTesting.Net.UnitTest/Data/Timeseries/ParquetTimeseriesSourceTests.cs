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
            var physicalData = new Dictionary<MarketTime, double>();
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
                                if (dtDef[idx] > 0 && MarketTime.TryParse(dtArr[idx].Span, sourceTimeZone: null, out var rowDate))
                                {
                                    physicalData[rowDate] = closeArr[idx];
                                }
                            }
                        }
                    }
                }
                finally { reader.DisposeAsync().GetAwaiter().GetResult(); }
            }

            Assert.IsTrue(physicalData.Count > 0, "Physical Parquet data should contain rows.");

            // 2. 使用 ParquetTimeseriesSource 加载序列
            using var source = TimeseriesSourceFactory.Create(parquetRoot, tableId);
            
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
                MarketTime date = kvp.Key;
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

            using var source = TimeseriesSourceFactory.Create(parquetRoot, tableId);

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
            using var defaultSource = (TimeseriesSourceBase)TimeseriesSourceFactory.Create(parquetRoot, tableId);
            Assert.IsNull(defaultSource.SourceTimeZone);
            Assert.IsTrue(defaultSource.TradeDates.Count > 0);
            MarketTime defaultFirstDate = defaultSource.TradeDates[0];

            // 2. 带 Asia/Shanghai (+8) 时区加载
            using var tzSource = (TimeseriesSourceBase)TimeseriesSourceFactory.Create(parquetRoot, tableId, timeZone: "Asia/Shanghai");
            Assert.IsNotNull(tzSource.SourceTimeZone);
            Assert.AreEqual("Asia/Shanghai", tzSource.SourceTimeZone.Id);
            Assert.AreEqual(defaultSource.TradeDates.Count, tzSource.TradeDates.Count);

            // Parquet 物理列存储的是 Unix 毫秒时间戳，在指定时区模式下保留精确的 UTC 时间戳
            Assert.AreEqual(DateTimeKind.Utc, tzSource.TradeDates[0].ToUtcDateTime().Kind);
            Assert.AreEqual((MarketTime)new DateTime(2021, 1, 4, 7, 0, 0, DateTimeKind.Utc), tzSource.TradeDates[0]);
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

                using var source = TimeseriesSourceFactory.Create(tempDir, "test_table");
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
                using var source = TimeseriesSourceFactory.Create(tempDir, "test_table_no_allow");
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
                using var source = TimeseriesSourceFactory.Create(tempDir, "test_table_allow_param", nullableFields: allowList);
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
                using var source = TimeseriesSourceFactory.Create(tempDir, "test_table_allow_meta");
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
                    using var source = TimeseriesSourceFactory.Create(tempDir, "test_table_null_date");
                });
                Assert.IsTrue(ex.Message.Contains("Date") && ex.Message.Contains("共 1/2 行无效") && ex.Message.Contains("[0]"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        public class NullableSymbolSample
        {
            public string? Symbol { get; set; }
            public DateTime Date { get; set; }
            public double Close { get; set; }
        }

        [TestMethod]
        public async Task SymbolColumn_WithNull_WithoutAllowlist_ThrowsInvalidDataException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_null_sym_" + Guid.NewGuid().ToString("N"));
            string tableDir = Path.Combine(tempDir, "symbol_null_table");
            Directory.CreateDirectory(tableDir);

            try
            {
                var samples = new List<NullableSymbolSample>
                {
                    new() { Symbol = null, Date = new DateTime(2025, 1, 2), Close = 10.0 },
                    new() { Symbol = "sh.600000", Date = new DateTime(2025, 1, 3), Close = 11.0 }
                };

                string parquetPath = Path.Combine(tableDir, "data.parquet");
                using (var fs = File.Create(parquetPath))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }

                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    using var source = TimeseriesSourceFactory.Create(tempDir, "symbol_null_table");
                });
                StringAssert.Contains(ex.Message, "Symbol");
                StringAssert.Contains(ex.Message, "共 1/2 行无效");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        public class MixedNumericSample
        {
            public string Symbol { get; set; } = "sh.600000";
            public DateTime Date { get; set; }
            public double DoubleVal { get; set; }
            public float FloatVal { get; set; }
            public long LongVal { get; set; }
            public int IntVal { get; set; }
        }

        [TestMethod]
        public async Task ArrayValueConversions_AllPrimitiveCombinations_ShouldConvertAccurately()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_num_conv_" + Guid.NewGuid().ToString("N"));
            string tableDir = Path.Combine(tempDir, "numeric_convert_table");
            Directory.CreateDirectory(tableDir);

            try
            {
                var samples = new List<MixedNumericSample>
                {
                    new()
                    {
                        Symbol = "sh.600000",
                        Date = new DateTime(2025, 1, 2),
                        DoubleVal = 100.5,
                        FloatVal = 50.25f,
                        LongVal = 999999L,
                        IntVal = 1234
                    }
                };

                string parquetPath = Path.Combine(tableDir, "data.parquet");
                using (var fs = File.Create(parquetPath))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }

                using var source = TimeseriesSourceFactory.Create(tempDir, "numeric_convert_table");
                Assert.AreEqual(1, source.TradeDates.Count);

                // 1. double -> long & int
                long[] doubleAsLong = new long[1];
                source.ReadSymbolSeries("sh.600000", "DoubleVal", 0, 1, doubleAsLong);
                Assert.AreEqual(100L, doubleAsLong[0]);

                int[] doubleAsInt = new int[1];
                source.ReadSymbolSeries("sh.600000", "DoubleVal", 0, 1, doubleAsInt);
                Assert.AreEqual(100, doubleAsInt[0]);

                // 2. float -> double, long, int
                double[] floatAsDouble = new double[1];
                source.ReadSymbolSeries("sh.600000", "FloatVal", 0, 1, floatAsDouble);
                Assert.AreEqual(50.25, floatAsDouble[0], 1e-4);

                long[] floatAsLong = new long[1];
                source.ReadSymbolSeries("sh.600000", "FloatVal", 0, 1, floatAsLong);
                Assert.AreEqual(50L, floatAsLong[0]);

                int[] floatAsInt = new int[1];
                source.ReadSymbolSeries("sh.600000", "FloatVal", 0, 1, floatAsInt);
                Assert.AreEqual(50, floatAsInt[0]);

                // 3. long -> double, float, int
                double[] longAsDouble = new double[1];
                source.ReadSymbolSeries("sh.600000", "LongVal", 0, 1, longAsDouble);
                Assert.AreEqual(999999.0, longAsDouble[0], 1e-4);

                float[] longAsFloat = new float[1];
                source.ReadSymbolSeries("sh.600000", "LongVal", 0, 1, longAsFloat);
                Assert.AreEqual(999999.0f, longAsFloat[0], 1e-4f);

                int[] longAsInt = new int[1];
                source.ReadSymbolSeries("sh.600000", "LongVal", 0, 1, longAsInt);
                Assert.AreEqual(999999, longAsInt[0]);

                // 4. int -> double, float, long
                double[] intAsDouble = new double[1];
                source.ReadSymbolSeries("sh.600000", "IntVal", 0, 1, intAsDouble);
                Assert.AreEqual(1234.0, intAsDouble[0], 1e-4);

                float[] intAsFloat = new float[1];
                source.ReadSymbolSeries("sh.600000", "IntVal", 0, 1, intAsFloat);
                Assert.AreEqual(1234.0f, intAsFloat[0], 1e-4f);

                long[] intAsLong = new long[1];
                source.ReadSymbolSeries("sh.600000", "IntVal", 0, 1, intAsLong);
                Assert.AreEqual(1234L, intAsLong[0]);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        public class DateTimeOffsetSample
        {
            public string Symbol { get; set; } = "sh.600000";
            public DateTimeOffset Date { get; set; }
            public DateTimeOffset? OptionalTime { get; set; }
            public double Close { get; set; }
        }

        [TestMethod]
        public async Task DateTimeOffsetColumn_HandlingAndConversion()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_dto_" + Guid.NewGuid().ToString("N"));
            string tableDir = Path.Combine(tempDir, "dto_table");
            Directory.CreateDirectory(tableDir);

            try
            {
                var baseTime = new DateTimeOffset(2025, 3, 1, 9, 30, 0, TimeSpan.FromHours(8));
                var samples = new List<DateTimeOffsetSample>
                {
                    new() { Symbol = "sh.600000", Date = baseTime, OptionalTime = baseTime, Close = 20.0 },
                    new() { Symbol = "sh.600000", Date = baseTime.AddDays(1), OptionalTime = null, Close = 21.0 }
                };

                string parquetPath = Path.Combine(tableDir, "data.parquet");
                using (var fs = File.Create(parquetPath))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }

                // 测试包含 metadata nullable_fields 与 外部参数合并分支
                string metadataPath = Path.Combine(tableDir, "metadata.json");
                File.WriteAllText(metadataPath, """
                {
                    "table_id": "dto_table",
                    "category": "timeseries",
                    "format": "parquet",
                    "layout": "flat",
                    "partition": "none",
                    "schema": {
                        "Symbol": "string",
                        "Date": "datetime",
                        "OptionalTime": "datetime",
                        "Close": "double"
                    },
                    "nullable_fields": ["OptionalTime"]
                }
                """);

                // 外部也传入允许空值字段，触发合并逻辑
                var externalAllow = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Close" };
                using var source = TimeseriesSourceFactory.Create(tempDir, "dto_table", nullableFields: externalAllow);

                Assert.AreEqual(2, source.TradeDates.Count);
                double[] closes = new double[2];
                source.ReadSymbolSeries("sh.600000", "Close", 0, 2, closes);
                Assert.AreEqual(20.0, closes[0], 1e-4);
                Assert.AreEqual(21.0, closes[1], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        public class KlineTimeSample
        {
            public string Symbol { get; set; } = "sh.600000";
            public DateTime Date { get; set; }
            public double Close { get; set; }
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_IntradayMinuteData_MultiSymbolPreservesTimestampsAndValues()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_minute_multi_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var t0 = new DateTime(2025, 1, 6, 9, 30, 0);
                var t1 = new DateTime(2025, 1, 6, 9, 31, 0);
                var t2 = new DateTime(2025, 1, 6, 9, 32, 0);

                // sh.600000 全量 3 个分钟；sz.000001 在 t1 缺失停牌
                var samples = new List<KlineTimeSample>
                {
                    new() { Symbol = "sh.600000", Date = t0, Close = 10.0 },
                    new() { Symbol = "sh.600000", Date = t1, Close = 10.1 },
                    new() { Symbol = "sh.600000", Date = t2, Close = 10.2 },
                    new() { Symbol = "sz.000001", Date = t0, Close = 20.0 },
                    new() { Symbol = "sz.000001", Date = t2, Close = 20.2 }
                };

                string tableId = "ashare.kline.1m";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);
                using (var fs = File.Create(Path.Combine(tableDir, "data.parquet")))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }
                File.WriteAllText(Path.Combine(tableDir, "metadata.json"), $$"""
                {
                    "table_id": "{{tableId}}",
                    "category": "timeseries",
                    "format": "parquet",
                    "layout": "flat",
                    "partition": "none",
                    "schema": {
                        "Symbol": "string",
                        "Date": "datetime",
                        "Close": "double"
                    }
                }
                """);

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

                // 1. 验证全局多分钟时间轴完整保留
                Assert.AreEqual(3, source.TradeDates.Count);
                Assert.AreEqual(t0, source.TradeDates[0]);
                Assert.AreEqual(t1, source.TradeDates[1]);
                Assert.AreEqual(t2, source.TradeDates[2]);

                // 2. 验证多标的识别
                Assert.AreEqual(2, source.Symbols.Count);
                Assert.IsTrue(source.Symbols.Contains("sh.600000"));
                Assert.IsTrue(source.Symbols.Contains("sz.000001"));

                // 3. 验证第一只股票数值准确对齐
                double[] closes1 = new double[3];
                source.ReadSymbolSeries("sh.600000", "Close", 0, 3, closes1);
                Assert.AreEqual(10.0, closes1[0], 1e-4);
                Assert.AreEqual(10.1, closes1[1], 1e-4);
                Assert.AreEqual(10.2, closes1[2], 1e-4);

                // 4. 验证第二只股票在缺失分钟 t1 严格填补 NaN，其余数值严格对齐
                double[] closes2 = new double[3];
                source.ReadSymbolSeries("sz.000001", "Close", 0, 3, closes2);
                Assert.AreEqual(20.0, closes2[0], 1e-4);
                Assert.IsTrue(double.IsNaN(closes2[1]), "缺失分钟步必须严格填充为 NaN");
                Assert.AreEqual(20.2, closes2[2], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_DailyData_WithNoSpecialSuffix_BuildsCalendarAndValuesAccurately()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_daily_nosuffix_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var d0 = new DateTime(2025, 1, 6);
                var d1 = new DateTime(2025, 1, 7);
                var d2 = new DateTime(2025, 1, 8);

                var samples = new List<KlineTimeSample>
                {
                    new() { Symbol = "sh.600000", Date = d0, Close = 10.0 },
                    new() { Symbol = "sh.600000", Date = d1, Close = 11.0 },
                    new() { Symbol = "sh.600000", Date = d2, Close = 12.0 },
                    new() { Symbol = "sz.000001", Date = d0, Close = 20.0 },
                    new() { Symbol = "sz.000001", Date = d1, Close = 21.0 },
                    new() { Symbol = "sz.000001", Date = d2, Close = 22.0 }
                };

                // 表名使用无任何特化后缀的普通名称，严禁依赖 .1d 或 .day 猜测
                string tableId = "stock_daily_bars";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);
                using (var fs = File.Create(Path.Combine(tableDir, "data.parquet")))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }
                File.WriteAllText(Path.Combine(tableDir, "metadata.json"), $$"""
                {
                    "table_id": "{{tableId}}",
                    "category": "timeseries",
                    "format": "parquet",
                    "layout": "flat",
                    "partition": "none",
                    "schema": {
                        "Symbol": "string",
                        "Date": "datetime",
                        "Close": "double"
                    }
                }
                """);

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

                // 1. 验证交易日历与零点纯日期守恒
                Assert.AreEqual(3, source.TradeDates.Count);
                Assert.AreEqual(d0, source.TradeDates[0]);
                Assert.AreEqual(d1, source.TradeDates[1]);
                Assert.AreEqual(d2, source.TradeDates[2]);
                Assert.AreEqual(TimeSpan.Zero, source.TradeDates[0].TimeOfDay);

                // 2. 验证多标的数值准确提取
                double[] closes1 = new double[3];
                source.ReadSymbolSeries("sh.600000", "Close", 0, 3, closes1);
                Assert.AreEqual(10.0, closes1[0], 1e-4);
                Assert.AreEqual(11.0, closes1[1], 1e-4);
                Assert.AreEqual(12.0, closes1[2], 1e-4);

                double[] closes2 = new double[3];
                source.ReadSymbolSeries("sz.000001", "Close", 0, 3, closes2);
                Assert.AreEqual(20.0, closes2[0], 1e-4);
                Assert.AreEqual(21.0, closes2[1], 1e-4);
                Assert.AreEqual(22.0, closes2[2], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_InterleavedCrossSectionalData_ThrowsInvalidDataException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_interleaved_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var t0 = new DateTime(2025, 1, 6, 9, 30, 0);
                var t1 = new DateTime(2025, 1, 6, 9, 31, 0);

                // 物理数据按时间截面交叉存放 (ORDER BY datetime, symbol)，同一标的非连续交替出现
                var samples = new List<KlineTimeSample>
                {
                    // T0 截面
                    new() { Symbol = "sh.600000", Date = t0, Close = 10.0 },
                    new() { Symbol = "sz.000001", Date = t0, Close = 20.0 },

                    // T1 截面 (sh.600000 二次交替出现)
                    new() { Symbol = "sh.600000", Date = t1, Close = 10.1 },
                    new() { Symbol = "sz.000001", Date = t1, Close = 20.1 }
                };

                string tableId = "interleaved_kline";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);
                using (var fs = File.Create(Path.Combine(tableDir, "data.parquet")))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }
                File.WriteAllText(Path.Combine(tableDir, "metadata.json"), $$"""
                {
                    "table_id": "{{tableId}}",
                    "category": "timeseries",
                    "format": "parquet",
                    "layout": "flat",
                    "partition": "none",
                    "schema": {
                        "Symbol": "string",
                        "Date": "datetime",
                        "Close": "double"
                    }
                }
                """);

                // 必须在预先扫描阶段直接抛出 InvalidDataException 阻断加载，并提示上游 Python 修复命令
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    using var source = TimeseriesSourceFactory.Create(tempDir, tableId);
                });

                StringAssert.Contains(ex.Message, "非连续交错存储");
                StringAssert.Contains(ex.Message, "df.sort_values");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_UnsortedTimestamps_AutoSortsAndAlignsAccurately()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_unsorted_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var t0 = new DateTime(2025, 1, 6, 9, 30, 0);
                var t1 = new DateTime(2025, 1, 6, 9, 31, 0);
                var t2 = new DateTime(2025, 1, 6, 9, 32, 0);

                // 物理行记录乱序存放 (T2, T0, T1)
                var samples = new List<KlineTimeSample>
                {
                    new() { Symbol = "sh.600000", Date = t2, Close = 10.2 },
                    new() { Symbol = "sh.600000", Date = t0, Close = 10.0 },
                    new() { Symbol = "sh.600000", Date = t1, Close = 10.1 },
                };

                string tableId = "unsorted_kline";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);
                using (var fs = File.Create(Path.Combine(tableDir, "data.parquet")))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }
                File.WriteAllText(Path.Combine(tableDir, "metadata.json"), $$"""
                {
                    "table_id": "{{tableId}}",
                    "category": "timeseries",
                    "format": "parquet",
                    "layout": "flat",
                    "partition": "none",
                    "schema": {
                        "Symbol": "string",
                        "Date": "datetime",
                        "Close": "double"
                    }
                }
                """);

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);

                // 全局日历依然升序
                Assert.AreEqual(3, source.TradeDates.Count);
                Assert.AreEqual(t0, source.TradeDates[0]);
                Assert.AreEqual(t1, source.TradeDates[1]);
                Assert.AreEqual(t2, source.TradeDates[2]);

                // 提取时应自动原地排序自愈，精准对齐日历网格，绝无假 NaN
                double[] closes = new double[3];
                source.ReadSymbolSeries("sh.600000", "Close", 0, 3, closes);
                Assert.AreEqual(10.0, closes[0], 1e-4);
                Assert.AreEqual(10.1, closes[1], 1e-4);
                Assert.AreEqual(10.2, closes[2], 1e-4);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [TestMethod]
        public async Task ParquetTimeseriesSource_DuplicateConflictingTimestamps_ThrowsInvalidDataException()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_parquet_duplicate_conflict_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var t0 = new DateTime(2025, 1, 6, 9, 30, 0);

                // 物理行记录在同一时间戳存在两条冲突价格 (10.0 vs 10.5)
                var samples = new List<KlineTimeSample>
                {
                    new() { Symbol = "sh.600000", Date = t0, Close = 10.0 },
                    new() { Symbol = "sh.600000", Date = t0, Close = 10.5 },
                };

                string tableId = "duplicate_kline";
                string tableDir = Path.Combine(tempDir, tableId);
                Directory.CreateDirectory(tableDir);
                using (var fs = File.Create(Path.Combine(tableDir, "data.parquet")))
                {
                    await ParquetSerializer.SerializeAsync(samples, fs);
                }
                File.WriteAllText(Path.Combine(tableDir, "metadata.json"), $$"""
                {
                    "table_id": "{{tableId}}",
                    "category": "timeseries",
                    "format": "parquet",
                    "layout": "flat",
                    "partition": "none",
                    "schema": {
                        "Symbol": "string",
                        "Date": "datetime",
                        "Close": "double"
                    }
                }
                """);

                using var source = TimeseriesSourceFactory.Create(tempDir, tableId);
                double[] closes = new double[1];

                // 必须抛出 InvalidDataException 阻断脏数据，严禁隐式吞掉或假覆盖
                var ex = Assert.ThrowsException<InvalidDataException>(() =>
                {
                    source.ReadSymbolSeries("sh.600000", "Close", 0, 1, closes);
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
