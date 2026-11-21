using System;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine.Exchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

/// <summary>
/// 撮合流动性角色（Maker / Taker）与分笔守恒回归测试套件。
/// 验证市价主动吃单与挂单排队被动成交的流动性角色打标、滑点扣除及跳空价格改善逻辑。
/// </summary>
[TestClass]
public class MatchingEngineRoleTests
{
    [TestMethod]
    public void MatchingEngine_ImmediateMarketBuy_ShouldTagTakerAndApplySlippage()
    {
        // 场景：滑点万分之五，基准市价 10.0 元，发出市价买单
        var config = new BrokerConfig
        {
            Slippage = 0.0005,
            FeeRate = 0.0
        };
        var matcher = new MatchingEngine(config);
        var time = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
        var bar = new BarSnapshot(10.0, 10.5, 9.8, 10.0, 100000.0, 1000000.0);

        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 0.0, 1000m);
        var result = matcher.EvaluateImmediateOrder(req, 10.0, in bar, 1.0, time);

        Assert.IsTrue(result.IsFilled);
        Assert.AreEqual(LiquidityRole.Taker, result.Role, "即时成交市价单角色必须为 Taker");
        Assert.AreEqual(10.0 * (1.0 + 0.0005), result.FillPrice, 1e-6, "Taker 必须承担滑点成本");
        Assert.AreEqual(1000m, result.FillQuantity);
    }

    [TestMethod]
    public void MatchingEngine_PendingLimitOrder_OpenGapDown_ShouldTagMakerAndPriceImprove()
    {
        // 场景：委托限价买单 10.0 元，次日开盘跳空低开至 9.5 元
        var config = new BrokerConfig
        {
            Slippage = 0.001, // 即使配置了滑点，Maker 撮合也承担零滑点
            FeeRate = 0.0
        };
        var matcher = new MatchingEngine(config);
        var time = new DateTime(2025, 1, 16, 9, 30, 0, DateTimeKind.Utc);
        // 开盘跳空低开：Open = 9.5, High = 9.8, Low = 9.4, Close = 9.7
        var bar = new BarSnapshot(9.5, 9.8, 9.4, 9.7, 50000.0, 480000.0);

        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.0, 500m);
        var simOrder = new SimulatedOrder("sim-1", req, time.AddDays(-1));

        var result = matcher.EvaluatePendingLimitOrder(simOrder, 9.7, in bar, time);

        Assert.IsTrue(result.IsFilled);
        Assert.AreEqual(LiquidityRole.Maker, result.Role, "挂单簿排队成交角色必须为 Maker");
        Assert.AreEqual(9.5, result.FillPrice, 1e-6, "Maker 限价买单在跳空低开时享受更优开盘价成交（零滑点惩罚）");
        Assert.AreEqual(500m, result.FillQuantity);
    }

    [TestMethod]
    public void Gateway_PartialFillConservation_CumQtyAndLeavesQty_ShouldConsistentlyEqualOrderQty()
    {
        // 场景：通过 SimulatedExchangeGateway 撮合挂单，验证 ExecutionReport 的 CumQty 与 LeavesQty 之和守恒
        var config = new BrokerConfig { Slippage = 0.0, FeeRate = 0.0 };
        var gateway = new SimulatedExchangeGateway(config);
        var time0 = new DateTime(2025, 1, 15, 9, 30, 0, DateTimeKind.Utc);
        gateway.OnMarketData("sh.600000", 10.0, time0);

        // 提交未触碰的限价单（委托总量 600 股）
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.0, 600m);
        gateway.SendOrder("ord-cons", req);

        ExecutionReport? capturedReport = null;
        gateway.OnExecution += r => capturedReport = r;

        // 推送触碰价格行情
        var time1 = time0.AddDays(1);
        var bar = CreateBar(8.9, 9.2, 8.8, 9.0, 100000.0);
        gateway.OnMarketData("sh.600000", 9.0, time1, bar);

        Assert.IsNotNull(capturedReport);
        Assert.AreEqual(600m, capturedReport.CumQuantity);
        Assert.AreEqual(0m, capturedReport.LeavesQuantity);
        Assert.AreEqual(req.Quantity, capturedReport.CumQuantity + capturedReport.LeavesQuantity, "CumQty + LeavesQty 必须严格守恒等于 OrderQty");
        Assert.AreEqual(LiquidityRole.Maker, capturedReport.Role);
    }

    private static IDataRow CreateBar(double open, double high, double low, double close, double volume)
    {
        var cols = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = 0,
            ["high"] = 1,
            ["low"] = 2,
            ["close"] = 3,
            ["volume"] = 4
        };
        var values = new[]
        {
            open.ToString(System.Globalization.CultureInfo.InvariantCulture),
            high.ToString(System.Globalization.CultureInfo.InvariantCulture),
            low.ToString(System.Globalization.CultureInfo.InvariantCulture),
            close.ToString(System.Globalization.CultureInfo.InvariantCulture),
            volume.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return new CarrotBacktesting.NET.Data.DataRow(values, cols);
    }
}
