using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class TimeseriesChunkManagerTests
    {
        private string _tempCacheDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempCacheDir = Path.Combine(Path.GetTempPath(), "CarrotQuantTests_Manager", Guid.NewGuid().ToString());
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

        [TestMethod]
        public void TestChunkManager_InitialLoad_PropertiesAndAsReadOnly()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ", "600000.SH" }, 10);
            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: 4,
                refreshMode: "lazy");

            Assert.AreEqual("close", manager.FieldName);
            Assert.AreEqual(typeof(double), manager.DataType);
            Assert.IsFalse(manager.IsDirty);
            Assert.AreEqual(10, manager.TotalRows);
            // 10 行在 pageSize=4 下：chunk 0 (4行), chunk 1 (4行), chunk 2 (2行)
            Assert.AreEqual(2, manager.ActivePageIndex);
            Assert.AreEqual(2, manager.ActivePageRowCount);

            var readOnlyView = manager.AsReadOnly();
            Assert.AreEqual(10, readOnlyView.RowCount);
            Assert.AreEqual(2, readOnlyView.Width);

            for (int r = 0; r < 10; r++)
            {
                Assert.AreEqual(r * 100.0 + 0, readOnlyView[r, 0]);
                Assert.AreEqual(r * 100.0 + 1, readOnlyView[r, 1]);
            }
        }

        [TestMethod]
        public void TestChunkManager_AppendRow_Rollover_PageBoundary()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ", "600000.SH" }, 3);
            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: 4,
                refreshMode: "lazy");

            // 初始状态：3 行，chunk 0 尚未写满
            Assert.AreEqual(3, manager.TotalRows);
            Assert.AreEqual(0, manager.ActivePageIndex);
            Assert.AreEqual(3, manager.ActivePageRowCount);
            Assert.IsFalse(manager.IsDirty);

            // 追加第 4 行，填满 chunk 0
            manager.AppendRow(stackalloc double[] { 300.0, 301.0 });
            Assert.AreEqual(4, manager.TotalRows);
            Assert.AreEqual(0, manager.ActivePageIndex);
            Assert.AreEqual(4, manager.ActivePageRowCount);
            Assert.IsTrue(manager.IsDirty);

            // 追加第 5 行，触发跨页轮转（Rollover）开辟 chunk 1
            manager.AppendRow(stackalloc double[] { 400.0, 401.0 });
            Assert.AreEqual(5, manager.TotalRows);
            Assert.AreEqual(1, manager.ActivePageIndex);
            Assert.AreEqual(1, manager.ActivePageRowCount);
            Assert.IsTrue(manager.IsDirty);

            // 验证只读视图无缝跨页连续读取
            var view = manager.AsReadOnly();
            Assert.AreEqual(5, view.RowCount);
            Assert.AreEqual(300.0, view[3, 0]);
            Assert.AreEqual(301.0, view[3, 1]);
            Assert.AreEqual(400.0, view[4, 0]);
            Assert.AreEqual(401.0, view[4, 1]);
        }

        [TestMethod]
        public void TestChunkManager_AppendRow_4096DefaultBoundary_Rollover()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ" }, 4096);
            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: 4096,
                refreshMode: "lazy");

            manager.EnsureLoaded();
            Assert.AreEqual(4096, manager.TotalRows);
            Assert.AreEqual(0, manager.ActivePageIndex);
            Assert.AreEqual(4096, manager.ActivePageRowCount);

            // 追加第 4097 行，触发 4096 真实分页轮转
            manager.AppendRow(stackalloc double[] { 9999.0 });
            Assert.AreEqual(4097, manager.TotalRows);
            Assert.AreEqual(1, manager.ActivePageIndex);
            Assert.AreEqual(1, manager.ActivePageRowCount);
            Assert.IsTrue(manager.IsDirty);

            Assert.AreEqual(9999.0, manager.AsReadOnly()[4096, 0]);
        }

        [TestMethod]
        public void TestChunkManager_Commit_IncrementalFlushAndZeroHistoricalWriteAmplification()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ", "600000.SH" }, 4);
            var cacheStore = new TimeseriesStore(_tempCacheDir, "test_table", pageSize: 4, compression: "lz4");

            // 1. 首次加载：4 行正好写满 chunk 0
            using (var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 4,
                refreshMode: "force"))
            {
                manager.EnsureLoaded();
                Assert.AreEqual(4, manager.TotalRows);
                Assert.IsFalse(manager.IsDirty);
            }

            string chunk0Path = Path.Combine(_tempCacheDir, "test_table", "close", "chunk_0.bin");
            string chunk1Path = Path.Combine(_tempCacheDir, "test_table", "close", "chunk_1.bin");
            Assert.IsTrue(File.Exists(chunk0Path), "chunk_0.bin 必须在首次写入后存在。");
            Assert.IsFalse(File.Exists(chunk1Path), "chunk_1.bin 此时不应存在。");

            var chunk0LastWrite = File.GetLastWriteTimeUtc(chunk0Path);

            // 2. 模拟实盘运行：追加 2 行数据（跨入 chunk 1），然后精准 Commit
            using (var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 4,
                refreshMode: "lazy"))
            {
                manager.EnsureLoaded();
                Assert.AreEqual(4, manager.TotalRows);

                // 追加第 5、6 行
                manager.AppendRow(stackalloc double[] { 500.0, 501.0 });
                manager.AppendRow(stackalloc double[] { 600.0, 601.0 });

                Assert.AreEqual(6, manager.TotalRows);
                Assert.AreEqual(1, manager.ActivePageIndex);
                Assert.AreEqual(2, manager.ActivePageRowCount);
                Assert.IsTrue(manager.IsDirty);

                manager.Commit();
                Assert.IsFalse(manager.IsDirty);
            }

            // 3. 验证历史分块零写放大（chunk_0 未被重新写入，chunk_1 增量落盘）
            Assert.IsTrue(File.Exists(chunk1Path), "Commit 后 chunk_1.bin 必须生成。");
            var chunk0NewWrite = File.GetLastWriteTimeUtc(chunk0Path);
            Assert.AreEqual(chunk0LastWrite, chunk0NewWrite, "历史 chunk_0.bin 严禁被二次写入，必须保证零写放大。");

            // 4. 重启模拟：新实例从磁盘缓存重新载入，验证全量 6 行一致性
            using (var recoveryManager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 4,
                refreshMode: "lazy"))
            {
                recoveryManager.EnsureLoaded();
                Assert.AreEqual(6, recoveryManager.TotalRows);
                Assert.AreEqual(1, recoveryManager.ActivePageIndex);
                Assert.AreEqual(2, recoveryManager.ActivePageRowCount);

                var view = recoveryManager.AsReadOnly();
                Assert.AreEqual(6, view.RowCount);
                Assert.AreEqual(500.0, view[4, 0]);
                Assert.AreEqual(501.0, view[4, 1]);
                Assert.AreEqual(600.0, view[5, 0]);
                Assert.AreEqual(601.0, view[5, 1]);
            }
        }

        [TestMethod]
        public void TestChunkManager_UpdateRow_And_RewriteActiveChunkWithoutNewRows()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ", "600000.SH" }, 4);
            var cacheStore = new TimeseriesStore(_tempCacheDir, "test_table", pageSize: 4, compression: "lz4");

            // 1. 首次加载 4 行并缓存
            using (var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 4,
                refreshMode: "force"))
            {
                manager.EnsureLoaded();
            }

            // 2. 模拟盘中行更新（行数未增加，但活跃分块内容被修订）
            using (var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 4,
                refreshMode: "lazy"))
            {
                manager.EnsureLoaded();
                Assert.AreEqual(4, manager.TotalRows);

                // 更新第 3 行（0-based）数据并自动标脏
                manager.UpdateRow(3, stackalloc double[] { 888.0, 999.0 });
                Assert.IsTrue(manager.IsDirty);

                // 提交落盘：行数未增，触发 RewriteChunk 重写活跃块
                manager.Commit();
                Assert.IsFalse(manager.IsDirty);
            }

            // 3. 验证从磁盘恢复后，更新的值已精准持久化
            using (var recoveryManager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 4,
                refreshMode: "lazy"))
            {
                recoveryManager.EnsureLoaded();
                Assert.AreEqual(4, recoveryManager.TotalRows);
                var view = recoveryManager.AsReadOnly();
                Assert.AreEqual(888.0, view[3, 0]);
                Assert.AreEqual(999.0, view[3, 1]);
            }
        }

        [TestMethod]
        public void TestChunkManager_AppendRow_DimensionMismatch_ThrowsArgumentException()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ", "600000.SH" }, 5);
            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: 4,
                refreshMode: "lazy");

            manager.EnsureLoaded();
            // 代码总数为 2，传入长度为 3 的截面
            Assert.ThrowsException<ArgumentException>(() =>
            {
                manager.AppendRow(stackalloc double[] { 1.0, 2.0, 3.0 });
            });
        }

        [TestMethod]
        public void TestChunkManager_Disposed_ThrowsObjectDisposedException()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ" }, 5);
            var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: 4,
                refreshMode: "lazy");

            manager.Dispose();

            Assert.ThrowsException<ObjectDisposedException>(() => manager.EnsureLoaded());
            Assert.ThrowsException<ObjectDisposedException>(() => manager.AsReadOnly());
            Assert.ThrowsException<ObjectDisposedException>(() => manager.Commit());
            Assert.ThrowsException<ObjectDisposedException>(() => manager.AppendRow(stackalloc double[] { 1.0 }));
        }

        [TestMethod]
        public void TestChunkManager_BufferProperty_And_MarkDirty()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ" }, 4);
            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: 4,
                refreshMode: "lazy");

            var rawBuffer = manager.Matrix;
            Assert.IsNotNull(rawBuffer);
            Assert.AreEqual(4, rawBuffer.RowCount);

            Assert.IsFalse(manager.IsDirty);
            manager.MarkDirty();
            Assert.IsTrue(manager.IsDirty);
        }

        [TestMethod]
        public void TestChunkManager_UpdateRow_Exceptions()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ", "600000.SH" }, 4);
            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: 4,
                refreshMode: "lazy");

            manager.EnsureLoaded();

            // 长度不匹配抛出 ArgumentException
            Assert.ThrowsException<ArgumentException>(() =>
            {
                manager.UpdateRow(0, stackalloc double[] { 1.0 });
            });

            // 行索引越界抛出 ArgumentOutOfRangeException
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            {
                manager.UpdateRow(100, stackalloc double[] { 1.0, 2.0 });
            });
        }

        [TestMethod]
        public void TestChunkManager_CorruptedCacheFallback_InLazyMode()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ" }, 4);
            var cacheStore = new TimeseriesStore(_tempCacheDir, "table_corrupt", pageSize: 4, compression: "lz4");

            // 1. 首次加载写入缓存
            using (var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 4,
                refreshMode: "force"))
            {
                manager.EnsureLoaded();
            }

            // 2. 故意破坏 chunk 文件（写入非法垃圾字节导致解压失败）
            string chunk0Path = Path.Combine(_tempCacheDir, "table_corrupt", "close", "chunk_0.bin");
            File.WriteAllBytes(chunk0Path, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });

            // 3. 再次加载，触发 catch 分支并成功 fallback 从 source 重建
            using (var manager2 = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 4,
                refreshMode: "lazy"))
            {
                manager2.EnsureLoaded();
                Assert.AreEqual(4, manager2.TotalRows);
                Assert.AreEqual(0.0, manager2.AsReadOnly()[0, 0]);
            }
        }

        [TestMethod]
        public void TestChunkManager_AppendRow_WithTimestamp_And_AutoCommitAtBatchSize()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ", "600000.SH" }, 2);
            var cacheStore = new TimeseriesStore(_tempCacheDir, "table_batch", pageSize: 16, compression: "lz4");

            // 1. 初始化 2 行并写入缓存，设置 commitBatchSize: 3
            using (var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 16,
                refreshMode: "force",
                commitBatchSize: 3))
            {
                manager.EnsureLoaded();
                Assert.AreEqual(2, manager.TotalRows);
                Assert.AreEqual(0, manager.UncommittedRowCount);

                // 追加第 1 行与第 2 行（尚未达到批次大小 3，处于纯内存缓冲态）
                manager.AppendRow(new DateTime(2025, 1, 10), stackalloc double[] { 10.0, 20.0 });
                manager.AppendRow(new DateTime(2025, 1, 11), stackalloc double[] { 11.0, 21.0 });
                Assert.AreEqual(4, manager.TotalRows);
                Assert.AreEqual(2, manager.UncommittedRowCount);
                Assert.IsTrue(manager.IsDirty);

                // 追加第 3 行，命中批次阈值 3，自动触发落盘并清空 uncommitted 缓冲！
                manager.AppendRow(new DateTime(2025, 1, 12), stackalloc double[] { 12.0, 22.0 });
                Assert.AreEqual(5, manager.TotalRows);
                Assert.AreEqual(0, manager.UncommittedRowCount);
                Assert.IsFalse(manager.IsDirty);
            }

            // 2. 磁盘恢复验证：5 行已全部入库，且时间戳保真
            using (var recoveryManager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 16,
                refreshMode: "lazy"))
            {
                recoveryManager.EnsureLoaded();
                Assert.AreEqual(5, recoveryManager.TotalRows);
                var view = recoveryManager.AsReadOnly();
                Assert.AreEqual(10.0, view[2, 0]);
                Assert.AreEqual(20.0, view[2, 1]);
                Assert.AreEqual(11.0, view[3, 0]);
                Assert.AreEqual(21.0, view[3, 1]);
                Assert.AreEqual(12.0, view[4, 0]);
                Assert.AreEqual(22.0, view[4, 1]);

                var meta = cacheStore.ReadMetadata("close");
                Assert.IsNotNull(meta);
                Assert.AreEqual(5, meta.TradeDates.Count);
                Assert.AreEqual("2025-01-12", meta.TradeDates[4]);
            }
        }

        [TestMethod]
        public void TestChunkManager_Dispose_ForcesResidualCommit()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ" }, 2);
            var cacheStore = new TimeseriesStore(_tempCacheDir, "table_residual", pageSize: 16, compression: "lz4");

            // 追加了 1 行（未达到 64 行批次），未显式调用 Commit()，直接退出并 Dispose
            using (var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 16,
                refreshMode: "force",
                commitBatchSize: 64))
            {
                manager.EnsureLoaded();
                manager.AppendRow(new DateTime(2025, 2, 1), stackalloc double[] { 99.0 });
                Assert.AreEqual(1, manager.UncommittedRowCount);
                Assert.IsTrue(manager.IsDirty);
                // 退出 using 块，自动触发 Dispose 中的保底 Commit()
            }

            // 恢复验证：残留的那一行依然成功落盘
            using (var recoveryManager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 16,
                refreshMode: "lazy"))
            {
                recoveryManager.EnsureLoaded();
                Assert.AreEqual(3, recoveryManager.TotalRows);
                Assert.AreEqual(99.0, recoveryManager.AsReadOnly()[2, 0]);
            }
        }

        [TestMethod]
        public void TestChunkManager_AppendCol_FlushesRowsFirst_ThenExpandsWidthAndCommits()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ", "600000.SH" }, 2);
            var cacheStore = new TimeseriesStore(_tempCacheDir, "table_col", pageSize: 16, compression: "lz4");

            using (var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                cacheStore,
                pageSize: 16,
                refreshMode: "force",
                commitBatchSize: 64))
            {
                manager.EnsureLoaded();
                // 1. 先在内存中追加一行（未提交）
                manager.AppendRow(new DateTime(2025, 3, 1), stackalloc double[] { 10.0, 20.0 });
                Assert.AreEqual(1, manager.UncommittedRowCount);

                // 2. 新股上市（AppendCol）：应该先自动 Commit 未落盘行，再拓宽列为 3
                manager.AppendCol("bj.920001");

                Assert.AreEqual(0, manager.UncommittedRowCount);
                Assert.AreEqual(3, manager.Matrix.Width);

                // 验证旧历史行在新标的列上自动填充为 NaN
                var view = manager.AsReadOnly();
                Assert.AreEqual(3, view.RowCount);
                Assert.IsTrue(double.IsNaN(view[0, 2]));
                Assert.IsTrue(double.IsNaN(view[1, 2]));
                Assert.IsTrue(double.IsNaN(view[2, 2]));

                // 3. 从新股票上市开始，后续 AppendRow 直接使用 3 列数据
                manager.AppendRow(new DateTime(2025, 3, 2), stackalloc double[] { 11.0, 21.0, 31.0 });
                Assert.AreEqual(4, view.RowCount);
                Assert.AreEqual(31.0, view[3, 2]);

                manager.Commit();
            }

            // 4. 磁盘恢复验证：3 只股票，新宽度已持久化
            var meta = cacheStore.ReadMetadata("close");
            Assert.IsNotNull(meta);
            Assert.AreEqual(3, meta.Symbols.Count);
            Assert.AreEqual("bj.920001", meta.Symbols[2]);
        }

        [TestMethod]
        public void TestChunkManager_StepTo_TriggersPrefetch_And_AdvancesBaseRow()
        {
            // 64 行数据，单页 16 行，常驻 4 块
            const int totalRows = 64;
            const int pageSize = 16;
            const int maxPages = 4;

            var mock = new MockMarketSeriesSource(new[] { "000001.SZ" }, totalRows);
            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: pageSize,
                refreshMode: "lazy",
                commitBatchSize: 64,
                maxPages: maxPages);

            Assert.AreEqual(4, manager.MaxPages);
            Assert.AreEqual(64, manager.TotalRows);
            Assert.AreEqual(0, manager.BaseRowIndex);

            // 1. 步进到第 12 行（当前页 75% 处，触发向后台发起预读页 1 的任务）
            manager.StepTo(12);

            // 2. 步进跨入第 16 行（挂载页 1）
            manager.StepTo(16);
            Assert.AreEqual(0, manager.BaseRowIndex);

            // 3. 继续推进到第 63 行
            manager.StepTo(63);

            // 验证常驻块数依然受限
            Assert.AreEqual(4, manager.MaxPages);
        }

        [TestMethod]
        public void TestChunkManager_PrefetchDisabled_SetsCanPrefetchFalse()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ" }, 32);
            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: 16,
                refreshMode: "lazy",
                commitBatchSize: 64,
                maxPages: 4,
                prefetch: false,
                isLive: false);

            Assert.IsFalse(manager.CanPrefetch);
            Assert.IsFalse(manager.IsLive);

            // 推进到第 12 行（正常会触发 prefetch 的位置）
            manager.StepTo(12);
            // 推进到第 16 行（正常跨页位置）
            manager.StepTo(16);

            var view = manager.AsReadOnly();
            Assert.AreEqual(32, view.RowCount);
            Assert.AreEqual(1600.0, view[16, 0]);
        }

        [TestMethod]
        public void TestChunkManager_LivingMode_DisablesPrefetchRegardlessOfPrefetchFlag()
        {
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ" }, 16);
            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: 16,
                refreshMode: "lazy",
                commitBatchSize: 64,
                maxPages: 4,
                prefetch: true,
                isLive: true);

            // 核心断言：实盘模式下即便 prefetch=true，也必须被一票否决
            Assert.IsTrue(manager.IsLive);
            Assert.IsFalse(manager.CanPrefetch);

            // 实盘模式推进到第 12 行，不触发任何预读
            manager.StepTo(12);

            // 实盘追加第 17 行（新页 Rollover）
            manager.AppendRow(stackalloc double[] { 9999.0 });
            Assert.AreEqual(17, manager.TotalRows);
            Assert.AreEqual(1, manager.ActivePageIndex);
        }

        [TestMethod]
        public void TestChunkManager_LargeDataset_SlidingWindowAndEvictionEndToEnd()
        {
            // 构造 64 行数据，pageSize = 4（共 16 个页），maxPages = 4（最多常驻 4 页 = 16 行）
            int totalDays = 64;
            int pageSize = 4;
            int maxPages = 4;
            var mock = new MockMarketSeriesSource(new[] { "000001.SZ", "600000.SH" }, totalDays);

            using var manager = new TimeseriesChunkManager<double>(
                "close",
                mock,
                mock,
                store: null,
                pageSize: pageSize,
                refreshMode: "lazy",
                commitBatchSize: 64,
                maxPages: maxPages,
                prefetch: true,
                isLive: false);

            manager.EnsureLoaded();
            Assert.AreEqual(64, manager.TotalRows);
            Assert.AreEqual(0, manager.BaseRowIndex);

            var view = manager.AsReadOnly();
            // 初始时常驻前 4 个页（16 行）
            Assert.AreEqual(16, view.RowCount);
            Assert.AreEqual(0.0, view[0, 0]);
            Assert.AreEqual(1.0, view[0, 1]);
            Assert.AreEqual(1500.0, view[15, 0]);
            Assert.AreEqual(1501.0, view[15, 1]);

            // 逐行步进推进回测，模拟引擎从第 0 行推进到第 32 行（页 8）
            for (int r = 0; r <= 32; r++)
            {
                manager.StepTo(r);
            }

            // 推进到第 32 行时，当前挂载了页 8（32~35行），常驻窗口容量为 4 页（16 行）
            // 因此最老允许的页是 9 - 4 = 5，基准行 BaseRowIndex 应已推进到 5 * 4 = 20 行！
            Assert.IsTrue(manager.BaseRowIndex >= 20, $"BaseRowIndex 应已推进以淘汰冷页，实际为: {manager.BaseRowIndex}");

            // 验证当前窗口内数据完全准确
            Assert.AreEqual(3200.0, view[32, 0]);
            Assert.AreEqual(3201.0, view[32, 1]);

            // 验证已被淘汰的冷页（如第 0 行）访问时被物理拦截，零假阳性
            Assert.ThrowsException<IndexOutOfRangeException>(() =>
            {
                var val = view[0, 0];
            });

            // 继续逐行步进推进到最后一行（第 63 行，页 15）
            for (int r = 33; r < 64; r++)
            {
                manager.StepTo(r);
            }

            Assert.AreEqual(6300.0, view[63, 0]);
            Assert.AreEqual(6301.0, view[63, 1]);
            Assert.IsTrue(manager.BaseRowIndex >= 48, $"BaseRowIndex 应推进到 48 以上，实际为: {manager.BaseRowIndex}");
        }

        private class MockMarketSeriesSource : IMarketSeriesSource
        {
            private readonly List<string> _symbols;
            private readonly List<DateTime> _dates;

            public MockMarketSeriesSource(string[] symbols, int dateCount)
            {
                _symbols = new List<string>(symbols);
                _dates = new List<DateTime>(dateCount);
                var baseDate = new DateTime(2020, 1, 1);
                for (int i = 0; i < dateCount; i++)
                {
                    _dates.Add(baseDate.AddDays(i));
                }
            }

            public IReadOnlyList<string> Symbols => _symbols;
            public IReadOnlyList<string> FieldNames => new List<string> { "close" };
            public IReadOnlyList<DateTime> TradeDates => _dates;

            public Type GetFieldType(string fieldName) => typeof(double);
            public int GetSymbolIndex(string symbol) => _symbols.IndexOf(symbol);
            public int GetDateIndex(DateTime date) => _dates.IndexOf(date);

            public void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged
            {
                int symIdx = _symbols.IndexOf(symbol);
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

