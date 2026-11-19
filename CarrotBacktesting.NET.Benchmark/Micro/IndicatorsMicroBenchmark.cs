using System;
using BenchmarkDotNet.Attributes;
using CarrotBacktesting.NET.Indicators;

namespace CarrotBacktesting.NET.Benchmark.Micro;

/// <summary>
/// 流式指标族（Stream Indicators）单步增量递推更新 Micro 基准测试。
/// 检验各指标单次 Update 的纳秒级延迟与 0 GC 堆内存分配守恒。
/// </summary>
[MemoryDiagnoser]
[InProcess]
public class IndicatorsMicroBenchmark
{
    private Sma _sma = null!;
    private Ema _ema = null!;
    private Atr _atr = null!;
    private Rsi _rsi = null!;
    private Macd _macd = null!;
    private BollingerBands _bollinger = null!;
    private Kdj _kdj = null!;

    private double[] _opens = null!;
    private double[] _highs = null!;
    private double[] _lows = null!;
    private double[] _closes = null!;
    private double[] _volumes = null!;

    private const int N = 1000;

    [GlobalSetup]
    public void Setup()
    {
        _sma = new Sma(20);
        _ema = new Ema(20);
        _atr = new Atr(14);
        _rsi = new Rsi(14);
        _macd = new Macd(12, 26, 9);
        _bollinger = new BollingerBands(20, 2.0);
        _kdj = new Kdj(9, 3, 3);

        _opens = new double[N];
        _highs = new double[N];
        _lows = new double[N];
        _closes = new double[N];
        _volumes = new double[N];

        for (int i = 0; i < N; i++)
        {
            double basePrice = 100.0 + Math.Sin(i * 0.1) * 10.0;
            _opens[i] = basePrice;
            _highs[i] = basePrice + 1.5;
            _lows[i] = basePrice - 1.2;
            _closes[i] = basePrice + 0.3;
            _volumes[i] = 10000.0 + i;
        }
    }

    [Benchmark(OperationsPerInvoke = N)]
    public double Sma_Update()
    {
        for (int i = 0; i < N; i++)
        {
            _sma.Update(_opens[i], _highs[i], _lows[i], _closes[i], _volumes[i]);
        }
        return _sma.Current;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public double Ema_Update()
    {
        for (int i = 0; i < N; i++)
        {
            _ema.Update(_opens[i], _highs[i], _lows[i], _closes[i], _volumes[i]);
        }
        return _ema.Current;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public double Atr_Update()
    {
        for (int i = 0; i < N; i++)
        {
            _atr.Update(_opens[i], _highs[i], _lows[i], _closes[i], _volumes[i]);
        }
        return _atr.Current;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public double Rsi_Update()
    {
        for (int i = 0; i < N; i++)
        {
            _rsi.Update(_opens[i], _highs[i], _lows[i], _closes[i], _volumes[i]);
        }
        return _rsi.Current;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public double Macd_Update()
    {
        for (int i = 0; i < N; i++)
        {
            _macd.Update(_opens[i], _highs[i], _lows[i], _closes[i], _volumes[i]);
        }
        return _macd.Current;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public double BollingerBands_Update()
    {
        for (int i = 0; i < N; i++)
        {
            _bollinger.Update(_opens[i], _highs[i], _lows[i], _closes[i], _volumes[i]);
        }
        return _bollinger.Current;
    }

    [Benchmark(OperationsPerInvoke = N)]
    public double Kdj_Update()
    {
        for (int i = 0; i < N; i++)
        {
            _kdj.Update(_opens[i], _highs[i], _lows[i], _closes[i], _volumes[i]);
        }
        return _kdj.Current;
    }
}
