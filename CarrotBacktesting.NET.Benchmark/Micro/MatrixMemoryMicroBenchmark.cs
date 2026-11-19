using System;
using BenchmarkDotNet.Attributes;
using CarrotBacktesting.NET.Data;

namespace CarrotBacktesting.NET.Benchmark.Micro;

/// <summary>
/// 二维连续矩阵（ChunkMatrix2D）内存访问单步 Micro 基准测试。
/// 严谨对比：
/// 1. 连续截面行切片访问 (L1/L2 Cache 命中)
/// 2. 本时间步内随机打乱标的索引访问 (Cache Line 穿透与延迟)
/// 3. 二维索引器访问
/// 4. 非托管裸指针直通流式访问
/// </summary>
[MemoryDiagnoser]
[InProcess]
public class MatrixMemoryMicroBenchmark
{
    private ChunkMatrix2D<double> _matrix = null!;
    private int[] _shuffledCols = null!;
    private const int Rows = 240;
    private const int Cols = 5000;

    [GlobalSetup]
    public void Setup()
    {
        _matrix = new ChunkMatrix2D<double>(width: Cols, pageSize: 4096, initialRowCount: Rows);
        var span = _matrix.GetPageSpan(0, Rows);
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = i * 1.5;
        }

        // 构造打乱顺序的随机列索引数组（模拟随机标的点查）
        _shuffledCols = new int[Cols];
        for (int c = 0; c < Cols; c++) _shuffledCols[c] = c;
        Random.Shared.Shuffle(_shuffledCols);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _matrix.Dispose();
    }

    /// <summary>
    /// 1. 连续截面行切片访问（顺序访问 0..Cols-1，充分利用 CPU 缓存行硬件预取）。
    /// </summary>
    [Benchmark(OperationsPerInvoke = Rows * Cols)]
    public double Sequential_RowSpan()
    {
        double sum = 0;
        var fullSpan = _matrix.GetReadOnlyPageSpan(0, Rows);
        for (int r = 0; r < Rows; r++)
        {
            var row = fullSpan.Slice(r * Cols, Cols);
            for (int c = 0; c < Cols; c++)
            {
                sum += row[c];
            }
        }
        return sum;
    }

    /// <summary>
    /// 2. 本时间步内随机打乱标的索引访问（测试随机离散查询的寻址与 Cache 抖动）。
    /// </summary>
    [Benchmark(OperationsPerInvoke = Rows * Cols)]
    public double Random_Column_PointRead()
    {
        double sum = 0;
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                sum += _matrix[r, _shuffledCols[c]];
            }
        }
        return sum;
    }

    /// <summary>
    /// 3. 二维索引器直接访问 [r, c]。
    /// </summary>
    [Benchmark(OperationsPerInvoke = Rows * Cols)]
    public double Direct_2D_Indexer()
    {
        double sum = 0;
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                sum += _matrix[r, c];
            }
        }
        return sum;
    }

    /// <summary>
    /// 4. 非托管裸指针直通连续流式遍历。
    /// </summary>
    [Benchmark(OperationsPerInvoke = Rows * Cols)]
    public unsafe double Raw_Pointer_Stream()
    {
        double sum = 0;
        var fullSpan = _matrix.GetReadOnlyPageSpan(0, Rows);
        fixed (double* pBase = fullSpan)
        {
            double* pCur = pBase;
            double* pEnd = pBase + (Rows * Cols);
            while (pCur < pEnd)
            {
                sum += *pCur++;
            }
        }
        return sum;
    }
}
