using System;
using BenchmarkDotNet.Attributes;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;

namespace CarrotBacktesting.NET.Benchmark.Micro;

/// <summary>
/// 虚拟经纪商账本（AccountLedger）与撮合引擎（MatchingEngine / OrderBook）单步 Micro 基准测试。
/// 验证资金预冻结、限价/市价撮合判定、订单簿挂单插入与快速移除的纳秒级吞吐及 0 GC 分配。
/// </summary>
[MemoryDiagnoser]
[InProcess]
public class BrokerMatchingMicroBenchmark
{
    private MatchingEngine _engine = null!;
    private AccountLedger _ledger = null!;
    private OrderBook _orderBook = null!;

    private OrderRequest _limitBuyRequest = null!;
    private OrderRequest _marketBuyRequest = null!;
    private DateTime _tradeTime;

    [GlobalSetup]
    public void Setup()
    {
        var config = new BrokerConfig
        {
            InitialCash = 1_000_000_000.0,
            FeeRate = 0.0003,
            Slippage = 0.0001,
            MinFee = 5.0
        };

        _engine = new MatchingEngine(config);
        _ledger = new AccountLedger(config);
        _orderBook = new OrderBook();

        _limitBuyRequest = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 1000m);
        _marketBuyRequest = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 0.0, 1000m);
        _tradeTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 1. 限价单即时触碰撮合判定（纯数学摩擦计算与状态判定）。
    /// </summary>
    [Benchmark]
    public bool MatchingEngine_LimitOrder_Immediate()
    {
        var result = _engine.EvaluateImmediateOrder(_limitBuyRequest, basePrice: 10.0, bar: BarSnapshot.Empty, multiplier: 1.0, _tradeTime);
        return result.IsFilled;
    }

    /// <summary>
    /// 2. 市价单即时滑点冲击撮合判定。
    /// </summary>
    [Benchmark]
    public bool MatchingEngine_MarketOrder_Evaluation()
    {
        var result = _engine.EvaluateImmediateOrder(_marketBuyRequest, basePrice: 10.0, bar: BarSnapshot.Empty, multiplier: 1.0, _tradeTime);
        return result.IsFilled;
    }

    /// <summary>
    /// 3. 账户资产两阶段资金冻结 (TryFreeze)。
    /// </summary>
    [Benchmark]
    public bool AccountLedger_TryFreeze()
    {
        return _ledger.TryFreeze(
            _limitBuyRequest,
            estimatedPrice: 10.0,
            multiplier: 1.0,
            marginRate: 1.0,
            out _,
            out _,
            out _,
            out _,
            out _);
    }

    /// <summary>
    /// 4. 订单簿（OrderBook）FIFO 挂单压入与快速移除。
    /// </summary>
    [Benchmark]
    public bool OrderBook_Add_And_Remove()
    {
        var order = new SimulatedOrder("bench_ord", _limitBuyRequest, _tradeTime);
        _orderBook.Add(order);
        return _orderBook.Remove("bench_ord", out _);
    }
}
