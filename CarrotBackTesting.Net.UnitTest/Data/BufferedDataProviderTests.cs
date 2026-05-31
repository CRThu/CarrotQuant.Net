using System;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
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
    }
}
