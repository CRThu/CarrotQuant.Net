using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Benchmark;

/// <summary>
/// 纯内存自包含合成行情数据源。
/// 零磁盘 I/O 依赖、零外部文件、无副作用，提供任意规模的标的与时间步模拟行情。
/// </summary>
public sealed class SyntheticMarketGenerator : ITimeseriesSource
{
    private readonly List<string> _symbols;
    private readonly List<MarketTime> _tradeDates;
    private readonly List<string> _fields = new() { "close", "open", "high", "low", "volume", "amount" };

    /// <summary>
    /// 初始化指定标的数与时间步数的合成行情数据源。
    /// </summary>
    /// <param name="symbolCount">标的股票数量（如 1 ~ 10,000）</param>
    /// <param name="timeSteps">时间步数（如 240 根分钟 Bar 或 1,200 个交易日）</param>
    /// <param name="isMinute">是否为 1 分钟级高频时间轴（默认 true）</param>
    public SyntheticMarketGenerator(int symbolCount, int timeSteps, bool isMinute = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(symbolCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeSteps);

        _symbols = new List<string>(symbolCount);
        for (int i = 0; i < symbolCount; i++)
        {
            _symbols.Add("sym_" + i.ToString("D4"));
        }

        _tradeDates = new List<MarketTime>(timeSteps);
        var baseTime = new MarketTime(2025, 1, 2, 9, 30, 0);
        for (int i = 0; i < timeSteps; i++)
        {
            _tradeDates.Add(isMinute ? baseTime.AddMinutes(i) : baseTime.AddDays(i));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Symbols => _symbols;

    /// <inheritdoc />
    public IReadOnlyList<MarketTime> TradeDates => _tradeDates;

    /// <inheritdoc />
    public IReadOnlyList<string> FieldNames => _fields;

    /// <inheritdoc />
    public Type GetFieldType(string fieldName) => typeof(double);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetSymbolIndex(string symbol)
    {
        if (symbol.StartsWith("sym_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(symbol.AsSpan(4), out int idx) && idx < _symbols.Count)
        {
            return idx;
        }
        return _symbols.IndexOf(symbol);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetDateIndex(MarketTime date) => _tradeDates.BinarySearch(date);

    /// <inheritdoc />
    public void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged
    {
        if (typeof(T) != typeof(double))
        {
            destination.Clear();
            return;
        }

        int symIdx = GetSymbolIndex(symbol);
        if (symIdx < 0) symIdx = 0;

        var dst = MemoryMarshal.Cast<T, double>(destination);

        // 基于正弦波与标的基价构造自然的周期性震荡波形，产生金叉与死叉信号
        double basePrice = 50.0 + (symIdx % 100);
        double freq = 0.08 + (symIdx % 10) * 0.005;

        for (int i = 0; i < length; i++)
        {
            int step = startIndex + i;
            double wave = Math.Sin((step + symIdx * 3) * freq) * 5.0;
            double close = basePrice + wave;

            if (fieldName.Equals("close", StringComparison.OrdinalIgnoreCase))
            {
                dst[i] = close;
            }
            else if (fieldName.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                dst[i] = close - 0.15;
            }
            else if (fieldName.Equals("high", StringComparison.OrdinalIgnoreCase))
            {
                dst[i] = close + 0.35;
            }
            else if (fieldName.Equals("low", StringComparison.OrdinalIgnoreCase))
            {
                dst[i] = close - 0.35;
            }
            else if (fieldName.Equals("volume", StringComparison.OrdinalIgnoreCase))
            {
                dst[i] = 1000.0 + (step % 50) * 20.0;
            }
            else // amount
            {
                dst[i] = close * (1000.0 + (step % 50) * 20.0);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _symbols.Clear();
        _tradeDates.Clear();
    }
}
