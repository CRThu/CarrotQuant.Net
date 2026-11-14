using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine.Broker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

/// <summary>
/// 模拟真实在线交易柜台（如 CTP、飞马、币安等）。
/// 纯粹实现通用网关接口 IExchangeGateway，绝不包含任何回测仿真专有契约（ISimulatedExchange）。
/// </summary>
internal sealed class MockLiveExchangeGateway : IExchangeGateway
{
    private readonly ConcurrentDictionary<string, OrderRequest> _liveOrders = new(StringComparer.Ordinal);

    public event Action<ExecutionReport>? OnExecution;
    public event Action<string, OrderStatus>? OnOrderStateChanged;

    public void SendOrder(string orderId, OrderRequest request)
    {
        _liveOrders[orderId] = request;
        // 模拟柜台收到委托回报
        Task.Run(() =>
        {
            OnOrderStateChanged?.Invoke(orderId, OrderStatus.Submitted);
        });
    }

    public void CancelOrder(string orderId)
    {
        if (_liveOrders.TryRemove(orderId, out _))
        {
            Task.Run(() =>
            {
                OnOrderStateChanged?.Invoke(orderId, OrderStatus.Canceled);
            });
        }
    }

    /// <summary>
    /// 模拟交易所撮合成交推送。
    /// </summary>
    public void SimulateTradeFill(string orderId, double fillPrice, decimal fillQuantity, double commission)
    {
        if (_liveOrders.TryGetValue(orderId, out var req))
        {
            var report = new ExecutionReport(orderId, req.Symbol, fillPrice, fillQuantity, commission, DateTime.UtcNow);
            OnExecution?.Invoke(report);
        }
    }

    /// <summary>
    /// 模拟交易所废单或拒单推送。
    /// </summary>
    public void SimulateReject(string orderId)
    {
        if (_liveOrders.TryRemove(orderId, out _))
        {
            OnOrderStateChanged?.Invoke(orderId, OrderStatus.Rejected);
        }
    }
}

[TestClass]
public class OnlineTradingIntegrationTests
{
    [TestMethod]
    public void OnlineTrading_SubmitOrder_AsyncExecution_ShouldCommitToLedger()
    {
        var gateway = new MockLiveExchangeGateway();
        var config = new BrokerConfig
        {
            InitialCash = 1_000_000.0,
            AllowShort = false,
            SettleDays = 0,
            FeeRate = 0.0003
        };
        var broker = new Broker(gateway, config);

        // 1. 提交买入 1,000 股现货
        var order = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 1000m);
        string orderId = broker.SubmitOrder(order);

        Assert.AreEqual(OrderStatus.Submitted, broker.GetOrderStatus(orderId));
        // 本地两阶段冻结：1000 * 10 = 10000 资金
        Assert.AreEqual(1_000_000.0 - 10000.0 - (10000.0 * 0.0003), broker.GetCash(), 1e-4);

        // 2. 模拟柜台异步成交回报到达
        gateway.SimulateTradeFill(orderId, 10.0, 1000m, 3.0);

        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(orderId));
        Assert.AreEqual(1000m, broker.GetPosition("sh.600000"));
        Assert.AreEqual(1000m, broker.GetAvailablePosition("sh.600000"));
        // 实际现金 = 1,000,000 - 10,000 (货款) - 3.0 (实际手续费) = 989,997.0
        Assert.AreEqual(989997.0, broker.GetCash(), 1e-4);
        Assert.AreEqual(989997.0 + 10000.0, broker.GetTotalEquity(), 1e-4);
    }

    [TestMethod]
    public void OnlineTrading_PartialFills_ProportionalUnfreezeAndAccumulate()
    {
        var gateway = new MockLiveExchangeGateway();
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            AllowShort = false,
            SettleDays = 0,
            FeeRate = 0.0
        };
        var broker = new Broker(gateway, config);

        // 提交 100 股买单 @ 10 元 (冻结 1000 元)
        var order = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 100m);
        string orderId = broker.SubmitOrder(order);
        Assert.AreEqual(99000.0, broker.GetCash());

        // 批次 1: 成交 30 股
        gateway.SimulateTradeFill(orderId, 10.0, 30m, 0.0);
        Assert.AreEqual(OrderStatus.PartialFilled, broker.GetOrderStatus(orderId));
        Assert.AreEqual(30m, broker.GetPosition("sh.600000"));
        // 冻结资金从 1000 释放 300，扣除 300，现金仍显示可用 99000
        Assert.AreEqual(99000.0, broker.GetCash());

        // 批次 2: 成交剩余 70 股 -> 全部成交
        gateway.SimulateTradeFill(orderId, 10.0, 70m, 0.0);
        Assert.AreEqual(OrderStatus.Filled, broker.GetOrderStatus(orderId));
        Assert.AreEqual(100m, broker.GetPosition("sh.600000"));
        Assert.AreEqual(99000.0, broker.GetCash());
        Assert.AreEqual(100_000.0, broker.GetTotalEquity());
    }

    [TestMethod]
    public void OnlineTrading_OrderRejectionByCounter_ShouldUnfreezeFundsCleanly()
    {
        var gateway = new MockLiveExchangeGateway();
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            FeeRate = 0.0
        };
        var broker = new Broker(gateway, config);

        var order = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 1000m);
        string orderId = broker.SubmitOrder(order);
        Assert.AreEqual(90000.0, broker.GetCash());

        // 模拟柜台回报拒单
        gateway.SimulateReject(orderId);

        Assert.AreEqual(OrderStatus.Rejected, broker.GetOrderStatus(orderId));
        Assert.AreEqual(100_000.0, broker.GetCash(), "拒单后冻结资金必须 100% 安全原路释放");
    }

    [TestMethod]
    public void OnlineTrading_ReconnectionAndStateSync_ShouldResetAndAlign()
    {
        var gateway = new MockLiveExchangeGateway();
        var config = new BrokerConfig { InitialCash = 100_000.0 };
        var broker = new Broker(gateway, config);

        // 提交两笔在途订单
        string id1 = broker.SubmitOrder(new OrderRequest("s1", OrderDirection.Buy, OrderType.Limit, 10.0, 500m));
        string id2 = broker.SubmitOrder(new OrderRequest("s2", OrderDirection.Buy, OrderType.Limit, 20.0, 500m));

        // 断线重连，收到柜台权威资产与持仓同步
        broker.SyncState(200_000.0, new Dictionary<string, decimal>
        {
            ["s1"] = 1500m,
            ["s2"] = 800m
        });

        // 验证旧在途订单已安全标记为 Canceled
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(id1));
        Assert.AreEqual(OrderStatus.Canceled, broker.GetOrderStatus(id2));
        Assert.AreEqual(200_000.0, broker.GetCash());
        Assert.AreEqual(1500m, broker.GetPosition("s1"));
        Assert.AreEqual(800m, broker.GetPosition("s2"));
    }

    [TestMethod]
    public void OnlineTrading_RealTimeMarkToMarket_WithLiveQuoteFeed()
    {
        var gateway = new MockLiveExchangeGateway();
        var config = new BrokerConfig
        {
            InitialCash = 100_000.0,
            FeeRate = 0.0
        };
        var broker = new Broker(gateway, config);

        broker.SyncState(50_000.0, new Dictionary<string, decimal>
        {
            ["AAPL"] = 100m,
            ["MSFT"] = 50m
        });

        // 模拟外部行情源（Market Data Feed）
        var liveQuoteFeed = new Dictionary<string, double>
        {
            ["AAPL"] = 200.0,
            ["MSFT"] = 400.0
        };

        // 动态盯市权益 = 现金 50,000 + (100 * 200) + (50 * 400) = 50,000 + 20,000 + 20,000 = 90,000
        double equity = broker.GetTotalEquity(sym => liveQuoteFeed.TryGetValue(sym, out var p) ? p : 0.0);
        Assert.AreEqual(90_000.0, equity, 1e-4);
    }

    [TestMethod]
    public void OnlineTrading_HighConcurrencyStress_ZeroDeadlock_ShouldSucceed()
    {
        // 高并发压力测试：20 个线程并发执行下单、撤单与成交推送，验证 Open Calls 锁隔离机制绝无死锁
        var gateway = new MockLiveExchangeGateway();
        var config = new BrokerConfig
        {
            InitialCash = 10_000_000.0,
            FeeRate = 0.0
        };
        var broker = new Broker(gateway, config);

        var tasks = new List<Task>();
        for (int t = 0; t < 20; t++)
        {
            int threadId = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    string symbol = $"sym_{threadId % 5}";
                    var req = new OrderRequest(symbol, OrderDirection.Buy, OrderType.Limit, 10.0, 10m);
                    string id = broker.SubmitOrder(req);

                    if (i % 2 == 0)
                    {
                        // 模拟柜台推送成交
                        gateway.SimulateTradeFill(id, 10.0, 10m, 0.0);
                    }
                    else
                    {
                        // 模拟撤单
                        broker.CancelOrder(id);
                        gateway.CancelOrder(id);
                    }
                }
            }));
        }

        bool allCompleted = Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));
        Assert.IsTrue(allCompleted, "高并发压测必须在 10 秒内安全完成，绝不允许任何死锁或竞态卡死");
        Assert.IsTrue(broker.GetTotalEquity() > 0.0);
    }
}
