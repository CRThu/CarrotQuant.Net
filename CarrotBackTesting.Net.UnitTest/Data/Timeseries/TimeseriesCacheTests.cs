using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class TimeseriesCacheTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CarrotCacheTests_" + Guid.NewGuid().ToString("N"));
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
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesCache("", "t1"));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesCache("   ", "t1"));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesCache(_tempDir, ""));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesCache(_tempDir, "   "));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesCache(_tempDir, "t1", 0));
            Assert.ThrowsException<ArgumentException>(() => new TimeseriesCache(_tempDir, "t1", 1000)); // 非 2 的幂

            var store = new TimeseriesCache(_tempDir, "table_1", 2048, "none");
            Assert.AreEqual("table_1", store.TableId);
            Assert.AreEqual(2048, store.PageSize);
            Assert.AreEqual("none", store.Compression);
        }

        [TestMethod]
        public void TestWriteAndLoadMatrix_AcrossMultipleChunks()
        {
            const string tableId = "ashare.kline.1d.raw.baostock";
            const string fieldName = "close";
            const int pageSize = 1024;
            const int totalRows = 2500; // 跨越 3 个 chunk: 1024 + 1024 + 452
            const int width = 20;

            var store = new TimeseriesCache(_tempDir, tableId, pageSize);
            Assert.IsFalse(store.HasField(fieldName));

            // 1. 准备原始测试数据与 Buffer
            var symbols = new List<string>();
            for (int c = 0; c < width; c++) symbols.Add($"sh.60000{c:D2}");

            var tradeDates = new List<DateTime>();
            var startDate = new DateTime(2020, 1, 1);
            for (int r = 0; r < totalRows; r++) tradeDates.Add(startDate.AddDays(r));

            using var buffer = new ChunkMatrix2D<double>(width, pageSize, totalRows);
            for (int r = 0; r < totalRows; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    buffer[r, c] = r * 100.0 + c + 0.123;
                }
            }

            // 2. 写入缓存
            store.WriteMatrix(fieldName, buffer, symbols, tradeDates);
            Assert.IsTrue(store.HasField(fieldName));

            string fieldDir = store.GetFieldStoreDir(fieldName);
            Assert.IsTrue(File.Exists(Path.Combine(fieldDir, "chunk_0.bin")));
            Assert.IsTrue(File.Exists(Path.Combine(fieldDir, "chunk_1.bin")));
            Assert.IsTrue(File.Exists(Path.Combine(fieldDir, "chunk_2.bin")));

            // 3. 从缓存恢复加载 Buffer
            using var loadedBuffer = store.LoadMatrix<double>(fieldName);

            Assert.IsNotNull(loadedBuffer);
            Assert.AreEqual(totalRows, loadedBuffer.RowCount);
            Assert.AreEqual(width, loadedBuffer.Width);

            // 4. 全量数值精度比对
            for (int r = 0; r < totalRows; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    double expected = r * 100.0 + c + 0.123;
                    Assert.AreEqual(expected, loadedBuffer[r, c], 1e-9, $"第 {r} 行第 {c} 列数值不匹配");
                }
            }

            // 5. 校验元数据
            var meta = store.ReadMetadata(fieldName);
            Assert.IsNotNull(meta);
            Assert.AreEqual(tableId, meta.TableId);
            Assert.AreEqual(fieldName, meta.FieldName);
            Assert.AreEqual(pageSize, meta.PageSize);
            Assert.AreEqual(width, meta.Width);
            Assert.AreEqual(totalRows, meta.RowCount);
            Assert.AreEqual(startDate, meta.StartDate);
            Assert.AreEqual(tradeDates[^1], meta.EndDate);
            Assert.AreEqual(width, meta.Symbols.Count);
            Assert.AreEqual(totalRows, meta.TradeDates.Count);
        }

        [TestMethod]
        public void TestAppendRows_CrossChunkBoundaries()
        {
            const string tableId = "ashare.kline.1d.raw.baostock";
            const string fieldName = "open";
            const int pageSize = 1024;
            const int initialRows = 1000;
            const int appendRows = 500; // 1000 + 500 = 1500 行，跨越 1024 分页边界
            const int width = 10;

            var store = new TimeseriesCache(_tempDir, tableId, pageSize);

            var symbols = new List<string>();
            for (int c = 0; c < width; c++) symbols.Add($"sz.00000{c:D2}");

            var tradeDates = new List<DateTime>();
            var baseDate = new DateTime(2021, 1, 1);
            for (int r = 0; r < initialRows; r++) tradeDates.Add(baseDate.AddDays(r));

            // 初始写入 1000 行
            using (var initBuffer = new ChunkMatrix2D<float>(width, pageSize, initialRows))
            {
                for (int r = 0; r < initialRows; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        initBuffer[r, c] = (float)(r * 10.0 + c);
                    }
                }
                store.WriteMatrix(fieldName, initBuffer, symbols, tradeDates);
            }

            // 增量追加 500 行
            var newDates = new List<DateTime>();
            for (int r = 0; r < appendRows; r++)
            {
                newDates.Add(baseDate.AddDays(initialRows + r));
            }

            store.AppendRows<float>(fieldName, appendRows, (rowIdx, destSpan) =>
            {
                for (int c = 0; c < width; c++)
                {
                    destSpan[c] = (float)(rowIdx * 10.0 + c);
                }
            }, newDates);

            // 验证增量后的完整性
            using var loadedBuffer = store.LoadMatrix<float>(fieldName);
            Assert.AreEqual(initialRows + appendRows, loadedBuffer.RowCount);

            for (int r = 0; r < initialRows + appendRows; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    float expected = (float)(r * 10.0 + c);
                    Assert.AreEqual(expected, loadedBuffer[r, c], 1e-5f);
                }
            }

            var meta = store.ReadMetadata(fieldName);
            Assert.IsNotNull(meta);
            Assert.AreEqual(initialRows + appendRows, meta.RowCount);
            Assert.AreEqual(newDates[^1], meta.EndDate);
        }

        [TestMethod]
        public void TestAppendRows_UncompressedMode_CrossChunkBoundaries()
        {
            const string tableId = "test_none_append";
            const string fieldName = "open";
            const int pageSize = 512;
            const int initialRows = 500;
            const int appendRows = 100; // 500 + 100 = 600 行，跨越 512 边界
            const int width = 10;

            var store = new TimeseriesCache(_tempDir, tableId, pageSize, compression: "none");

            var symbols = new List<string>();
            for (int c = 0; c < width; c++) symbols.Add($"s_{c}");
            var tradeDates = new List<DateTime>();
            var baseDate = new DateTime(2021, 1, 1);
            for (int r = 0; r < initialRows; r++) tradeDates.Add(baseDate.AddDays(r));

            using (var initBuffer = new ChunkMatrix2D<float>(width, pageSize, initialRows))
            {
                for (int r = 0; r < initialRows; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        initBuffer[r, c] = r * 10.0f + c;
                    }
                }
                store.WriteMatrix(fieldName, initBuffer, symbols, tradeDates);
            }

            var newDates = new List<DateTime>();
            for (int r = 0; r < appendRows; r++) newDates.Add(baseDate.AddDays(initialRows + r));

            store.AppendRows<float>(fieldName, appendRows, (rowIdx, destSpan) =>
            {
                for (int c = 0; c < width; c++) destSpan[c] = rowIdx * 10.0f + c;
            }, newDates);

            using var loadedBuffer = store.LoadMatrix<float>(fieldName);
            Assert.AreEqual(initialRows + appendRows, loadedBuffer.RowCount);

            for (int r = 0; r < initialRows + appendRows; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    Assert.AreEqual(r * 10.0f + c, loadedBuffer[r, c], 1e-5f);
                }
            }
        }

        [TestMethod]
        public void TestTypeMismatch_ThrowsInvalidOperationException()
        {
            const string tableId = "test_table";
            const string fieldName = "close";
            var store = new TimeseriesCache(_tempDir, tableId, 1024);

            using var buffer = new ChunkMatrix2D<double>(5, 1024, 10);
            store.WriteMatrix(fieldName, buffer, new[] { "s1", "s2", "s3", "s4", "s5" }, new DateTime[10]);

            // 写入的是 double，尝试作为 long 加载应抛出强类型异常
            Assert.ThrowsException<InvalidOperationException>(() => store.LoadMatrix<long>(fieldName));
        }

        [TestMethod]
        public void TestCorruptedChunkSize_ThrowsInvalidDataException()
        {
            const string tableId = "test_table";
            const string fieldName = "close";
            var store = new TimeseriesCache(_tempDir, tableId, 1024);

            using var buffer = new ChunkMatrix2D<double>(5, 1024, 10);
            store.WriteMatrix(fieldName, buffer, new[] { "s1", "s2", "s3", "s4", "s5" }, new DateTime[10]);

            // 篡改文件大小模拟损坏
            string chunkPath = Path.Combine(store.GetFieldStoreDir(fieldName), "chunk_0.bin");
            File.WriteAllBytes(chunkPath, new byte[10]); // 故意截断为 10 字节

            Assert.ThrowsException<InvalidDataException>(() => store.LoadMatrix<double>(fieldName));
        }

        [TestMethod]
        public void TestClearAndClearTable()
        {
            const string tableId = "test_table";
            var store = new TimeseriesCache(_tempDir, tableId, 1024);

            using var buffer = new ChunkMatrix2D<double>(2, 1024, 5);
            store.WriteMatrix("close", buffer, new[] { "s1", "s2" }, new DateTime[5]);
            store.WriteMatrix("open", buffer, new[] { "s1", "s2" }, new DateTime[5]);

            Assert.IsTrue(store.HasField("close"));
            Assert.IsTrue(store.HasField("open"));

            store.Clear("close");
            Assert.IsFalse(store.HasField("close"));
            Assert.IsTrue(store.HasField("open"));

            store.ClearTable();
            Assert.IsFalse(store.HasField("open"));
        }

        [TestMethod]
        public void TestLz4Compression_SubstantiallyReducesSizeAndRestoresExactData()
        {
            const string tableId = "test_lz4_table";
            const string fieldName = "volume";
            const int pageSize = 1024;
            const int rows = 1024;
            const int width = 100;
            long rawByteCount = (long)rows * width * sizeof(double); // 819,200 字节

            var store = new TimeseriesCache(_tempDir, tableId, pageSize, compression: "lz4");

            var symbols = new List<string>();
            for (int c = 0; c < width; c++) symbols.Add($"s_{c}");
            var dates = new List<DateTime>();
            for (int r = 0; r < rows; r++) dates.Add(new DateTime(2022, 1, 1).AddDays(r));

            using (var buffer = new ChunkMatrix2D<double>(width, pageSize, rows))
            {
                // 模拟具有周期性与局部相似性的量价特征数据
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        buffer[r, c] = (r % 20) * 1000.0 + (c % 10);
                    }
                }
                store.WriteMatrix(fieldName, buffer, symbols, dates);
            }

            // 1. 验证磁盘压缩效果（LZ4 应将 819KB 至少压缩 50% 以上）
            string chunkFile = Path.Combine(store.GetFieldStoreDir(fieldName), "chunk_0.bin");
            var fi = new FileInfo(chunkFile);
            Assert.IsTrue(fi.Length < rawByteCount / 2, $"LZ4 压缩效果未达标: 原始 {rawByteCount} 字节, 压缩后 {fi.Length} 字节");

            // 2. 验证解压恢复数值的 100% 纯正
            using var loaded = store.LoadMatrix<double>(fieldName);
            Assert.AreEqual(rows, loaded.RowCount);
            Assert.AreEqual(width, loaded.Width);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    double expected = (r % 20) * 1000.0 + (c % 10);
                    Assert.AreEqual(expected, loaded[r, c], 1e-9);
                }
            }
        }

        [TestMethod]
        public void TestUncompressed_NoneMode_ExactDiskSize()
        {
            const string tableId = "test_none_table";
            const string fieldName = "price";
            const int pageSize = 512;
            const int rows = 500;
            const int width = 10;
            long expectedBytes = (long)rows * width * sizeof(float);

            var store = new TimeseriesCache(_tempDir, tableId, pageSize, compression: "none");

            var symbols = new List<string>();
            for (int c = 0; c < width; c++) symbols.Add($"s_{c}");
            var dates = new List<DateTime>();
            for (int r = 0; r < rows; r++) dates.Add(new DateTime(2023, 1, 1).AddDays(r));

            using (var buffer = new ChunkMatrix2D<float>(width, pageSize, rows))
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        buffer[r, c] = r + c * 0.5f;
                    }
                }
                store.WriteMatrix(fieldName, buffer, symbols, dates);
            }

            // 无压缩模式下，文件大小必须完全等于 raw 字节
            string chunkFile = Path.Combine(store.GetFieldStoreDir(fieldName), "chunk_0.bin");
            var fi = new FileInfo(chunkFile);
            Assert.AreEqual(expectedBytes, fi.Length, "无压缩模式下 chunk 文件大小必须精确等于 raw 字节");

            using var loaded = store.LoadMatrix<float>(fieldName);
            Assert.AreEqual(rows, loaded.RowCount);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    Assert.AreEqual(r + c * 0.5f, loaded[r, c], 1e-5f);
                }
            }
        }

        [TestMethod]
        public void TestNonFullDataAndSparseNaN_PersistAndRestoreAccuracy()
        {
            // 验证非满分块（pageSize=1024 但只有 300 行）以及真实金融场景中的稀疏 NaN（未上市/退市）
            const string tableId = "test_sparse_table";
            const string fieldName = "close";
            const int pageSize = 1024;
            const int totalRows = 300; // 非满页（仅填充满 300/1024）
            const int width = 5;

            var store = new TimeseriesCache(_tempDir, tableId, pageSize, compression: "lz4");

            var symbols = new List<string> { "s_ipo_late", "s_delisted", "s_normal", "s_halted", "s_all_nan" };
            var dates = new List<DateTime>();
            for (int r = 0; r < totalRows; r++) dates.Add(new DateTime(2021, 1, 1).AddDays(r));

            using (var buffer = new ChunkMatrix2D<double>(width, pageSize, totalRows))
            {
                for (int r = 0; r < totalRows; r++)
                {
                    // 列 0: 晚上市股票（前 100 天为 NaN）
                    buffer[r, 0] = r < 100 ? double.NaN : 10.0 + r;

                    // 列 1: 早期退市股票（后 150 天为 NaN）
                    buffer[r, 1] = r >= 150 ? double.NaN : 20.0 + r;

                    // 列 2: 正常连续交易股票
                    buffer[r, 2] = 30.0 + r;

                    // 列 3: 停牌股票（中间 50~80 天成交量为 0 或 NaN）
                    buffer[r, 3] = (r >= 50 && r <= 80) ? 0.0 : 40.0 + r;

                    // 列 4: 全周期未上市/停牌股票（全 NaN）
                    buffer[r, 4] = double.NaN;
                }

                store.WriteMatrix(fieldName, buffer, symbols, dates);
            }

            // 从 LZ4 压缩缓存直接反序列化
            using var loaded = store.LoadMatrix<double>(fieldName);
            Assert.AreEqual(totalRows, loaded.RowCount);
            Assert.AreEqual(width, loaded.Width);

            for (int r = 0; r < totalRows; r++)
            {
                // 列 0
                if (r < 100) Assert.IsTrue(double.IsNaN(loaded[r, 0]));
                else Assert.AreEqual(10.0 + r, loaded[r, 0], 1e-9);

                // 列 1
                if (r >= 150) Assert.IsTrue(double.IsNaN(loaded[r, 1]));
                else Assert.AreEqual(20.0 + r, loaded[r, 1], 1e-9);

                // 列 2
                Assert.AreEqual(30.0 + r, loaded[r, 2], 1e-9);

                // 列 3
                if (r >= 50 && r <= 80) Assert.AreEqual(0.0, loaded[r, 3], 1e-9);
                else Assert.AreEqual(40.0 + r, loaded[r, 3], 1e-9);

                // 列 4
                Assert.IsTrue(double.IsNaN(loaded[r, 4]));
            }
        }

        [TestMethod]
        public void TestRewriteChunk_SingleBinIsolation_ZeroIoAmplification()
        {
            // 验证单分块精准重刷（RewriteChunk）机制：
            // 1. 跨越 3 个分块（Chunk 0, Chunk 1, Chunk 2 尾块）；
            // 2. 仅对 Chunk 1 实施局部数据重写/清洗；
            // 3. 验证 Chunk 0 与 Chunk 2 在磁盘上的物理文件未发生任何触碰（内容与哈希完全不变，I/O 放大比严格为单块大小）；
            // 4. 验证重新加载后，仅 Chunk 1 的数据被更新，Chunk 0 与 Chunk 2 保持历史精度；
            // 5. 校验越界分块索引抛出 ArgumentOutOfRangeException。
            const string tableId = "test_rewrite_chunk";
            const string fieldName = "close";
            const int pageSize = 1024;
            const int totalRows = 2500; // 跨越 3 个分块 (0: 1024行, 1: 1024行, 2: 452行)
            const int width = 4;

            var store = new TimeseriesCache(_tempDir, tableId, pageSize, compression: "lz4");

            var symbols = new List<string> { "s0", "s1", "s2", "s3" };
            var dates = new List<DateTime>();
            for (int r = 0; r < totalRows; r++) dates.Add(new DateTime(2021, 1, 1).AddDays(r));

            // 1. 初始构建 3 个分块的缓存
            using (var buffer = new ChunkMatrix2D<double>(width, pageSize, totalRows))
            {
                for (int r = 0; r < totalRows; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        buffer[r, c] = r * 10.0 + c;
                    }
                }
                store.WriteMatrix(fieldName, buffer, symbols, dates);
            }

            string fieldDir = Path.Combine(_tempDir, tableId, fieldName);
            string chunk0Path = Path.Combine(fieldDir, "chunk_0.bin");
            string chunk1Path = Path.Combine(fieldDir, "chunk_1.bin");
            string chunk2Path = Path.Combine(fieldDir, "chunk_2.bin");

            Assert.IsTrue(File.Exists(chunk0Path));
            Assert.IsTrue(File.Exists(chunk1Path));
            Assert.IsTrue(File.Exists(chunk2Path));

            byte[] chunk0BytesBefore = File.ReadAllBytes(chunk0Path);
            byte[] chunk2BytesBefore = File.ReadAllBytes(chunk2Path);

            // 2. 精确重刷 Chunk 1 (行 1024 ~ 2047)
            store.RewriteChunk<double>(fieldName, pageIndex: 1, (globalRow, rowSpan) =>
            {
                for (int c = 0; c < width; c++)
                {
                    rowSpan[c] = 88888.0 + globalRow * 100.0 + c;
                }
            });

            // 3. 断言物理隔离性：Chunk 0 与 Chunk 2 字节内容绝对未变（零触碰，零 I/O 放大）
            byte[] chunk0BytesAfter = File.ReadAllBytes(chunk0Path);
            byte[] chunk2BytesAfter = File.ReadAllBytes(chunk2Path);
            CollectionAssert.AreEqual(chunk0BytesBefore, chunk0BytesAfter, "Chunk 0 不应被写入或触碰");
            CollectionAssert.AreEqual(chunk2BytesBefore, chunk2BytesAfter, "Chunk 2 不应被写入或触碰");

            // 4. 重新反序列化全量 Buffer 校验数据精度
            using var loaded = store.LoadMatrix<double>(fieldName);
            Assert.AreEqual(totalRows, loaded.RowCount);
            Assert.AreEqual(width, loaded.Width);

            for (int r = 0; r < totalRows; r++)
            {
                if (r < 1024)
                {
                    // Chunk 0: 保持原值
                    for (int c = 0; c < width; c++)
                    {
                        Assert.AreEqual(r * 10.0 + c, loaded[r, c], 1e-9);
                    }
                }
                else if (r < 2048)
                {
                    // Chunk 1: 已更新为清洗重刷后的新值
                    for (int c = 0; c < width; c++)
                    {
                        Assert.AreEqual(88888.0 + r * 100.0 + c, loaded[r, c], 1e-9);
                    }
                }
                else
                {
                    // Chunk 2: 保持原值
                    for (int c = 0; c < width; c++)
                    {
                        Assert.AreEqual(r * 10.0 + c, loaded[r, c], 1e-9);
                    }
                }
            }

            // 5. 校验非法分块索引异常拦截
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            {
                store.RewriteChunk<double>(fieldName, pageIndex: -1, (_, _) => { });
            });
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            {
                store.RewriteChunk<double>(fieldName, pageIndex: 3, (_, _) => { });
            });
        }

        [TestMethod]
        public void TestVariableWidthChunks_AutoExpandOnLoad_HistoricalChunksUntouched()
        {
            // 验证 Scheme B 物理存储模型：
            // 1. Chunk 0 为历史分块（宽 2），Chunk 1 为当前活跃分块（宽 2）；
            // 2. 当有新股票 IPO 上市扩充时，仅重写当前活跃分块 Chunk 1（扩宽至 4 列）；
            // 3. 断言历史分块 Chunk 0 磁盘文件物理零触碰（大小、修改时间与内容严格不变，避免冗余 0/NaN 存储膨胀）；
            // 4. 载入 Buffer 时，目标宽度为 4，历史分块 Chunk 0 自动在内存中拓宽（前 2 列保留原始值，后 2 列自动补 NaN）；
            // 5. 当前分块 Chunk 1 完整包含 4 列全部有效数值。
            const string tableId = "test_var_width";
            const string fieldName = "close";
            const int pageSize = 1024;
            const int totalRows = 1500; // Chunk 0 (1024行), Chunk 1 (476行)
            const int initialWidth = 2;

            var store = new TimeseriesCache(_tempDir, tableId, pageSize, compression: "lz4");

            var initialSymbols = new List<string> { "s0", "s1" };
            var dates = new List<DateTime>();
            for (int r = 0; r < totalRows; r++) dates.Add(new DateTime(2021, 1, 1).AddDays(r));

            // 1. 初始构建 2 列分块
            using (var buffer = new ChunkMatrix2D<double>(initialWidth, pageSize, totalRows))
            {
                for (int r = 0; r < totalRows; r++)
                {
                    buffer[r, 0] = r * 10.0 + 0;
                    buffer[r, 1] = r * 10.0 + 1;
                }
                store.WriteMatrix(fieldName, buffer, initialSymbols, dates);
            }

            string fieldDir = Path.Combine(_tempDir, tableId, fieldName);
            string chunk0Path = Path.Combine(fieldDir, "chunk_0.bin");
            string chunk1Path = Path.Combine(fieldDir, "chunk_1.bin");

            byte[] chunk0BytesBefore = File.ReadAllBytes(chunk0Path);

            // 2. 模拟新标的上市：仅重写当前活跃分块 Chunk 1（pageIndex: 1, 宽度扩展为 4 列）
            var expandedSymbols = new List<string> { "s0", "s1", "s2", "s3" };
            store.RewriteChunk<double>(
                fieldName,
                pageIndex: 1,
                (globalRow, rowSpan) =>
                {
                    rowSpan[0] = globalRow * 10.0 + 0;
                    rowSpan[1] = globalRow * 10.0 + 1;
                    rowSpan[2] = globalRow * 10.0 + 2;
                    rowSpan[3] = globalRow * 10.0 + 3;
                },
                newChunkWidth: 4,
                updatedSymbols: expandedSymbols);

            // 3. 断言历史分块 Chunk 0 物理文件零触碰（零写 I/O 放大，零历史膨胀）
            byte[] chunk0BytesAfter = File.ReadAllBytes(chunk0Path);
            CollectionAssert.AreEqual(chunk0BytesBefore, chunk0BytesAfter, "历史分块 Chunk 0 绝对不可被重写或触碰");

            // 4. 校验元数据自描述：各 Chunk 拥有独立的宽度描述
            var meta = store.ReadMetadata(fieldName);
            Assert.IsNotNull(meta);
            Assert.AreEqual(4, meta.Width);
            Assert.AreEqual(2, meta.GetChunkWidth(0), "Chunk 0 应保持宽度 2");
            Assert.AreEqual(4, meta.GetChunkWidth(1), "Chunk 1 应更新为宽度 4");

            // 5. 载入 4 列宽度的完整内存 Buffer，验证内存自动拓宽对齐
            using var loaded = store.LoadMatrix<double>(fieldName, 4);
            Assert.AreEqual(totalRows, loaded.RowCount);
            Assert.AreEqual(4, loaded.Width);

            // 验证 Chunk 0（历史分块）：前 2 列保留原始数值，后 2 列自动补 NaN
            for (int r = 0; r < 1024; r++)
            {
                Assert.AreEqual(r * 10.0 + 0, loaded[r, 0], 1e-9);
                Assert.AreEqual(r * 10.0 + 1, loaded[r, 1], 1e-9);
                Assert.IsTrue(double.IsNaN(loaded[r, 2]), $"第 {r} 行历史分块未上市股票必须自动补 NaN");
                Assert.IsTrue(double.IsNaN(loaded[r, 3]), $"第 {r} 行历史分块未上市股票必须自动补 NaN");
            }

            // 验证 Chunk 1（当前活跃分块）：4 列全部包含有效数据
            for (int r = 1024; r < totalRows; r++)
            {
                Assert.AreEqual(r * 10.0 + 0, loaded[r, 0], 1e-9);
                Assert.AreEqual(r * 10.0 + 1, loaded[r, 1], 1e-9);
                Assert.AreEqual(r * 10.0 + 2, loaded[r, 2], 1e-9);
                Assert.AreEqual(r * 10.0 + 3, loaded[r, 3], 1e-9);
            }
        }

        [TestMethod]
        public void TestIsCompatible_EdgeCases_ReturnsFalse()
        {
            const string tableId = "compat_test_table";
            const string fieldName = "close";
            var store = new TimeseriesCache(_tempDir, tableId, 1024);

            var symbols = new List<string> { "sh.600000", "sh.600001" };
            var dates = new List<DateTime> { DateTime.Today };
            using (var buffer = new ChunkMatrix2D<double>(2, 1024, 1))
            {
                buffer[0, 0] = 1.0;
                buffer[0, 1] = 2.0;
                store.WriteMatrix(fieldName, buffer, symbols, dates);
            }

            // 1. 类型不匹配
            Assert.IsFalse(store.IsCompatible(fieldName, symbols, typeof(int), out _));

            // 2. PageSize 不匹配 (用 pageSize=2048 的 store 检查)
            var store2048 = new TimeseriesCache(_tempDir, tableId, 2048);
            Assert.IsFalse(store2048.IsCompatible(fieldName, symbols, typeof(double), out _));

            // 3. 股票代码集合缩减
            var reducedSymbols = new List<string> { "sh.600000" };
            Assert.IsFalse(store.IsCompatible(fieldName, reducedSymbols, typeof(double), out _));

            // 4. 股票代码前缀不守恒 (顺序或内容不一致)
            var alteredSymbols = new List<string> { "sh.600001", "sh.600000" };
            Assert.IsFalse(store.IsCompatible(fieldName, alteredSymbols, typeof(double), out _));

            // 5. 不存在的字段
            Assert.IsFalse(store.IsCompatible("non_existent", symbols, typeof(double), out _));
        }

        [TestMethod]
        public void TestRewriteChunk_InvalidIndex_ThrowsException()
        {
            const string tableId = "rewrite_invalid_idx";
            var store = new TimeseriesCache(_tempDir, tableId, 64);
            var symbols = new List<string> { "sh.600000" };
            var dates = new List<DateTime> { DateTime.Today };

            using (var buffer = new ChunkMatrix2D<double>(1, 64, 1))
            {
                store.WriteMatrix("close", buffer, symbols, dates);
            }

            // 负数索引抛出 ArgumentOutOfRangeException
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            {
                store.RewriteChunk<double>("close", -1, (r, span) => span.Fill(1.0));
            });

            // 超界索引抛出 ArgumentOutOfRangeException
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            {
                store.RewriteChunk<double>("close", 100, (r, span) => span.Fill(1.0));
            });
        }

        [TestMethod]
        public void TestTryReadChunk_ReadsSingleChunk_Accurately()
        {
            const string tableId = "try_read_chunk_table";
            var store = new TimeseriesCache(_tempDir, tableId, 16, "lz4");
            var symbols = new List<string> { "sh.600000", "sz.000001" };
            var dates = new List<DateTime>();
            for (int i = 0; i < 35; i++) dates.Add(DateTime.Today.AddDays(i));

            using (var buffer = new ChunkMatrix2D<double>(2, 16, 35))
            {
                for (int r = 0; r < 35; r++)
                {
                    buffer[r, 0] = r * 1.0;
                    buffer[r, 1] = r * 2.0;
                }
                store.WriteMatrix("close", buffer, symbols, dates);
            }

            // 读取第 0 块（16 行，32 个 double）
            double[] chunk0 = new double[32];
            bool ok0 = store.TryReadChunk<double>("close", 0, chunk0, out int rows0);
            Assert.IsTrue(ok0);
            Assert.AreEqual(16, rows0);
            Assert.AreEqual(0.0, chunk0[0]);
            Assert.AreEqual(15.0, chunk0[30]); // 第 15 行第 0 列

            // 读取第 2 块（最后 3 行：35 - 32 = 3 行，6 个 double）
            double[] chunk2 = new double[32];
            bool ok2 = store.TryReadChunk<double>("close", 2, chunk2, out int rows2);
            Assert.IsTrue(ok2);
            Assert.AreEqual(3, rows2);
            Assert.AreEqual(32.0, chunk2[0]); // 第 32 行

            // 读取不存在的第 5 块
            bool ok5 = store.TryReadChunk<double>("close", 5, chunk0, out _);
            Assert.IsFalse(ok5);

            // 缓冲区容量不足安全返回 false
            double[] small = new double[5];
            bool okSmall = store.TryReadChunk<double>("close", 0, small, out _);
            Assert.IsFalse(okSmall);
        }
    }
}

