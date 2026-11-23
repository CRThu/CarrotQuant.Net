using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Indicators;

namespace CarrotBacktesting.NET.Benchmark;

/// <summary>
/// 标准工业级双均线动量基准策略（SMA5 vs SMA20）。
/// 用于测试端到端典型全流程回测（行情读取 + 双指标池计算 + 状态判断 + 挂单冻结 + 撮合成交回报）。
/// </summary>
public sealed class DualMaBenchmarkStrategy : IStrategy
{
    public string Name => "DualMaBenchmark";
    public int Lookback => 30;

    private IndicatorPool<Sma>? _fastPool;
    private IndicatorPool<Sma>? _slowPool;

    public void OnStart(IEngineContext context)
    {
        _fastPool = context.Indicators.GetOrAdd("sma5", () => new Sma(5));
        _slowPool = context.Indicators.GetOrAdd("sma20", () => new Sma(20));
    }

    public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
    public void OnStop(IEngineContext context) { }

    public void OnUpdate(IEngineContext context)
    {
        var symbols = context.Symbols;
        int count = symbols.Count;

        for (int i = 0; i < count; i++)
        {
            var fast = _fastPool![i];
            var slow = _slowPool![i];
            if (!fast.IsReady || !slow.IsReady) continue;

            double close = context.GetClose(i);
            if (double.IsNaN(close) || close <= 0) continue;

            string symbol = symbols[i];
            decimal currentPos = context.Broker.GetPosition(symbol);

            // 金叉买入
            if (fast[0] > slow[0] && fast[-1] <= slow[-1] && currentPos == 0)
            {
                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, close, 100m));
            }
            // 死叉卖出
            else if (fast[0] < slow[0] && fast[-1] >= slow[-1] && currentPos > 0)
            {
                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, close, currentPos));
            }
        }
    }
}

/// <summary>
/// 纯空基准策略（OnUpdate 无任何逻辑，测引擎主时钟推进与状态机极限）。
/// </summary>
public sealed class PureEmptyBenchmarkStrategy : IStrategy
{
    public string Name => "PureEmptyBenchmark";
    public int Lookback => 0;
    public void OnStart(IEngineContext context) { }
    public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
    public void OnStop(IEngineContext context) { }
    public void OnUpdate(IEngineContext context) { }
}

/// <summary>
/// 纯行情遍历基准策略（仅遍历读取各标的最新收盘价，无指标无撮合）。
/// 包含 Checksum 累加器以 100% 杜绝 JIT 死代码消除（Dead Code Elimination）优化。
/// </summary>
public sealed class ReadPriceBenchmarkStrategy : IStrategy
{
    public string Name => "ReadPriceBenchmark";
    public int Lookback => 1;
    public double Checksum { get; private set; }

    public void OnStart(IEngineContext context) { }
    public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
    public void OnStop(IEngineContext context) { }

    public void OnUpdate(IEngineContext context)
    {
        int count = context.Symbols.Count;
        double sum = 0.0;
        for (int i = 0; i < count; i++)
        {
            sum += context.GetClose(i);
        }
        Checksum += sum;
    }
}

/// <summary>
/// 随机打乱行情遍历基准策略（本时间步内按随机排列索引点查各标的最新收盘价，测试 CPU Cache 颠簸与离散访问延迟）。
/// 包含 Checksum 累加器以 100% 杜绝 JIT 死代码消除（Dead Code Elimination）优化。
/// </summary>
public sealed class RandomReadPriceBenchmarkStrategy : IStrategy
{
    public string Name => "RandomReadPriceBenchmark";
    public int Lookback => 1;
    public double Checksum { get; private set; }
    private int[] _shuffledIndices = null!;

    public void OnStart(IEngineContext context)
    {
        int count = context.Symbols.Count;
        _shuffledIndices = new int[count];
        for (int i = 0; i < count; i++) _shuffledIndices[i] = i;
        Random.Shared.Shuffle(_shuffledIndices);
    }

    public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
    public void OnStop(IEngineContext context) { }

    public void OnUpdate(IEngineContext context)
    {
        int count = _shuffledIndices.Length;
        double sum = 0.0;
        for (int i = 0; i < count; i++)
        {
            sum += context.GetClose(_shuffledIndices[i]);
        }
        Checksum += sum;
    }
}

/// <summary>
/// 单指标计算基准策略（注册单项 SMA20 指标池，每步触发全市场指标增量递推更新，零撮合零交易）。
/// 包含 Checksum 累加器以 100% 杜绝 JIT 死代码消除（Dead Code Elimination）优化。
/// </summary>
public sealed class SingleIndicatorBenchmarkStrategy : IStrategy
{
    public string Name => "SingleIndicatorBenchmark";
    public int Lookback => 25;
    public double Checksum { get; private set; }
    private IndicatorPool<Sma>? _smaPool;

    public void OnStart(IEngineContext context)
    {
        _smaPool = context.Indicators.GetOrAdd("sma20", () => new Sma(20));
    }

    public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
    public void OnStop(IEngineContext context) { }

    public void OnUpdate(IEngineContext context)
    {
        int count = context.Symbols.Count;
        double sum = 0.0;
        for (int i = 0; i < count; i++)
        {
            var sma = _smaPool![i];
            sum += sma.Current;
        }
        Checksum += sum;
    }
}

/// <summary>
/// 单买卖交易基准策略（每步发起买卖委托意图，密集触发 Broker 账本冻结与撮合成交，零复杂指标计算）。
/// </summary>
public sealed class SingleTradeBenchmarkStrategy : IStrategy
{
    public string Name => "SingleTradeBenchmark";
    public int Lookback => 1;
    public void OnStart(IEngineContext context) { }
    public void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }
    public void OnStop(IEngineContext context) { }

    public void OnUpdate(IEngineContext context)
    {
        var symbols = context.Symbols;
        int count = symbols.Count;
        int step = context.CurrentIndex;

        for (int i = 0; i < count; i++)
        {
            string symbol = symbols[i];
            double close = context.GetClose(i);
            if (double.IsNaN(close) || close <= 0) continue;

            if (step % 2 == 0)
            {
                context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, close, 100m));
            }
            else
            {
                decimal pos = context.Broker.GetPosition(symbol);
                if (pos > 0)
                {
                    context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, close, pos));
                }
            }
        }
    }
}
