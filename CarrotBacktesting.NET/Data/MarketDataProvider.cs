using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data;

/// <summary>
/// 行情与复权数据提供器实现。
/// 提供时序数据读取、行切片缓存与复权因子双向折算。
/// </summary>
public sealed class MarketDataProvider : IMarketDataProvider, IDisposable
{
    private readonly ITimeseriesRegistry _timeseries;
    private readonly HashSet<string> _priceFields;

    // 动态矩阵缓存，按需延迟获取并复用已解析的时序矩阵（使用 TryGetValue 杜绝热路径委托分配）
    private readonly ConcurrentDictionary<string, IReadOnlyMatrix2D<double>?> _matrixCache = new(StringComparer.OrdinalIgnoreCase);

    // 复权因子矩阵与时步映射
    private IReadOnlyMatrix2D<double>? _factorMatrix;
    private int[]? _stepToDateIndex;

    // 单步复权行切片缓存，按字段名隔离复用缓冲区
    private sealed class RowBufferEntry
    {
        public int TargetIndex = -1;
        public double[] Buffer = Array.Empty<double>();
    }

    // 基于线程局部存储维护行缓冲区，保障多策略并发管线下的线程隔离与 0 堆分配
    private readonly ThreadLocal<Dictionary<string, RowBufferEntry>> _threadLocalBuffers =
        new(() => new Dictionary<string, RowBufferEntry>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// 直接装配构造函数（支持直接注入已解析的复权因子矩阵与时步映射，供单元测试与显式装配使用）。
    /// </summary>
    public MarketDataProvider(
        ITimeseriesRegistry timeseries,
        IReadOnlyMatrix2D<double>? factorMatrix = null,
        int[]? stepToDateIndex = null,
        IEnumerable<string>? priceFields = null)
    {
        _timeseries = timeseries ?? throw new ArgumentNullException(nameof(timeseries));
        _factorMatrix = factorMatrix;
        _stepToDateIndex = stepToDateIndex;

        _priceFields = new HashSet<string>(AdjustmentConfig.DefaultPriceFields, StringComparer.OrdinalIgnoreCase);
        if (priceFields != null)
        {
            foreach (var f in priceFields)
            {
                if (!string.IsNullOrWhiteSpace(f)) _priceFields.Add(f);
            }
        }
    }

    /// <summary>
    /// 自闭环装配构造函数（数据层根据复权配置与外部依赖自行解析加载复权矩阵，构造即就绪）。
    /// </summary>
    public MarketDataProvider(
        ITimeseriesRegistry timeseries,
        AdjustmentConfig adjustConfig,
        IStorageResolver? resolver = null,
        IEventRegistry? events = null,
        IMonitor? monitor = null)
    {
        _timeseries = timeseries ?? throw new ArgumentNullException(nameof(timeseries));
        ArgumentNullException.ThrowIfNull(adjustConfig);

        _priceFields = new HashSet<string>(adjustConfig.PriceFields ?? (IEnumerable<string>)AdjustmentConfig.DefaultPriceFields, StringComparer.OrdinalIgnoreCase);

        if (adjustConfig.Mode == AdjustmentMode.Post)
        {
            var (factorMatrix, stepToDateIndex) = AdjustmentProvider.Resolve(
                adjustConfig,
                timeseries.Metadata,
                resolver: resolver,
                events: events,
                timeseries: timeseries,
                monitor: monitor);

            _factorMatrix = factorMatrix;
            _stepToDateIndex = stepToDateIndex;
        }
    }

    /// <inheritdoc />
    public IMarketMetadata Metadata => _timeseries.Metadata;

    /// <inheritdoc />
    public ITimeseriesRegistry Timeseries => _timeseries;

    /// <inheritdoc />
    public bool HasAdjustments => _factorMatrix != null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IReadOnlyMatrix2D<double>? GetMatrix(string fieldName)
    {
        if (_matrixCache.TryGetValue(fieldName, out var matrix))
        {
            return matrix;
        }
        return LoadMatrix(fieldName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private IReadOnlyMatrix2D<double>? LoadMatrix(string fieldName)
    {
        var m = _timeseries.HasField(fieldName) ? _timeseries.GetMatrix<double>(fieldName) : null;
        _matrixCache[fieldName] = m;
        return m;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetFactor(int symbolIndex, int stepIndex)
    {
        if (symbolIndex < 0 || stepIndex < 0) return double.NaN;
        if (_factorMatrix == null) return 1.0;

        int dateRow = (_stepToDateIndex != null && (uint)stepIndex < (uint)_stepToDateIndex.Length)
            ? _stepToDateIndex[stepIndex]
            : stepIndex;

        if ((uint)dateRow >= (uint)_factorMatrix.RowCount || (uint)symbolIndex >= (uint)_factorMatrix.Width)
        {
            return double.NaN;
        }

        double factor = _factorMatrix[dateRow, symbolIndex];
        return (double.IsNaN(factor) || factor <= 0.0) ? double.NaN : factor;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetField(int symbolIndex, string fieldName, int stepIndex, PriceMode mode = PriceMode.Raw)
    {
        if (symbolIndex < 0 || stepIndex < 0) return double.NaN;

        var matrix = GetMatrix(fieldName);
        if (matrix == null || (uint)stepIndex >= (uint)matrix.RowCount || (uint)symbolIndex >= (uint)matrix.Width)
        {
            return double.NaN;
        }

        double raw = matrix[stepIndex, symbolIndex];
        if (mode != PriceMode.Post || _factorMatrix == null || !_priceFields.Contains(fieldName))
        {
            return raw;
        }

        double factor = GetFactor(symbolIndex, stepIndex);
        return double.IsNaN(factor) ? double.NaN : raw * factor;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetField(string symbol, string fieldName, int stepIndex, PriceMode mode = PriceMode.Raw)
    {
        int col = Metadata.GetSymbolIndex(symbol);
        return col >= 0 ? GetField(col, fieldName, stepIndex, mode) : double.NaN;
    }

    /// <inheritdoc />
    public ReadOnlySpan<double> GetRow(string fieldName, int stepIndex, PriceMode mode = PriceMode.Raw)
    {
        if (string.IsNullOrEmpty(fieldName) || stepIndex < 0)
        {
            return ReadOnlySpan<double>.Empty;
        }

        var matrix = GetMatrix(fieldName);
        if (matrix == null || (uint)stepIndex >= (uint)matrix.RowCount)
        {
            return ReadOnlySpan<double>.Empty;
        }

        int width = matrix.Width;
        ReadOnlySpan<double> rawRow = matrix.GetRowView(stepIndex, 0, width);

        if (mode != PriceMode.Post || _factorMatrix == null || !_priceFields.Contains(fieldName))
        {
            return rawRow;
        }

        int dateRow = (_stepToDateIndex != null && (uint)stepIndex < (uint)_stepToDateIndex.Length)
            ? _stepToDateIndex[stepIndex]
            : stepIndex;

        if ((uint)dateRow >= (uint)_factorMatrix.RowCount)
        {
            return rawRow;
        }

        ReadOnlySpan<double> factorRow = _factorMatrix.GetRowView(dateRow, 0, Math.Min(width, _factorMatrix.Width));

        var rowBuffers = _threadLocalBuffers.Value!;
        if (!rowBuffers.TryGetValue(fieldName, out var entry))
        {
            entry = new RowBufferEntry();
            rowBuffers[fieldName] = entry;
        }

        if (entry.Buffer.Length < width)
        {
            entry.Buffer = new double[width];
            entry.TargetIndex = -1;
        }

        if (entry.TargetIndex == stepIndex)
        {
            return new ReadOnlySpan<double>(entry.Buffer, 0, width);
        }

        double[] buf = entry.Buffer;
        int limit = Math.Min(width, factorRow.Length);
        for (int i = 0; i < limit; i++)
        {
            double factor = factorRow[i];
            buf[i] = (factor <= 0.0 || double.IsNaN(factor)) ? double.NaN : rawRow[i] * factor;
        }
        for (int i = limit; i < width; i++)
        {
            buf[i] = double.NaN;
        }

        entry.TargetIndex = stepIndex;
        return new ReadOnlySpan<double>(buf, 0, width);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double ReadValue(string fieldName, int symbolIndex, int stepIndex)
    {
        var m = GetMatrix(fieldName);
        if (m != null && (uint)stepIndex < (uint)m.RowCount && (uint)symbolIndex < (uint)m.Width)
        {
            return m[stepIndex, symbolIndex];
        }
        return double.NaN;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BarSnapshot GetBar(int symbolIndex, int stepIndex, PriceMode mode = PriceMode.Raw)
    {
        if (symbolIndex < 0 || stepIndex < 0) return BarSnapshot.Empty;

        double open = ReadValue(StandardFields.Open, symbolIndex, stepIndex);
        double high = ReadValue(StandardFields.High, symbolIndex, stepIndex);
        double low = ReadValue(StandardFields.Low, symbolIndex, stepIndex);
        double close = ReadValue(StandardFields.Close, symbolIndex, stepIndex);
        double volume = ReadValue(StandardFields.Volume, symbolIndex, stepIndex);
        double amount = ReadValue(StandardFields.Amount, symbolIndex, stepIndex);

        // 四价全部为 NaN 时，断定为完全无行情数据（如停牌）
        if (double.IsNaN(open) && double.IsNaN(high) && double.IsNaN(low) && double.IsNaN(close))
        {
            return BarSnapshot.Empty;
        }

        if (mode == PriceMode.Post && _factorMatrix != null)
        {
            double factor = GetFactor(symbolIndex, stepIndex);
            if (!double.IsNaN(factor))
            {
                if (!double.IsNaN(open)) open *= factor;
                if (!double.IsNaN(high)) high *= factor;
                if (!double.IsNaN(low)) low *= factor;
                if (!double.IsNaN(close)) close *= factor;
            }
            else
            {
                open = double.NaN;
                high = double.NaN;
                low = double.NaN;
                close = double.NaN;
            }
        }

        return new BarSnapshot(open, high, low, close, volume, amount);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BarSnapshot GetBar(string symbol, int stepIndex, PriceMode mode = PriceMode.Raw)
    {
        int col = Metadata.GetSymbolIndex(symbol);
        return col >= 0 ? GetBar(col, stepIndex, mode) : BarSnapshot.Empty;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToRawPrice(int symbolIndex, double postPrice, int stepIndex)
    {
        if (double.IsNaN(postPrice) || double.IsInfinity(postPrice)) return double.NaN;
        if (_factorMatrix == null) return postPrice;
        double factor = GetFactor(symbolIndex, stepIndex);
        return (double.IsNaN(factor) || factor <= 0.0) ? double.NaN : postPrice / factor;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToRawPrice(string symbol, double postPrice, int stepIndex)
    {
        int idx = Metadata.GetSymbolIndex(symbol);
        return idx >= 0 ? ToRawPrice(idx, postPrice, stepIndex) : double.NaN;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToPostPrice(int symbolIndex, double rawPrice, int stepIndex)
    {
        if (double.IsNaN(rawPrice) || double.IsInfinity(rawPrice)) return double.NaN;
        if (_factorMatrix == null) return rawPrice;
        double factor = GetFactor(symbolIndex, stepIndex);
        return (double.IsNaN(factor) || factor <= 0.0) ? double.NaN : rawPrice * factor;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToPostPrice(string symbol, double rawPrice, int stepIndex)
    {
        int idx = Metadata.GetSymbolIndex(symbol);
        return idx >= 0 ? ToPostPrice(idx, rawPrice, stepIndex) : double.NaN;
    }

    /// <summary>
    /// 释放持有的非托管复权资源。
    /// </summary>
    public void Dispose()
    {
        (_factorMatrix as IDisposable)?.Dispose();
        _factorMatrix = null;
        _stepToDateIndex = null;
        _threadLocalBuffers.Dispose();
    }
}
