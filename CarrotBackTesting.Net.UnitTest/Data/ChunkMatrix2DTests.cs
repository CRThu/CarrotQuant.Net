using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Data.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data
{
    [TestClass]
    public class ChunkMatrix2DTests
    {
        [TestMethod]
        public void TestConstructor_ValidatesParameters()
        {
            // 验证宽度为 0 或负数抛错
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ChunkMatrix2D<double>(0, 4096));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ChunkMatrix2D<double>(-5, 4096));

            // 验证 PageSize 非 2 的幂抛错
            Assert.ThrowsException<ArgumentException>(() => new ChunkMatrix2D<double>(100, 1000));
            Assert.ThrowsException<ArgumentException>(() => new ChunkMatrix2D<double>(100, 0));
            Assert.ThrowsException<ArgumentException>(() => new ChunkMatrix2D<double>(100, -4096));

            // 验证合法构造（支持 1024, 2048, 4096, 8192 等非硬编码分页大小）
            using var buf1024 = new ChunkMatrix2D<double>(50, 1024);
            Assert.AreEqual(1024, buf1024.PageSize);
            Assert.AreEqual(50, buf1024.Width);

            using var buf4096 = new ChunkMatrix2D<double>(500, 4096, 8000);
            Assert.AreEqual(4096, buf4096.PageSize);
            Assert.AreEqual(500, buf4096.Width);
            Assert.AreEqual(8000, buf4096.RowCount);
            Assert.AreEqual(2, buf4096.PageCount); // (8000 + 4095) / 4096 = 2 页
        }

        [TestMethod]
        public void TestElementReadWrite_And_IndexBounds()
        {
            using var buffer = new ChunkMatrix2D<double>(width: 10, pageSize: 1024);

            // 动态扩容写入
            buffer.SetElement(0, 0, 123.456);
            buffer.SetElement(2050, 9, 789.012);

            Assert.AreEqual(2051, buffer.RowCount);
            Assert.AreEqual(123.456, buffer[0, 0]);
            Assert.AreEqual(789.012, buffer[2050, 9]);

            // 通过只读接口访问
            IReadOnlyMatrix2D<double> readOnly = buffer;
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

            using var buffer = new ChunkMatrix2D<double>(width: 10, pageSize: pageSize);

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
            using var buffer = new ChunkMatrix2D<float>(width: width, pageSize: 1024);

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
            IReadOnlyMatrix2D<float> readOnly = buffer;
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
            using var buffer = new ChunkMatrix2D<long>(width: 20, pageSize: pageSize, initialRowCount: 3000);

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
        public void TestMultiThreaded_HighPerformanceRead_Reliability()
        {
            // 构造 10,000 行 x 500 列的行情矩阵（跨越 3 个 4096 页面）
            const int rows = 10000;
            const int cols = 500;
            using var buffer = new ChunkMatrix2D<double>(width: cols, pageSize: 4096, initialRowCount: rows);

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

            IReadOnlyMatrix2D<double> roBuffer = buffer;

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
        public void TestConcurrent_WriterExpandsCapacity_ReaderLockFreeReads()
        {
            // 验证当写线程不断扩容甚至跨页分配新页时，并发读线程在 Acquire 屏障保护下绝对不会抛出 IndexOutOfRangeException 或崩溃
            const int width = 100;
            const int pageSize = 128;
            using var matrix = new ChunkMatrix2D<long>(width: width, pageSize: pageSize, initialRowCount: 100);

            for (int r = 0; r < 100; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    matrix.SetElement(r, c, r * 1000L + c);
                }
            }

            var roMatrix = matrix.AsReadOnly();
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
            var token = cts.Token;

            var readTask = Task.Run(() =>
            {
                var rand = new Random(42);
                long sum = 0;
                while (!token.IsCancellationRequested)
                {
                    int currentRows = roMatrix.RowCount;
                    if (currentRows > 0)
                    {
                        int r = rand.Next(0, currentRows);
                        int c = rand.Next(0, roMatrix.Width);
                        long val = roMatrix[r, c];
                        sum += val;
                    }
                }
                return sum;
            });

            var writeTask = Task.Run(() =>
            {
                for (int targetRows = 200; targetRows <= 5000; targetRows += 200)
                {
                    matrix.EnsureCapacity(targetRows);
                    for (int c = 0; c < width; c++)
                    {
                        matrix.SetElement(targetRows - 1, c, (targetRows - 1) * 1000L + c);
                    }
                    System.Threading.Thread.Sleep(5);
                }
            });

            Task.WaitAll(writeTask);
            cts.Cancel();
            Task.WaitAll(readTask);

            Assert.IsTrue(matrix.RowCount >= 5000);
        }

        [TestMethod]
        public void TestDispose_Idempotency_And_ThrowsAfterDisposed()
        {
            var buffer = new ChunkMatrix2D<double>(width: 10, pageSize: 1024, initialRowCount: 2000);
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
            using var buffer = new ChunkMatrix2D<int>(width: 100, pageSize: 1024);
            Assert.AreEqual(0, buffer.RowCount);
        }

        [TestMethod]
        public void TestSetElement_NegativeRow_ShouldThrow()
        {
            using var buffer = new ChunkMatrix2D<int>(width: 100, pageSize: 1024);
            Assert.ThrowsException<IndexOutOfRangeException>(() => buffer.SetElement(-1, 0, 100));
        }

        [TestMethod]
        public void TestRowView_ModifyElement_ReflectedInParent()
        {
            using var buffer = new ChunkMatrix2D<int>(width: 50, pageSize: 1024);
            buffer.SetElement(0, 5, 100);

            var rowView = buffer.GetRowView(0, 0, 10);
            rowView[5] = 999;

            Assert.AreEqual(999, buffer[0, 5]);
        }

        [TestMethod]
        public void TestColumnView_ModifyElement_ReflectedInParent()
        {
            using var buffer = new ChunkMatrix2D<double>(width: 50, pageSize: 1024, initialRowCount: 20);
            buffer.SetElement(10, 2, 55.5);

            var colView = buffer.GetColumnView(5, 2, 10);
            colView[5] = 77.7; // 对应全局第 10 行

            Assert.AreEqual(77.7, buffer[10, 2]);
        }

        [TestMethod]
        public void TestAsReadOnly_VerifyIsolation()
        {
            using var buffer = new ChunkMatrix2D<int>(width: 50, pageSize: 1024);
            buffer.SetElement(0, 0, 100);

            IReadOnlyMatrix2D<int> readOnly = buffer.AsReadOnly();
            Assert.AreEqual(100, readOnly[0, 0]);

            var rowView = readOnly.GetRowView(0, 0, 1);
            Assert.AreEqual(100, rowView[0]);
        }

        [TestMethod]
        public async Task TestConcurrentWriters_DifferentRows_AllDataCorrect()
        {
            const int writeCount = 1000;
            using var buffer = new ChunkMatrix2D<int>(width: 10, pageSize: 64);

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
            using var buffer = new ChunkMatrix2D<int>(width: 10, pageSize: 64);
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
            using var buffer = new ChunkMatrix2D<double>(width: 4, pageSize: 64, initialRowCount: 100);
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

            IReadOnlyMatrix2D<double> ro = buffer;
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
            using var buffer = new ChunkMatrix2D<double>(width: 4, pageSize: 64, initialRowCount: 10);

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

            IReadOnlyMatrix2D<double> ro = buffer;
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
        public void TestIMatrix2D_AndViews_FullContract()
        {
            // 验证通过 IMatrix2D 抽象契约多态调用各操作与视图
            using IMatrix2D<int> buffer = new ChunkMatrix2D<int>(width: 10, pageSize: 64, initialRowCount: 20);

            Assert.AreEqual(10, buffer.Width);
            Assert.AreEqual(20, buffer.RowCount);

            // 1. IMatrix2D.SetElement 与索引器
            buffer.SetElement(2, 3, 42);
            Assert.AreEqual(42, buffer[2, 3]);

            // 2. IMatrix2D.SetRow 与 RowView / AsSpan
            int[] rowData = new int[] { 101, 102, 103 };
            buffer.SetRow(5, 2, rowData);
            RowView<int> rowView = buffer.GetRowView(5, 2, 3);
            Assert.AreEqual(3, rowView.Length);
            Assert.AreEqual(101, rowView[0]);
            Assert.AreEqual(102, rowView[1]);
            Assert.AreEqual(103, rowView[2]);
            Assert.AreEqual(3, rowView.AsSpan().Length);

            // 3. IMatrix2D.SetColumn 与 ColumnView
            int[] colData = new int[] { 201, 202, 203, 204 };
            buffer.SetColumn(10, 4, colData);
            ColumnView<int> colView = buffer.GetColumnView(10, 4, 4);
            Assert.AreEqual(4, colView.Length);
            Assert.AreEqual(201, colView[0]);
            Assert.AreEqual(204, colView[3]);

            // 4. 只读视图 ReadOnlyRowView / ReadOnlyColumnView
            IReadOnlyMatrix2D<int> ro = buffer;
            ReadOnlyRowView<int> roRowView = ro.GetRowView(5, 2, 3);
            Assert.AreEqual(3, roRowView.Length);
            Assert.AreEqual(101, roRowView[0]);
            Assert.AreEqual(3, roRowView.AsSpan().Length);

            ReadOnlyColumnView<int> roColView = ro.GetColumnView(10, 4, 4);
            Assert.AreEqual(4, roColView.Length);
            Assert.AreEqual(201, roColView[0]);
            Assert.AreEqual(204, roColView[3]);

            // 5. 隐式转换与 foreach 枚举支持
            ReadOnlySpan<int> implicitSpan = roRowView;
            Assert.AreEqual(3, implicitSpan.Length);
            Span<int> implicitMutSpan = rowView;
            Assert.AreEqual(3, implicitMutSpan.Length);

            int rowSum = 0;
            foreach (var val in roRowView) rowSum += val;
            Assert.AreEqual(101 + 102 + 103, rowSum);

            int colSum = 0;
            foreach (var val in roColView) colSum += val;
            Assert.AreEqual(201 + 202 + 203 + 204, colSum);

            // 6. 视图越界访问（ref struct 不能在 lambda 闭包中捕获，使用 try-catch 显式断言）
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

        [TestMethod]
        public void TestViews_ComprehensivePaths_ImplicitConversion_Foreach_And_CrossPage()
        {
            // 验证全部视图的隐式转换、foreach 枚举、跨页穿透与空视图路径
            using IMatrix2D<int> buffer = new ChunkMatrix2D<int>(width: 10, pageSize: 64, initialRowCount: 128);

            // 1. RowView 隐式转换为 ReadOnlySpan 与 Span
            buffer.SetRow(1, 0, new int[] { 10, 20, 30 });
            RowView<int> mutRowView = buffer.GetRowView(1, 0, 3);
            ReadOnlySpan<int> roSpanFromMut = mutRowView;
            Span<int> mutSpanFromMut = mutRowView;
            Assert.AreEqual(3, roSpanFromMut.Length);
            Assert.AreEqual(3, mutSpanFromMut.Length);
            Assert.AreEqual(10, roSpanFromMut[0]);
            Assert.AreEqual(30, mutSpanFromMut[2]);

            // 2. RowView 的 foreach 遍历与可写修改
            int sum = 0;
            foreach (ref int item in mutRowView)
            {
                item += 1;
                sum += item;
            }
            Assert.AreEqual(11 + 21 + 31, sum);
            Assert.AreEqual(11, buffer[1, 0]);

            // 3. 跨页 ColumnView 的 foreach 与 ref 遍历修改（跨越 pageSize=64 边界）
            int crossStart = 60;
            int crossLen = 10; // 从 60 到 69，跨越 page0 与 page1
            for (int r = 0; r < crossLen; r++)
            {
                buffer[crossStart + r, 5] = (r + 1) * 100;
            }

            ColumnView<int> crossColView = buffer.GetColumnView(crossStart, 5, crossLen);
            int colSum = 0;
            foreach (ref int val in crossColView)
            {
                colSum += val;
                val += 1; // 原地通过 ref 修改跨页数据
            }
            Assert.AreEqual(100 + 200 + 300 + 400 + 500 + 600 + 700 + 800 + 900 + 1000, colSum);
            Assert.AreEqual(101, buffer[crossStart, 5]);
            Assert.AreEqual(1001, buffer[crossStart + 9, 5]);

            // 4. 只读跨页 ReadOnlyColumnView 的 foreach 验证
            IReadOnlyMatrix2D<int> roBuffer = buffer;
            ReadOnlyColumnView<int> roCrossColView = roBuffer.GetColumnView(crossStart, 5, crossLen);
            int roColSum = 0;
            foreach (var val in roCrossColView)
            {
                roColSum += val;
            }
            Assert.AreEqual(colSum + crossLen, roColSum);

            // 5. 空视图 (Length = 0) 的 foreach 路径安全验证
            RowView<int> emptyRowView = buffer.GetRowView(1, 0, 0);
            int emptyCount = 0;
            foreach (var _ in emptyRowView) emptyCount++;
            Assert.AreEqual(0, emptyCount);

            ReadOnlyRowView<int> roEmptyRowView = roBuffer.GetRowView(1, 0, 0);
            foreach (var _ in roEmptyRowView) emptyCount++;
            Assert.AreEqual(0, emptyCount);

            ColumnView<int> emptyColView = buffer.GetColumnView(1, 0, 0);
            foreach (var _ in emptyColView) emptyCount++;
            Assert.AreEqual(0, emptyCount);

            ReadOnlyColumnView<int> roEmptyColView = roBuffer.GetColumnView(1, 0, 0);
            foreach (var _ in roEmptyColView) emptyCount++;
            Assert.AreEqual(0, emptyCount);
        }

        [TestMethod]
        public void TestChunkMatrix2D_ExpandWidth_PreservesDataAndFillsNaN()
        {
            const int initialWidth = 4;
            const int pageSize = 16;
            using var matrix = new ChunkMatrix2D<double>(initialWidth, pageSize, initialRowCount: 20);

            // 填充 20 行 x 4 列
            for (int r = 0; r < 20; r++)
            {
                for (int c = 0; c < initialWidth; c++)
                {
                    matrix.SetElement(r, c, r * 10.0 + c);
                }
            }

            Assert.AreEqual(4, matrix.Width);
            Assert.AreEqual(20, matrix.RowCount);

            // 拓宽宽度至 7（例如上市 3 只新股票）
            matrix.ExpandWidth(7);

            Assert.AreEqual(7, matrix.Width);
            Assert.AreEqual(20, matrix.RowCount);

            // 验证：旧数据 100% 保真
            for (int r = 0; r < 20; r++)
            {
                for (int c = 0; c < initialWidth; c++)
                {
                    Assert.AreEqual(r * 10.0 + c, matrix[r, c]);
                }
            }

            // 验证：新追加的列（c = 4, 5, 6）全部自动填充 NaN
            for (int r = 0; r < 20; r++)
            {
                for (int c = initialWidth; c < 7; c++)
                {
                    Assert.IsTrue(double.IsNaN(matrix[r, c]));
                }
            }

            // 若请求宽度小于等于当前宽度，幂等无损
            matrix.ExpandWidth(5);
            Assert.AreEqual(7, matrix.Width);
        }

        [TestMethod]
        public void TestRingBufferMode_EvictsOldPages_And_KeepsSlotsBounded()
        {
            // 单页 16 行，常驻 4 个分页，总容量 64 行
            const int pageSize = 16;
            const int maxPages = 4;
            const int width = 3;

            using var matrix = new ChunkMatrix2D<double>(width, pageSize, 0, maxPages);
            Assert.AreEqual(4, matrix.MaxPages);
            Assert.AreEqual(0, matrix.BaseRowIndex);

            // 1. 写入前 64 行（刚好占满 4 个槽位：逻辑页 0, 1, 2, 3）
            for (int r = 0; r < 64; r++)
            {
                matrix.SetRow(r, 0, new double[] { r, r * 10, r * 100 });
            }

            Assert.AreEqual(64, matrix.RowCount);
            Assert.AreEqual(0, matrix.BaseRowIndex);
            Assert.AreEqual(4, matrix.PageCount);

            // 验证 0..63 行全部可正常访问
            for (int r = 0; r < 64; r++)
            {
                Assert.AreEqual(r, matrix[r, 0]);
                Assert.AreEqual(r * 10, matrix[r, 1]);
            }

            // 2. 继续推进写入第 64..79 行（触发第 5 个逻辑页，槽位 0 被轮转复用，逻辑页 0 被淘汰）
            for (int r = 64; r < 80; r++)
            {
                matrix.SetRow(r, 0, new double[] { r, r * 10, r * 100 });
            }

            Assert.AreEqual(80, matrix.RowCount);
            // 最老有效基准行推进到 16（逻辑页 1 的起始行）
            Assert.AreEqual(16, matrix.BaseRowIndex);
            Assert.AreEqual(4, matrix.PageCount); // 常驻槽位严格守恒，不膨胀！

            // 3. 断言：已淘汰的逻辑页 0（行 0..15）严格抛出 IndexOutOfRangeException
            Assert.ThrowsException<IndexOutOfRangeException>(() => { var _ = matrix[0, 0]; });
            Assert.ThrowsException<IndexOutOfRangeException>(() => { var _ = matrix[15, 0]; });
            Assert.ThrowsException<IndexOutOfRangeException>(() => { var _ = matrix.GetPageSpan(0, 16); });
            Assert.ThrowsException<IndexOutOfRangeException>(() => { var _ = matrix.GetReadOnlyPageSpan(0, 16); });
            // 超界页同样拦截
            Assert.ThrowsException<IndexOutOfRangeException>(() => { var _ = matrix.GetPageSpan(10, 16); });
            Assert.ThrowsException<IndexOutOfRangeException>(() => { var _ = matrix.GetReadOnlyPageSpan(10, 16); });

            // 4. 断言：当前有效常驻窗口内的数据（行 16..79）精准读取
            for (int r = 16; r < 80; r++)
            {
                Assert.AreEqual(r, matrix[r, 0]);
                Assert.AreEqual(r * 10, matrix[r, 1]);
            }
        }
    }
}

