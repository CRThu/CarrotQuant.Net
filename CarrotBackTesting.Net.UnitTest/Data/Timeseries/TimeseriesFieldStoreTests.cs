using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class TimeseriesFieldStoreTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CarrotFieldStoreTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        [TestMethod]
        public void TestConstructor_ValidatesParameters()
        {
            var indexStore = new TimeseriesIndexStore(_tempDir, "t1");
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesFieldStore("", "t1", "close", 1024, "lz4", indexStore));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesFieldStore(_tempDir, "", "close", 1024, "lz4", indexStore));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesFieldStore(_tempDir, "t1", "", 1024, "lz4", indexStore));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesFieldStore(_tempDir, "t1", "close", 1000, "lz4", indexStore)); // 非 2 的幂
            Assert.ThrowsException<ArgumentNullException>(() => new TimeseriesFieldStore(_tempDir, "t1", "close", 1024, "lz4", null!));

            var fieldStore = new TimeseriesFieldStore(_tempDir, "t1", "close", 1024, "lz4", indexStore);
            Assert.AreEqual("close", fieldStore.FieldName);
            Assert.AreEqual(1024, fieldStore.PageSize);
            Assert.AreEqual("lz4", fieldStore.Compression);
            Assert.AreSame(indexStore, fieldStore.IndexStore);
        }

        [TestMethod]
        public void TestWriteAndLoadMatrix_WithFieldStore()
        {
            var indexStore = new TimeseriesIndexStore(_tempDir, "t1");
            var fieldStore = new TimeseriesFieldStore(_tempDir, "t1", "open", 512, "lz4", indexStore);

            int width = 5;
            int totalRows = 1200; // 512 + 512 + 176
            var symbols = new[] { "s1", "s2", "s3", "s4", "s5" };
            var baseTime = new MarketTime(2024, 1, 1);
            var dates = new List<MarketTime>(totalRows);
            for (int i = 0; i < totalRows; i++) dates.Add(baseTime.AddDays(i));

            using var matrix = new ChunkMatrix2D<float>(width, 512, totalRows);
            for (int r = 0; r < totalRows; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    matrix[r, c] = r * 10f + c;
                }
            }

            // 写入
            fieldStore.WriteMatrix(matrix, symbols, dates);
            Assert.IsTrue(fieldStore.HasField());

            // 验证表级索引也被同步写入
            Assert.AreEqual(5, indexStore.ReadSymbols().Count);
            Assert.AreEqual(1200, indexStore.ReadTradeDates().Count);

            // 从 fieldStore 加载
            using var loaded = fieldStore.LoadMatrix<float>();
            Assert.AreEqual(totalRows, loaded.RowCount);
            Assert.AreEqual(width, loaded.Width);

            for (int r = 0; r < totalRows; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    Assert.AreEqual(r * 10f + c, loaded[r, c], 1e-4f);
                }
            }

            // 清理单字段
            fieldStore.Clear();
            Assert.IsFalse(fieldStore.HasField());
            // 表级索引依然完好保留（字段级独立物理隔离）
            Assert.AreEqual(5, indexStore.ReadSymbols().Count);
        }

        [TestMethod]
        public void TestAutoExpandWidth_OnLoadMatrix()
        {
            var indexStore = new TimeseriesIndexStore(_tempDir, "t_expand");
            var fieldStore = new TimeseriesFieldStore(_tempDir, "t_expand", "volume", 256, "none", indexStore);

            int width = 2;
            int totalRows = 300;
            var symbols = new[] { "s1", "s2" };
            var dates = new List<MarketTime>(totalRows);
            for (int i = 0; i < totalRows; i++) dates.Add(new MarketTime(2024, 1, 1).AddDays(i));

            using var matrix = new ChunkMatrix2D<double>(width, 256, totalRows);
            for (int r = 0; r < totalRows; r++)
            {
                matrix[r, 0] = 100.0 + r;
                matrix[r, 1] = 200.0 + r;
            }

            fieldStore.WriteMatrix(matrix, symbols, dates);

            // 加载时指定更宽的 targetWidth = 4，后两列自动填充 NaN
            using var loaded = fieldStore.LoadMatrix<double>(targetWidth: 4);
            Assert.AreEqual(4, loaded.Width);
            Assert.AreEqual(totalRows, loaded.RowCount);

            for (int r = 0; r < totalRows; r++)
            {
                Assert.AreEqual(100.0 + r, loaded[r, 0], 1e-9);
                Assert.AreEqual(200.0 + r, loaded[r, 1], 1e-9);
                Assert.IsTrue(double.IsNaN(loaded[r, 2]));
                Assert.IsTrue(double.IsNaN(loaded[r, 3]));
            }
        }
    }
}
