using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data.Views;
using CommunityToolkit.HighPerformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class ChunkBuffer2DTests
    {
        [TestMethod]
        public void TestConstructor_ValidatesParameters()
        {
            // 验证宽度为 0 或负数抛错
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ChunkBuffer2D<double>(0, 4096));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ChunkBuffer2D<double>(-5, 4096));

            // 验证 PageSize 非 2 的幂抛错
            Assert.ThrowsException<ArgumentException>(() => new ChunkBuffer2D<double>(100, 1000));
            Assert.ThrowsException<ArgumentException>(() => new ChunkBuffer2D<double>(100, 0));
            Assert.ThrowsException<ArgumentException>(() => new ChunkBuffer2D<double>(100, -4096));

            // 验证合法构造（支持 1024, 2048, 4096, 8192 等非硬编码分页大小）
            using var buf1024 = new ChunkBuffer2D<double>(50, 1024);
            Assert.AreEqual(1024, buf1024.PageSize);
            Assert.AreEqual(50, buf1024.Width);

            using var buf4096 = new ChunkBuffer2D<double>(500, 4096, 8000);
            Assert.AreEqual(4096, buf4096.PageSize);
            Assert.AreEqual(500, buf4096.Width);
            Assert.AreEqual(8000, buf4096.RowCount);
            Assert.AreEqual(2, buf4096.PageCount); // (8000 + 4095) / 4096 = 2 页
        }

        [TestMethod]
        public void TestElementReadWrite_And_IndexBounds()
        {
            using var buffer = new ChunkBuffer2D<double>(width: 10, pageSize: 1024);

            // 动态扩容写入
            buffer.SetElement(0, 0, 123.456);
            buffer.SetElement(2050, 9, 789.012);

            Assert.AreEqual(2051, buffer.RowCount);
            Assert.AreEqual(123.456, buffer[0, 0]);
            Assert.AreEqual(789.012, buffer[2050, 9]);

            // 通过只读接口访问
            IReadOnlyBuffer2D<double> readOnly = buffer;
            Assert.AreEqual(123.456, readOnly[0, 0]);
            Assert.AreEqual(789.012, readOnly[2050, 9]);

            // 越界校验
            Assert.ThrowsException<IndexOutOfRangeException>(() => { var _ = buffer[2051, 0]; });
            Assert.ThrowsException<IndexOutOfRangeException>(() => { var _ = buffer[0, 10]; });
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.SetElement(0, 10, 1.0));
        }

        [TestMethod]
        public void TestSetColumn_And_CopyColumn_CrossPageBoundaries()
        {
            // 单页 1024 行，总共写入 5000 行，跨越 5 个分页
            const int totalRows = 5000;
            const int pageSize = 1024;
            const int colIdx = 3;

            using var buffer = new ChunkBuffer2D<double>(width: 10, pageSize: pageSize);

            double[] srcData = new double[totalRows];
            for (int i = 0; i < totalRows; i++)
            {
                srcData[i] = i * 1.5 + 0.1;
            }

            buffer.SetColumn(0, colIdx, srcData);

            Assert.AreEqual(totalRows, buffer.RowCount);
            Assert.AreEqual(5, buffer.PageCount); // (5000 + 1023) / 1024 = 5 页

            // 1. 逐点校验
            for (int i = 0; i < totalRows; i++)
            {
                Assert.AreEqual(srcData[i], buffer[i, colIdx], 1e-9, $"第 {i} 行数据不匹配");
            }

            // 2. 批量回读校验
            double[] readData = new double[totalRows];
            buffer.CopyColumn(0, colIdx, readData);

            for (int i = 0; i < totalRows; i++)
            {
                Assert.AreEqual(srcData[i], readData[i], 1e-9, $"CopyColumn 第 {i} 行数据不匹配");
            }
        }

        [TestMethod]
        public void TestSetRow_And_CopyRow_And_RowView()
        {
            const int width = 100;
            using var buffer = new ChunkBuffer2D<float>(width: width, pageSize: 1024);

            float[] rowData = new float[width];
            for (int i = 0; i < width; i++)
            {
                rowData[i] = i * 2.5f;
            }

            buffer.SetRow(50, 0, rowData);

            Assert.AreEqual(51, buffer.RowCount);

            // 1. CopyRow 校验
            float[] readRow = new float[width];
            buffer.CopyRow(50, 0, readRow);
            CollectionAssert.AreEqual(rowData, readRow);

            // 2. RowView 校验
            var rowView = buffer.GetRowView(50, 10, 20);
            Assert.AreEqual(20, rowView.Length);
            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(rowData[10 + i], rowView[i]);
            }

            // 3. 只读接口 RowView 校验
            IReadOnlyBuffer2D<float> readOnly = buffer;
            var roRowView = readOnly.GetRowView(50, 10, 20);
            Assert.AreEqual(20, roRowView.Length);
            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(rowData[10 + i], roRowView[i]);
            }
        }

        [TestMethod]
        public void TestColumnView_CrossPages()
        {
            const int pageSize = 1024;
            using var buffer = new ChunkBuffer2D<long>(width: 20, pageSize: pageSize, initialRowCount: 3000);

            for (int r = 0; r < 3000; r++)
            {
                buffer[r, 5] = r * 1000L;
            }

            var colView = buffer.GetColumnView(500, 5, 1500); // 跨越 Page 0 (500~1023) 和 Page 1 (1024~1999)
            Assert.AreEqual(1500, colView.Length);

            for (int i = 0; i < 1500; i++)
            {
                long expected = (500 + i) * 1000L;
                Assert.AreEqual(expected, colView[i]);
            }
        }

        [TestMethod]
        public void TestSetBlock_Span2D()
        {
            using var buffer = new ChunkBuffer2D<int>(width: 10, pageSize: 1024);

            int[,] rawMatrix = new int[3, 4]
            {
                { 1, 2, 3, 4 },
                { 5, 6, 7, 8 },
                { 9, 10, 11, 12 }
            };

            var span2d = new ReadOnlySpan2D<int>(rawMatrix);
            buffer.SetBlock(10, 2, span2d);

            Assert.AreEqual(13, buffer.RowCount);
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    Assert.AreEqual(rawMatrix[r, c], buffer[10 + r, 2 + c]);
                }
            }
        }

        [TestMethod]
        public void TestMultiThreaded_HighPerformanceRead_Reliability()
        {
            // 构造 10,000 行 x 500 列的行情矩阵（跨越 3 个 4096 页面）
            const int rows = 10000;
            const int cols = 500;
            using var buffer = new ChunkBuffer2D<double>(width: cols, pageSize: 4096, initialRowCount: rows);

            // 填充确定性特征数据: value = r * 10000 + c
            for (int c = 0; c < cols; c++)
            {
                double[] colData = new double[rows];
                for (int r = 0; r < rows; r++)
                {
                    colData[r] = r * 10000.0 + c;
                }
                buffer.SetColumn(0, c, colData);
            }

            IReadOnlyBuffer2D<double> roBuffer = buffer;

            // 启动 16 个并发工作任务，同时执行不同模式的并发读取：
            // 1. 垂直时序读取（列切片 / 模拟策略提取单股票时序特征）
            // 2. 水平截面读取（行切片 / 模拟全市场因子计算）
            // 3. 随机多点点查（模拟离散事件回溯）
            const int concurrency = 16;
            const int iterationsPerTask = 500;

            Parallel.For(0, concurrency, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, threadId =>
            {
                var rand = new Random(threadId * 1337);

                for (int iter = 0; iter < iterationsPerTask; iter++)
                {
                    int mode = iter % 3;
                    if (mode == 0)
                    {
                        // 垂直列抽样
                        int targetCol = rand.Next(0, cols);
                        int startRow = rand.Next(0, rows - 500);
                        var colView = roBuffer.GetColumnView(startRow, targetCol, 500);
                        for (int k = 0; k < 500; k++)
                        {
                            double expected = (startRow + k) * 10000.0 + targetCol;
                            if (colView[k] != expected)
                            {
                                throw new InvalidOperationException($"并发列读取异常: 期望 {expected}, 实际 {colView[k]}");
                            }
                        }
                    }
                    else if (mode == 1)
                    {
                        // 水平截面抽样
                        int targetRow = rand.Next(0, rows);
                        var rowView = roBuffer.GetRowView(targetRow, 0, cols);
                        for (int c = 0; c < cols; c++)
                        {
                            double expected = targetRow * 10000.0 + c;
                            if (rowView[c] != expected)
                            {
                                throw new InvalidOperationException($"并发行截面读取异常: 期望 {expected}, 实际 {rowView[c]}");
                            }
                        }
                    }
                    else
                    {
                        // 随机点查
                        int r = rand.Next(0, rows);
                        int c = rand.Next(0, cols);
                        double val = roBuffer[r, c];
                        double expected = r * 10000.0 + c;
                        if (val != expected)
                        {
                            throw new InvalidOperationException($"并发点查异常: 期望 {expected}, 实际 {val}");
                        }
                    }
                }
            });
        }

        [TestMethod]
        public void TestDispose_Idempotency_And_ThrowsAfterDisposed()
        {
            var buffer = new ChunkBuffer2D<double>(width: 10, pageSize: 1024, initialRowCount: 2000);
            buffer.SetElement(10, 5, 99.9);

            // 多次 Dispose 不抛出异常
            buffer.Dispose();
            buffer.Dispose();

            // Dispose 后访问应抛出 ObjectDisposedException
            Assert.ThrowsException<ObjectDisposedException>(() => buffer.SetElement(10, 5, 1.0));
            Assert.ThrowsException<ObjectDisposedException>(() => buffer.EnsureCapacity(3000));
            Assert.ThrowsException<ObjectDisposedException>(() => buffer.SetColumn(0, 0, new double[10]));
            Assert.ThrowsException<ObjectDisposedException>(() => buffer.CopyColumn(0, 0, new double[10]));
        }

        [TestMethod]
        public void TestRowCount_Initial_ShouldBeZero()
        {
            using var buffer = new ChunkBuffer2D<int>(width: 100, pageSize: 1024);
            Assert.AreEqual(0, buffer.RowCount);
        }

        [TestMethod]
        public void TestSetElement_NegativeRow_ShouldThrow()
        {
            using var buffer = new ChunkBuffer2D<int>(width: 100, pageSize: 1024);
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.SetElement(-1, 0, 100));
        }

        [TestMethod]
        public void TestSetBlock_InvalidWidthBound_ShouldThrowArgumentException()
        {
            const int width = 100;
            using var buffer = new ChunkBuffer2D<int>(width: width, pageSize: 1024);
            int[,] invalidData = new int[1, width + 1];

            Assert.ThrowsException<ArgumentException>(() => buffer.SetBlock(0, 0, invalidData.AsSpan2D()));
        }

        [TestMethod]
        public void TestRowView_ModifyElement_ReflectedInParent()
        {
            using var buffer = new ChunkBuffer2D<int>(width: 50, pageSize: 1024);
            buffer.SetElement(0, 5, 100);

            var rowView = buffer.GetRowView(0, 0, 10);
            rowView[5] = 999;

            Assert.AreEqual(999, buffer[0, 5]);
        }

        [TestMethod]
        public void TestColumnView_ModifyElement_ReflectedInParent()
        {
            using var buffer = new ChunkBuffer2D<double>(width: 50, pageSize: 1024, initialRowCount: 20);
            buffer.SetElement(10, 2, 55.5);

            var colView = buffer.GetColumnView(5, 2, 10);
            colView[5] = 77.7; // 对应全局第 10 行

            Assert.AreEqual(77.7, buffer[10, 2]);
        }

        [TestMethod]
        public void TestAsReadOnly_VerifyIsolation()
        {
            using var buffer = new ChunkBuffer2D<int>(width: 50, pageSize: 1024);
            buffer.SetElement(0, 0, 100);

            IReadOnlyBuffer2D<int> readOnly = buffer.AsReadOnly();
            Assert.AreEqual(100, readOnly[0, 0]);

            var rowView = readOnly.GetRowView(0, 0, 1);
            Assert.AreEqual(100, rowView[0]);
        }

        [TestMethod]
        public async Task TestConcurrentWriters_DifferentRows_AllDataCorrect()
        {
            const int writeCount = 1000;
            using var buffer = new ChunkBuffer2D<int>(width: 10, pageSize: 64);

            var tasks = new Task[writeCount];
            for (int i = 0; i < writeCount; i++)
            {
                int row = i;
                tasks[i] = Task.Run(() =>
                {
                    buffer.SetElement(row, 0, row * 2);
                });
            }

            await Task.WhenAll(tasks);

            Assert.AreEqual(writeCount, buffer.RowCount);
            for (int i = 0; i < writeCount; i++)
            {
                Assert.AreEqual(i * 2, buffer[i, 0]);
            }
        }

        [TestMethod]
        public async Task TestConcurrentReadersAndWriters_DuringExpansion_Stable()
        {
            const int targetRows = 2000;
            using var buffer = new ChunkBuffer2D<int>(width: 10, pageSize: 64);
            bool running = true;

            var readerTask = Task.Run(() =>
            {
                var rand = new Random(42);
                while (running)
                {
                    int rowCount = buffer.RowCount;
                    if (rowCount > 0)
                    {
                        int r = rand.Next(rowCount);
                        int val = buffer[r, 0];
                        // 验证读取不发生段错误或越界
                        Assert.IsTrue(val >= 0);
                    }
                }
            });

            for (int i = 0; i < targetRows; i++)
            {
                buffer.SetElement(i, 0, i);
            }

            running = false;
            await readerTask;

            Assert.AreEqual(targetRows, buffer.RowCount);
            for (int i = 0; i < targetRows; i++)
            {
                Assert.AreEqual(i, buffer[i, 0]);
            }
        }

        [TestMethod]
        public void TestCopyRowAndColumn_And_RowColumnViews()
        {
            using var buffer = new ChunkBuffer2D<double>(width: 4, pageSize: 64, initialRowCount: 100);
            for (int r = 0; r < 100; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    buffer[r, c] = r * 10 + c;
                }
            }

            // 1. CopyRow
            double[] rowDest = new double[4];
            buffer.CopyRow(5, 0, rowDest);
            Assert.AreEqual(50.0, rowDest[0]);
            Assert.AreEqual(51.0, rowDest[1]);
            Assert.AreEqual(52.0, rowDest[2]);
            Assert.AreEqual(53.0, rowDest[3]);

            // 2. CopyColumn (跨分页读取)
            double[] colDest = new double[80];
            buffer.CopyColumn(10, 2, colDest);
            for (int i = 0; i < 80; i++)
            {
                Assert.AreEqual((10 + i) * 10 + 2, colDest[i]);
            }

            // 3. RowView
            var rowView = buffer.GetRowView(5, 1, 2);
            Assert.AreEqual(2, rowView.Length);
            Assert.AreEqual(51.0, rowView[0]);
            Assert.AreEqual(52.0, rowView[1]);

            IReadOnlyBuffer2D<double> ro = buffer;
            var roRowView = ro.GetRowView(5, 1, 2);
            Assert.AreEqual(2, roRowView.Length);
            Assert.AreEqual(51.0, roRowView[0]);
            Assert.AreEqual(52.0, roRowView[1]);

            // 4. ColumnView
            var colView = buffer.GetColumnView(10, 1, 50);
            Assert.AreEqual(50, colView.Length);
            Assert.AreEqual(101.0, colView[0]);
            Assert.AreEqual(111.0, colView[1]);

            var roColView = ro.GetColumnView(10, 1, 50);
            Assert.AreEqual(50, roColView.Length);
            Assert.AreEqual(101.0, roColView[0]);
            Assert.AreEqual(111.0, roColView[1]);
        }

        [TestMethod]
        public void TestBoundaryExceptions_ThrowsProperly()
        {
            using var buffer = new ChunkBuffer2D<double>(width: 4, pageSize: 64, initialRowCount: 10);

            // SetElement 越界
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.SetElement(-1, 0, 1.0));
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.SetElement(0, 4, 1.0));

            // SetColumn 越界
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.SetColumn(-1, 0, new double[2]));
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.SetColumn(0, 4, new double[2]));

            // SetRow 越界
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.SetRow(-1, 0, new double[2]));
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.SetRow(0, 3, new double[3])); // 3 + 3 > 4

            // CopyColumn / CopyRow 越界
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.CopyColumn(0, 4, new double[2]));
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.CopyColumn(9, 0, new double[5])); // 9 + 5 > 10
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.CopyRow(10, 0, new double[2])); // 10 >= rowCount
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.CopyRow(0, 3, new double[2])); // 3 + 2 > 4

            // View 越界
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.GetRowView(10, 0, 1));
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.GetRowView(0, 3, 2));
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.GetColumnView(0, 4, 1));
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.GetColumnView(9, 0, 5));

            IReadOnlyBuffer2D<double> ro = buffer;
            Assert.ThrowsException<IndexOutOfRangeException>(() => ro.GetRowView(10, 0, 1));
            Assert.ThrowsException<IndexOutOfRangeException>(() => ro.GetRowView(0, 3, 2));
            Assert.ThrowsException<IndexOutOfRangeException>(() => ro.GetColumnView(0, 4, 1));
            Assert.ThrowsException<IndexOutOfRangeException>(() => ro.GetColumnView(9, 0, 5));

            // PageSpan 越界
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.GetPageSpan(10, 10));
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.GetPageSpan(0, 100)); // 100 > pageSize(64)
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.GetReadOnlyPageSpan(10, 10));
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.GetReadOnlyPageSpan(0, 100));
        }

        [TestMethod]
        public void TestIBuffer2D_AndViews_FullContract()
        {
            // 验证通过 IBuffer2D 抽象契约多态调用各操作与视图
            using IBuffer2D<int> buffer = new ChunkBuffer2D<int>(width: 10, pageSize: 64, initialRowCount: 20);

            Assert.AreEqual(10, buffer.Width);
            Assert.AreEqual(20, buffer.RowCount);

            // 1. IBuffer2D.SetElement 与索引器
            buffer.SetElement(2, 3, 42);
            Assert.AreEqual(42, buffer[2, 3]);

            // 2. IBuffer2D.SetRow 与 RowView / AsSpan
            int[] rowData = new int[] { 101, 102, 103 };
            buffer.SetRow(5, 2, rowData);
            RowView<int> rowView = buffer.GetRowView(5, 2, 3);
            Assert.AreEqual(3, rowView.Length);
            Assert.AreEqual(101, rowView[0]);
            Assert.AreEqual(102, rowView[1]);
            Assert.AreEqual(103, rowView[2]);
            Assert.AreEqual(3, rowView.AsSpan().Length);

            // 3. IBuffer2D.SetColumn 与 ColumnView
            int[] colData = new int[] { 201, 202, 203, 204 };
            buffer.SetColumn(10, 4, colData);
            ColumnView<int> colView = buffer.GetColumnView(10, 4, 4);
            Assert.AreEqual(4, colView.Length);
            Assert.AreEqual(201, colView[0]);
            Assert.AreEqual(204, colView[3]);

            // 4. 只读视图 ReadOnlyRowView / ReadOnlyColumnView
            IReadOnlyBuffer2D<int> ro = buffer;
            ReadOnlyRowView<int> roRowView = ro.GetRowView(5, 2, 3);
            Assert.AreEqual(3, roRowView.Length);
            Assert.AreEqual(101, roRowView[0]);
            Assert.AreEqual(3, roRowView.AsSpan().Length);

            ReadOnlyColumnView<int> roColView = ro.GetColumnView(10, 4, 4);
            Assert.AreEqual(4, roColView.Length);
            Assert.AreEqual(201, roColView[0]);
            Assert.AreEqual(204, roColView[3]);

            // 5. 视图越界访问（ref struct 不能在 lambda 闭包中捕获，使用 try-catch 显式断言）
            bool colViewThrew = false;
            try
            {
                var _ = colView[4];
            }
            catch (IndexOutOfRangeException)
            {
                colViewThrew = true;
            }
            Assert.IsTrue(colViewThrew);

            bool roColViewThrew = false;
            try
            {
                var _ = roColView[4];
            }
            catch (IndexOutOfRangeException)
            {
                roColViewThrew = true;
            }
            Assert.IsTrue(roColViewThrew);
        }
    }
}
