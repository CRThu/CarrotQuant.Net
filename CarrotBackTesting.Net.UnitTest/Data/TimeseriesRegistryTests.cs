using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class TimeseriesRegistryTests
    {
        private string _tempCacheDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempCacheDir = Path.Combine(Path.GetTempPath(), "CarrotQuantTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempCacheDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
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
        public void TestTimeseriesRegistryLoadAndDispose()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";
            using var source = new CsvTimeseriesSource(csvRoot, tableId);
            
            using (var provider = new TimeseriesRegistry(source, _tempCacheDir))
            {
                Assert.IsTrue(provider.HasField("close"));
                
                var buffer = provider.GetMatrix<double>("close");
                Assert.IsNotNull(buffer);
                Assert.AreEqual(source.TradeDates.Count, buffer.RowCount);
                Assert.AreEqual(source.Symbols.Count, buffer.Width);
            }
        }

        [TestMethod]
        public void TestTimeseriesRegistryWithMultiLevelParquet()
        {
            string testDataRoot = GetRawDataRoot();
            string parquetRoot = Path.Combine(testDataRoot, "parquet");
            string tableId = "ashare.kline.1m.raw.custom";
            using var source = new ParquetTimeseriesSource(parquetRoot, tableId);

            using var provider = new TimeseriesRegistry(source, _tempCacheDir);

            Assert.IsTrue(provider.HasField("close"));
            Assert.IsTrue(provider.HasField("open"));

            var buffer = provider.GetMatrix<double>("close");
            Assert.IsNotNull(buffer);
            Assert.AreEqual(source.TradeDates.Count, buffer.RowCount);
            Assert.AreEqual(source.Symbols.Count, buffer.Width);
        }

        [TestMethod]
        public void TestGetNonExistentFieldThrows()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";
            using var source = new CsvTimeseriesSource(csvRoot, tableId);
            
            using var provider = new TimeseriesRegistry(source, _tempCacheDir);
            
            Assert.ThrowsException<KeyNotFoundException>(() => provider.GetMatrix<double>("non_existent_field"));
        }

        [TestMethod]
        public void TestBufferDimensionsAlignWithMetadata()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";
            using var source = new CsvTimeseriesSource(csvRoot, tableId);
            
            using var provider = new TimeseriesRegistry(source, _tempCacheDir);
            var buffer = provider.GetMatrix<double>("close");
            
            Assert.AreEqual(provider.Metadata.TradeDates.Count, buffer.RowCount, "Buffer 行数与交易日数量不匹配");
            Assert.AreEqual(provider.Metadata.Symbols.Count, buffer.Width, "Buffer 列数与股票数量不匹配");
        }

        [TestMethod]
        public void TestFullBufferAccuracy()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";
            using var source = new CsvTimeseriesSource(csvRoot, tableId);
            
            using (var provider = new TimeseriesRegistry(source, _tempCacheDir))
            {
                var buffer = provider.GetMatrix<double>("close");
                
                string csvPath = Path.Combine(csvRoot, tableId, "year=2021", "sh.600000.csv");
                var lines = File.ReadAllLines(csvPath).Skip(1).ToList();
                
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

                for (int rowIdx = 0; rowIdx < lines.Count; rowIdx++)
                {
                    string[] parts = lines[rowIdx].Split(',');
                    double expectedClose = double.Parse(parts[6]);
                    double actualClose = buffer[rowIdx, colIdx];
                    
                    Assert.AreEqual(expectedClose, actualClose, 1e-4, $"第 {rowIdx} 行数据不匹配，预期: {expectedClose}, 实际: {actualClose}");
                }
            }
        }

        [TestMethod]
        public void TestTimeseriesRegistryWithPersistentCache_EndToEnd()
        {
            string csvRoot = Path.Combine(GetRawDataRoot(), "csv");
            string tableId = "ashare.kline.1d.raw.baostock";
            using var source = new CsvTimeseriesSource(csvRoot, tableId);

            // 1. 首次加载（缓存未命中，从源数据构建并写入磁盘缓存）
            using (var provider1 = new TimeseriesRegistry(
                source, _tempCacheDir, 
                pageSize: 1024, 
                enableCache: true, 
                refreshMode: "lazy", 
                tableId: tableId))
            {
                var buf1 = provider1.GetMatrix<double>("close");
                Assert.IsNotNull(buf1);
                Assert.AreEqual(source.TradeDates.Count, buf1.RowCount);
            }

            var store = new TimeseriesCache(_tempCacheDir, tableId, 1024);
            Assert.IsTrue(store.HasField("close"));
            var meta = store.ReadMetadata("close");
            Assert.IsNotNull(meta);
            Assert.AreEqual(source.TradeDates.Count, meta.RowCount);

            // 2. 第二次加载（缓存命中，直接从二进制分块文件加载）
            using (var provider2 = new TimeseriesRegistry(
                source, _tempCacheDir, 
                pageSize: 1024, 
                enableCache: true, 
                refreshMode: "lazy", 
                tableId: tableId))
            {
                var buf2 = provider2.GetMatrix<double>("close");
                Assert.IsNotNull(buf2);
                Assert.AreEqual(source.TradeDates.Count, buf2.RowCount);
                Assert.AreEqual(source.Symbols.Count, buf2.Width);
            }

            // 3. 强制刷新测试（force 模式清空并重新构建）
            using (var provider3 = new TimeseriesRegistry(
                source, _tempCacheDir, 
                pageSize: 1024, 
                enableCache: true, 
                refreshMode: "force", 
                tableId: tableId))
            {
                var buf3 = provider3.GetMatrix<double>("close");
                Assert.IsNotNull(buf3);
                Assert.AreEqual(source.TradeDates.Count, buf3.RowCount);
            }
        }

        [TestMethod]
        public void TestColumnCountChange_UniverseExpansionOrDelisting_TriggersRebuild()
        {
            const string tableId = "test.universe.change";
            const string fieldName = "close";

            var mockSource = new MockTimeseriesSource();
            mockSource.DatesList.AddRange(new[]
            {
                new DateTime(2021, 1, 4),
                new DateTime(2021, 1, 5),
                new DateTime(2021, 1, 6)
            });

            mockSource.SymbolsList.AddRange(new[] { "sh.600000", "sz.000001" });

            using (var provider1 = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 1024,
                enableCache: true,
                refreshMode: "lazy",
                tableId: tableId))
            {
                var buf1 = provider1.GetMatrix<double>(fieldName);
                Assert.AreEqual(2, buf1.Width);
                Assert.AreEqual(3, buf1.RowCount);
                Assert.AreEqual(0.0, buf1[0, 0], 1e-4);
                Assert.AreEqual(1.0, buf1[0, 1], 1e-4);
            }

            var store = new TimeseriesCache(_tempCacheDir, tableId, 1024);
            var meta1 = store.ReadMetadata(fieldName);
            Assert.IsNotNull(meta1);
            Assert.AreEqual(2, meta1.Width);
            Assert.AreEqual(2, meta1.Symbols.Count);

            // 扩充至 3 只股票
            mockSource.SymbolsList.Add("sz.000002");

            using (var provider2 = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 1024,
                enableCache: true,
                refreshMode: "lazy",
                tableId: tableId))
            {
                var buf2 = provider2.GetMatrix<double>(fieldName);
                Assert.AreEqual(3, buf2.Width);
                Assert.AreEqual(3, buf2.RowCount);
                Assert.AreEqual(0.0, buf2[0, 0], 1e-4);
                Assert.AreEqual(1.0, buf2[0, 1], 1e-4);
                Assert.AreEqual(2.0, buf2[0, 2], 1e-4);
            }

            var meta2 = store.ReadMetadata(fieldName);
            Assert.IsNotNull(meta2);
            Assert.AreEqual(3, meta2.Width);
            Assert.AreEqual(3, meta2.Symbols.Count);
            Assert.AreEqual("sz.000002", meta2.Symbols[2]);
        }

        [TestMethod]
        public void TestTimeseriesRegistry_ExpandedSymbols_AutoFillsNaN()
        {
            string tableId = "test.expand.nan.table";
            string fieldPe = "pe_ttm";

            var mockSource = new MockTimeseriesSource();
            mockSource.FieldsList.Add(fieldPe);
            mockSource.SymbolsList.Add("sz.000001");
            mockSource.DatesList.Add(new DateTime(2025, 1, 6));

            using (var provider1 = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 1024,
                enableCache: true,
                refreshMode: "force",
                tableId: tableId))
            {
                var bufPe = provider1.GetMatrix<double>(fieldPe);
                Assert.AreEqual(1, bufPe.Width);
            }

            mockSource.SymbolsList.Add("sz.000002");

            using (var provider2 = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 1024,
                enableCache: true,
                refreshMode: "lazy",
                tableId: tableId))
            {
                var bufPe = provider2.GetMatrix<double>(fieldPe);
                Assert.AreEqual(2, bufPe.Width);
                Assert.IsFalse(double.IsNaN(bufPe[0, 0]));
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TestTimeseriesRegistry_TypeMismatch_ThrowsException()
        {
            var mockSource = new MockTimeseriesSource();
            mockSource.SymbolsList.Add("sh.600000");
            mockSource.DatesList.Add(new DateTime(2025, 1, 2));

            using var provider = new TimeseriesRegistry(mockSource, _tempCacheDir);
            provider.GetMatrix<int>("close");
        }

        [TestMethod]
        public void TestTimeseriesRegistry_Commit_SyncsActiveChunkToDisk()
        {
            string tableId = "commit_test_table";
            var mockSource = new MockTimeseriesSource();
            mockSource.SymbolsList.Add("sh.600000");
            mockSource.SymbolsList.Add("sz.000001");
            mockSource.DatesList.Add(new DateTime(2025, 1, 2));

            // 1. 初始化 provider 并载入
            using (var provider = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "lazy", tableId: tableId))
            {
                var buffer = provider.GetMatrix<double>("close");
                Assert.AreEqual(1, buffer.RowCount);

                // 通过 Manager 追加实时行
                var manager = provider.GetCacheManager<double>("close");
                double[] newRow = new double[] { 88.8, 99.9 };
                manager.AppendRow(newRow);

                Assert.IsTrue(manager.IsDirty);
                Assert.AreEqual(2, manager.TotalRows);

                // 2. 调用 Provider 统一 Commit
                provider.Commit();
                Assert.IsFalse(manager.IsDirty);
            }

            // 3. 重新打开一个 Provider，验证已落盘的 Cache 中数据准确
            mockSource.DatesList.Add(new DateTime(2025, 1, 3));
            using (var providerRestart = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "lazy", tableId: tableId))
            {
                var buffer = providerRestart.GetMatrix<double>("close");
                Assert.AreEqual(2, buffer.RowCount);
                Assert.AreEqual(88.8, buffer[1, 0], 1e-4);
                Assert.AreEqual(99.9, buffer[1, 1], 1e-4);
            }
        }

        [TestMethod]
        public void TestTimeseriesRegistry_AutoRefreshMode_And_CorruptedCacheFallback()
        {
            string tableId = "auto_refresh_table";
            var mockSource = new MockTimeseriesSource();
            mockSource.SymbolsList.Add("sh.600000");
            for (int i = 0; i < 50; i++)
            {
                mockSource.DatesList.Add(new DateTime(2025, 1, 1).AddDays(i));
            }

            // 1. auto 模式首次写入
            using (var provider1 = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "auto", tableId: tableId))
            {
                var buf = provider1.GetMatrix<double>("close");
                Assert.AreEqual(50, buf.RowCount);
            }

            // 2. auto 模式同维度命中
            using (var provider2 = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "auto", tableId: tableId))
            {
                var buf = provider2.GetMatrix<double>("close");
                Assert.AreEqual(50, buf.RowCount);
            }

            // 3. auto 模式扩展股票代码
            mockSource.SymbolsList.Add("sh.600001");
            using (var provider3 = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "auto", tableId: tableId))
            {
                var buf = provider3.GetMatrix<double>("close");
                Assert.AreEqual(2, buf.Width);
            }

            // 4. 损坏缓存回退
            string fieldDir = Path.Combine(_tempCacheDir, ".cache", tableId, "close");
            string chunk0 = Path.Combine(fieldDir, "chunk_0.bin");
            if (File.Exists(chunk0))
            {
                File.WriteAllBytes(chunk0, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            }

            using (var provider4 = new TimeseriesRegistry(
                mockSource, _tempCacheDir,
                pageSize: 64, enableCache: true, refreshMode: "lazy", tableId: tableId))
            {
                var buf = provider4.GetMatrix<double>("close");
                Assert.AreEqual(50, buf.RowCount);
            }
        }

        [TestMethod]
        public void TestTimeseriesRegistry_SingleSymbol_DegenerateAndGetSeries()
        {
            var mockSource = new MockTimeseriesSource();
            mockSource.SymbolsList.Add("BTCUSDT");
            for (int i = 0; i < 100; i++)
            {
                mockSource.DatesList.Add(new DateTime(2025, 1, 1).AddDays(i));
            }

            // 使用精简构造函数（无需重复传 source）
            using var provider = new TimeseriesRegistry(mockSource, _tempCacheDir);

            // 1. 验证通过一维扩展获取默认标的（第 0 列）全量时间序列
            var defaultSeries = provider.GetSeries<double>("close");
            Assert.AreEqual(100, defaultSeries.Length);

            for (int r = 0; r < 100; r++)
            {
                Assert.AreEqual(r * 100.0, defaultSeries[r]);
            }

            // 2. 验证通过代码名称获取指定标的一维序列
            var namedSeries = provider.GetSeries<double>("close", "BTCUSDT");
            Assert.AreEqual(100, namedSeries.Length);
            Assert.AreEqual(defaultSeries[50], namedSeries[50]);

            // 3. 验证查询不存在的代码抛出 KeyNotFoundException
            Assert.ThrowsException<KeyNotFoundException>(() =>
            {
                provider.GetSeries<double>("close", "ETHUSDT");
            });
        }

        [TestMethod]
        public void TestTimeseriesRegistry_MultiThreaded_ConcurrentRead_LockFree()
        {
            var mockSource = new MockTimeseriesSource();
            for (int s = 0; s < 10; s++)
            {
                mockSource.SymbolsList.Add($"SYM_{s:D2}");
            }
            for (int i = 0; i < 200; i++)
            {
                mockSource.DatesList.Add(new DateTime(2025, 1, 1).AddDays(i));
            }

            using var provider = new TimeseriesRegistry(mockSource, _tempCacheDir);
            var buffer = provider.GetMatrix<double>("close");

            // 并发模拟：20 个线程、10,000 次高频并发随机访问同一个只读二维矩阵
            System.Threading.Tasks.Parallel.For(0, 10000, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 20 }, iter =>
            {
                int r = iter % 200;
                int c = iter % 10;
                double val = buffer[r, c];
                double expected = r * 100.0 + c;
                Assert.AreEqual(expected, val);
            });
        }

        [TestMethod]
        public void TestTimeseriesRegistry_GetCacheManager_Operations()
        {
            var mockSource = new MockTimeseriesSource();
            mockSource.SymbolsList.Add("000001.SZ");
            mockSource.DatesList.Add(new DateTime(2025, 1, 1));

            using var provider = new TimeseriesRegistry(mockSource, _tempCacheDir);

            // 1. 正确获取 Manager
            var manager1 = provider.GetCacheManager<double>("close");
            var manager2 = provider.GetCacheManager<double>("close");
            Assert.IsNotNull(manager1);
            Assert.AreSame(manager1, manager2);

            // 2. 类型不匹配抛出 InvalidOperationException
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                provider.GetCacheManager<float>("close");
            });

            // 3. 字段不存在抛出 KeyNotFoundException
            Assert.ThrowsException<KeyNotFoundException>(() =>
            {
                provider.GetCacheManager<double>("non_existent");
            });
        }

        [TestMethod]
        public void TimeseriesRegistry_ShouldImplementBothReadAndWriteContracts()
        {
            var mockSource = new MockTimeseriesSource();
            using var timeseries = new TimeseriesRegistry(mockSource, _tempCacheDir);

            Assert.IsInstanceOfType(timeseries, typeof(ITimeseriesRegistry));
            Assert.IsInstanceOfType(timeseries, typeof(ITimeseriesWriter));
        }

        private class MockTimeseriesSource : ITimeseriesSource
        {
            public List<string> SymbolsList { get; } = new();
            public List<DateTime> DatesList { get; } = new();
            public List<string> FieldsList { get; } = new() { "close" };
            public IReadOnlyList<string> Symbols => SymbolsList;
            public IReadOnlyList<string> FieldNames => FieldsList;
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

