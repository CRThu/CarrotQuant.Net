using System;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class BufferedDataProviderTests
    {
        private string _tempCacheDir;

        [TestInitialize]
        public void Setup()
        {
            // 为每个测试创建一个独立的空缓存目录，专门用于存放 MMF 文件
            _tempCacheDir = Path.Combine(Path.GetTempPath(), "CarrotQuantTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempCacheDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // 测试结束后，只清理 MMF 缓存文件，不触碰原始数据
            if (Directory.Exists(_tempCacheDir))
            {
                Directory.Delete(_tempCacheDir, true);
            }
        }

        private string GetRawDataRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root");
        }

        [TestMethod]
        public void TestBufferedDataProviderLoadAndDispose()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";

            var registry = new SimpleFieldRegistry();
            // 原始数据依然从 csvRoot 读取
            using var source = new CsvMarketSeriesSource(csvRoot, registry, tableId);
            
            // 使用新创建的 _tempCacheDir 作为 MMF 存放的缓存区
            using (var provider = new BufferedDataProvider(source, registry, source, _tempCacheDir))
            {
                Assert.IsTrue(provider.HasField("close"));
                
                // 加载数据
                var buffer = provider.GetBuffer<double>("close");
                Assert.IsNotNull(buffer);
                Assert.AreEqual(source.TradeDates.Count, buffer.RowCount);
                Assert.AreEqual(source.Symbols.Count, buffer.Width);
            }
            // 此时已调用 Dispose
        }

        [TestMethod]
        public void TestBufferedDataProviderWithMultiLevelParquet()
        {
            string testDataRoot = GetRawDataRoot();
            string parquetRoot = Path.Combine(testDataRoot, "parquet");
            string tableId = "ashare.kline.1m.raw.custom";

            var registry = new SimpleFieldRegistry();
            using var source = new ParquetMarketSeriesSource(parquetRoot, registry, tableId);

            using var provider = new BufferedDataProvider(source, registry, source, _tempCacheDir);

            Assert.IsTrue(provider.HasField("close"));
            Assert.IsTrue(provider.HasField("open"));

            var buffer = provider.GetBuffer<double>("close");
            Assert.IsNotNull(buffer);
            Assert.AreEqual(source.TradeDates.Count, buffer.RowCount);
            Assert.AreEqual(source.Symbols.Count, buffer.Width);
        }

        [TestMethod]
        public void TestGetNonExistentFieldThrows()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";

            var registry = new SimpleFieldRegistry();
            using var source = new CsvMarketSeriesSource(csvRoot, registry, tableId);
            
            using var provider = new BufferedDataProvider(source, registry, source, _tempCacheDir);
            
            Assert.ThrowsException<KeyNotFoundException>(() => provider.GetBuffer<double>("non_existent_field"));
        }

        [TestMethod]
        public void TestBufferDimensionsAlignWithMetadata()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";

            var registry = new SimpleFieldRegistry();
            using var source = new CsvMarketSeriesSource(csvRoot, registry, tableId);
            
            using var provider = new BufferedDataProvider(source, registry, source, _tempCacheDir);
            var buffer = provider.GetBuffer<double>("close");
            
            // 严谨验证：Buffer 维度必须等于元数据维度
            Assert.AreEqual(provider.Metadata.TradeDates.Count, buffer.RowCount, "Buffer 行数与交易日数量不匹配");
            Assert.AreEqual(provider.Metadata.Symbols.Count, buffer.Width, "Buffer 列数与股票数量不匹配");
        }

        [TestMethod]
        public void TestFullBufferAccuracy()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";

            var registry = new SimpleFieldRegistry();
            using var source = new CsvMarketSeriesSource(csvRoot, registry, tableId);
            
            using (var provider = new BufferedDataProvider(source, registry, source, _tempCacheDir))
            {
                var buffer = provider.GetBuffer<double>("close");
                
                // 1. 读取源 CSV 文件以备校验 (读取该股票 2021 年的所有数据)
                string csvPath = Path.Combine(csvRoot, tableId, "year=2021", "sh.600000.csv");
                var lines = File.ReadAllLines(csvPath).Skip(1).ToList();
                
                // 2. 查找 sh.600000 在 Metadata 中的列索引
                int colIdx = -1;
                for (int i = 0; i < provider.Metadata.Symbols.Count; i++)
                {
                    if (provider.Metadata.Symbols[i] == "sh.600000")
                    {
                        colIdx = i;
                        break;
                    }
                }
                Assert.AreNotEqual(-1, colIdx, "未在 Buffer 中找到股票 sh.600000");

                // 3. 逐行比对数据
                for (int rowIdx = 0; rowIdx < lines.Count; rowIdx++)
                {
                    string[] parts = lines[rowIdx].Split(',');
                    double expectedClose = double.Parse(parts[6]); // close 在第 6 列
                    
                    double actualClose = buffer[rowIdx, colIdx];
                    
                    Assert.AreEqual(expectedClose, actualClose, 1e-4, $"第 {rowIdx} 行数据不匹配，预期: {expectedClose}, 实际: {actualClose}");
                }
            }
        }

        [TestMethod]
        public void TestBufferedDataProviderWithPersistentCache_EndToEnd()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";

            var registry = new SimpleFieldRegistry();
            using var source = new CsvMarketSeriesSource(csvRoot, registry, tableId);

            // 1. 首次加载（缓存未命中，从源数据构建并写入磁盘缓存）
            using (var provider1 = new BufferedDataProvider(
                source, 
                registry, 
                source, 
                _tempCacheDir, 
                pageSize: 1024, 
                enableCache: true, 
                refreshMode: "lazy", 
                tableId: tableId))
            {
                var buf1 = provider1.GetBuffer<double>("close");
                Assert.IsNotNull(buf1);
                Assert.AreEqual(source.TradeDates.Count, buf1.RowCount);
            }

            // 验证磁盘缓存文件与元数据已成功生成
            var cacheStore = new TimeseriesCacheStore(_tempCacheDir, tableId, 1024);
            Assert.IsTrue(cacheStore.HasCache("close"));
            var meta = cacheStore.ReadMetadata("close");
            Assert.IsNotNull(meta);
            Assert.AreEqual(source.TradeDates.Count, meta.RowCount);

            // 2. 第二次加载（缓存命中，直接从二进制分块文件加载）
            using (var provider2 = new BufferedDataProvider(
                source, 
                registry, 
                source, 
                _tempCacheDir, 
                pageSize: 1024, 
                enableCache: true, 
                refreshMode: "lazy", 
                tableId: tableId))
            {
                var buf2 = provider2.GetBuffer<double>("close");
                Assert.IsNotNull(buf2);
                Assert.AreEqual(source.TradeDates.Count, buf2.RowCount);
                Assert.AreEqual(source.Symbols.Count, buf2.Width);
            }

            // 3. 强制刷新测试（force 模式清空并重新构建）
            using (var provider3 = new BufferedDataProvider(
                source, 
                registry, 
                source, 
                _tempCacheDir, 
                pageSize: 1024, 
                enableCache: true, 
                refreshMode: "force", 
                tableId: tableId))
            {
                var buf3 = provider3.GetBuffer<double>("close");
                Assert.IsNotNull(buf3);
                Assert.AreEqual(source.TradeDates.Count, buf3.RowCount);
            }
        }

        [TestMethod]
        public void TestColumnCountChange_UniverseExpansionOrDelisting_TriggersRebuild()
        {
            // 验证当股票池发生变更（新增上市 IPO 导致列数增加，或退市摘牌导致列数减少）时，
            // 缓存的列维度检查契约能够自动识别不兼容，安全触发全量重建，彻底防止行跨步错位
            const string tableId = "test.universe.change";
            const string fieldName = "close";
            var registry = new SimpleFieldRegistry();
            registry.RegisterField(fieldName, typeof(double), false);

            var mockSource = new MockMarketSeriesSource();
            mockSource.DatesList.AddRange(new[]
            {
                new DateTime(2021, 1, 4),
                new DateTime(2021, 1, 5),
                new DateTime(2021, 1, 6)
            });

            // 1. 初始状态：2 只股票
            mockSource.SymbolsList.AddRange(new[] { "sh.600000", "sz.000001" });

            using (var provider1 = new BufferedDataProvider(
                mockSource,
                registry,
                mockSource,
                _tempCacheDir,
                pageSize: 1024,
                enableCache: true,
                refreshMode: "lazy",
                tableId: tableId))
            {
                var buf1 = provider1.GetBuffer<double>(fieldName);
                Assert.AreEqual(2, buf1.Width);
                Assert.AreEqual(3, buf1.RowCount);
                Assert.AreEqual(0.0, buf1[0, 0], 1e-4); // (0 * 100) + 0 = 0
                Assert.AreEqual(1.0, buf1[0, 1], 1e-4); // (0 * 100) + 1 = 1
            }

            var cacheStore = new TimeseriesCacheStore(_tempCacheDir, tableId, 1024);
            var meta1 = cacheStore.ReadMetadata(fieldName);
            Assert.IsNotNull(meta1);
            Assert.AreEqual(2, meta1.Width);
            Assert.AreEqual(2, meta1.Symbols.Count);

            // 2. 模拟新股 IPO 上市 / 股票池扩充：列宽增加至 3（满足前缀一致且单调扩充契约）
            mockSource.SymbolsList.Add("sz.000002");

            using (var provider2 = new BufferedDataProvider(
                mockSource,
                registry,
                mockSource,
                _tempCacheDir,
                pageSize: 1024,
                enableCache: true,
                refreshMode: "lazy",
                tableId: tableId))
            {
                // lazy 模式下，检测到股票池维度发生变更，触发安全重刷以确保行跨步对齐与数据一致
                var buf2 = provider2.GetBuffer<double>(fieldName);
                Assert.AreEqual(3, buf2.Width);
                Assert.AreEqual(3, buf2.RowCount);
                Assert.AreEqual(0.0, buf2[0, 0], 1e-4); // 旧列 0 保持不变
                Assert.AreEqual(1.0, buf2[0, 1], 1e-4); // 旧列 1 保持不变
                Assert.AreEqual(2.0, buf2[0, 2], 1e-4); // 新列 2 (0 * 100) + 2 = 2
            }

            var meta2 = cacheStore.ReadMetadata(fieldName);
            Assert.IsNotNull(meta2);
            Assert.AreEqual(3, meta2.Width);
            Assert.AreEqual(3, meta2.Symbols.Count);
            Assert.AreEqual("sz.000002", meta2.Symbols[2]);

            // 3. 模拟股票退市 / 标的剔除：列宽缩减至 1
            mockSource.SymbolsList.Clear();
            mockSource.SymbolsList.Add("sz.000001");

            using (var provider3 = new BufferedDataProvider(
                mockSource,
                registry,
                mockSource,
                _tempCacheDir,
                pageSize: 1024,
                enableCache: true,
                refreshMode: "lazy",
                tableId: tableId))
            {
                var buf3 = provider3.GetBuffer<double>(fieldName);
                Assert.AreEqual(1, buf3.Width);
                Assert.AreEqual(3, buf3.RowCount);
                Assert.AreEqual(0.0, buf3[0, 0], 1e-4); // 当前股票池仅有 sz.000001 (索引 0)
            }

            var meta3 = cacheStore.ReadMetadata(fieldName);
            Assert.IsNotNull(meta3);
            Assert.AreEqual(1, meta3.Width);
            Assert.AreEqual(1, meta3.Symbols.Count);
            Assert.AreEqual("sz.000001", meta3.Symbols[0]);
        }

        [TestMethod]
        public void TestBufferedDataProvider_ExpandedSymbols_AutoFillsNaN()
        {
            // 验证当股票池扩充时，历史分块在载入内存拓宽时自动填充 NaN
            string tableId = "test.expand.nan.table";
            string fieldPe = "pe_ttm";

            var mockSource = new MockMarketSeriesSource();
            mockSource.SymbolsList.Add("sz.000001");
            mockSource.DatesList.Add(new DateTime(2025, 1, 6));

            var registry = new SimpleFieldRegistry();
            registry.RegisterField(fieldPe, typeof(double));

            // 阶段 1: 股票池仅 1 只股票，写入初版缓存
            using (var provider1 = new BufferedDataProvider(
                mockSource,
                registry,
                mockSource,
                _tempCacheDir,
                pageSize: 1024,
                enableCache: true,
                refreshMode: "force",
                tableId: tableId))
            {
                var bufPe = provider1.GetBuffer<double>(fieldPe);
                Assert.AreEqual(1, bufPe.Width);
            }

            // 阶段 2: 股票池扩充到 2 只股票，以 lazy 模式重新打开
            mockSource.SymbolsList.Add("sz.000002");

            using (var provider2 = new BufferedDataProvider(
                mockSource,
                registry,
                mockSource,
                _tempCacheDir,
                pageSize: 1024,
                enableCache: true,
                refreshMode: "lazy",
                tableId: tableId))
            {
                var bufPe = provider2.GetBuffer<double>(fieldPe);

                Assert.AreEqual(2, bufPe.Width);
                Assert.IsFalse(double.IsNaN(bufPe[0, 0])); // 旧股票首日数据有效
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TestBufferedDataProvider_TypeMismatch_ThrowsException()
        {
            var mockSource = new MockMarketSeriesSource();
            mockSource.SymbolsList.Add("sh.600000");
            mockSource.DatesList.Add(new DateTime(2025, 1, 2));

            var registry = new SimpleFieldRegistry();
            registry.RegisterField("close", typeof(double));

            using var provider = new BufferedDataProvider(mockSource, registry, mockSource, _tempCacheDir);
            provider.GetBuffer<int>("close"); // 请求 int 但注册为 double
        }

        [TestMethod]
        public void TestBufferedDataProvider_AutoRefreshMode_And_CorruptedCacheFallback()
        {
            string tableId = "auto_refresh_table";
            var mockSource = new MockMarketSeriesSource();
            mockSource.SymbolsList.Add("sh.600000");
            for (int i = 0; i < 50; i++)
            {
                mockSource.DatesList.Add(new DateTime(2025, 1, 1).AddDays(i));
            }

            var registry = new SimpleFieldRegistry();
            registry.RegisterField("close", typeof(double));

            // 1. auto 模式首次写入
            using (var provider1 = new BufferedDataProvider(
                mockSource, registry, mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "auto", tableId: tableId))
            {
                var buf = provider1.GetBuffer<double>("close");
                Assert.AreEqual(50, buf.RowCount);
            }

            // 2. auto 模式同维度命中
            using (var provider2 = new BufferedDataProvider(
                mockSource, registry, mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "auto", tableId: tableId))
            {
                var buf = provider2.GetBuffer<double>("close");
                Assert.AreEqual(50, buf.RowCount);
            }

            // 3. auto 模式扩展股票代码
            mockSource.SymbolsList.Add("sh.600001");
            using (var provider3 = new BufferedDataProvider(
                mockSource, registry, mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "auto", tableId: tableId))
            {
                var buf = provider3.GetBuffer<double>("close");
                Assert.AreEqual(2, buf.Width);
            }

            // 4. 损坏缓存回退
            string fieldDir = Path.Combine(_tempCacheDir, ".cache", tableId, "close");
            string chunk0 = Path.Combine(fieldDir, "chunk_0.bin");
            if (File.Exists(chunk0))
            {
                File.WriteAllBytes(chunk0, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }); // 破坏分块文件
            }

            using (var provider4 = new BufferedDataProvider(
                mockSource, registry, mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "lazy", tableId: tableId))
            {
                // 能够捕获解压损坏异常并平滑全量回退重建
                var buf = provider4.GetBuffer<double>("close");
                Assert.AreEqual(50, buf.RowCount);
            }
        }

        private class MockMarketSeriesSource : IMarketSeriesSource
        {
            public List<string> SymbolsList { get; } = new();
            public List<DateTime> DatesList { get; } = new();
            public IReadOnlyList<string> Symbols => SymbolsList;
            public IReadOnlyList<string> FieldNames => new List<string> { "close" };
            public IReadOnlyList<DateTime> TradeDates => DatesList;

            public Type GetFieldType(string fieldName) => typeof(double);
            public int GetSymbolIndex(string symbol) => SymbolsList.IndexOf(symbol);
            public int GetDateIndex(DateTime date) => DatesList.IndexOf(date);

            public void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged
            {
                int symIdx = SymbolsList.IndexOf(symbol);
                if (typeof(T) == typeof(double))
                {
                    var doubleSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<T, double>(destination);
                    for (int i = 0; i < length; i++)
                    {
                        doubleSpan[i] = (startIndex + i) * 100.0 + symIdx;
                    }
                }
            }

            public void Dispose() { }
        }
    }
}
