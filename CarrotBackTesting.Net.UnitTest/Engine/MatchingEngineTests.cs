using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Exchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class MatchingEngineTests
{
    private static IDataRow CreateBar(double open, double high, double low, double close, double volume, double? limitUp = null, double? limitDown = null)
    {
        var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = 0,
            ["high"] = 1,
            ["low"] = 2,
            ["close"] = 3,
            ["volume"] = 4
        };

        var values = new List<string>
        {
            open.ToString(System.Globalization.CultureInfo.InvariantCulture),
            high.ToString(System.Globalization.CultureInfo.InvariantCulture),
            low.ToString(System.Globalization.CultureInfo.InvariantCulture),
            close.ToString(System.Globalization.CultureInfo.InvariantCulture),
            volume.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (limitUp.HasValue)
        {
            cols["limit_up"] = values.Count;
            values.Add(limitUp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (limitDown.HasValue)
        {
            cols["limit_down"] = values.Count;
            values.Add(limitDown.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return new DataRow(values.ToArray(), cols);
    }

    [TestMethod]
    public void ImmediateOrder_MarketBuy_AppliesSlippageCorrectly()
    {
        var config = new BrokerConfig
        {
            Slippage = 0.001, // 千一滑点
            FeeRate = 0.0003,
            MinFee = 5.0
        };
        var matcher = new MatchingEngine(config);

        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000m);
        var result = matcher.EvaluateImmediateOrder(req, 10.0, null, 1.0, DateTime.UtcNow);

        Assert.IsTrue(result.IsFilled);
        // 10.0 * 1.001 = 10.01
        Assert.AreEqual(10.01, result.FillPrice, 1e-4);
        Assert.AreEqual(1000m, result.FillQuantity);
        // turnover = 1000 * 10.01 = 10010; fee = 10010 * 0.0003 = 3.003 -> MinFee = 5.0
        Assert.AreEqual(5.0, result.Commission, 1e-4);
    }

    [TestMethod]
    public void ImmediateOrder_MarketSell_AppliesSlippageCorrectly()
    {
        var config = new BrokerConfig
        {
            Slippage = 0.002, // 千二滑点
            FeeRate = 0.0005,
            MinFee = 0.0
        };
        var matcher = new MatchingEngine(config);

        var req = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 10.0, 2000m);
        var result = matcher.EvaluateImmediateOrder(req, 10.0, null, 1.0, DateTime.UtcNow);

        Assert.IsTrue(result.IsFilled);
        // 10.0 * (1 - 0.002) = 9.98
        Assert.AreEqual(9.98, result.FillPrice, 1e-4);
        Assert.AreEqual(2000m, result.FillQuantity);
        // turnover = 2000 * 9.98 = 19960; fee = 19960 * 0.0005 = 9.98
        Assert.AreEqual(9.98, result.Commission, 1e-4);
    }

    [TestMethod]
    public void ImmediateOrder_LimitPriceProtection_BuyCannotFillAboveLimit()
    {
        var config = new BrokerConfig
        {
            Slippage = 0.05 // 5% 巨大滑点
        };
        var matcher = new MatchingEngine(config);

        // 买单基准价 10.0，若加5%滑点为 10.5，但限价仅为 10.2
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.2, 1000m);
        var result = matcher.EvaluateImmediateOrder(req, 10.0, null, 1.0, DateTime.UtcNow);

        Assert.IsTrue(result.IsFilled);
        Assert.AreEqual(10.2, result.FillPrice, 1e-4, "成交价必须被截断在买入委托限价以内");
    }

    [TestMethod]
    public void ImmediateOrder_LimitPriceProtection_SellCannotFillBelowLimit()
    {
        var config = new BrokerConfig
        {
            Slippage = 0.05 // 5% 巨大滑点
        };
        var matcher = new MatchingEngine(config);

        // 卖单基准价 10.0，若减5%滑点为 9.5，但限价为 9.8
        var req = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 9.8, 1000m);
        var result = matcher.EvaluateImmediateOrder(req, 10.0, null, 1.0, DateTime.UtcNow);

        Assert.IsTrue(result.IsFilled);
        Assert.AreEqual(9.8, result.FillPrice, 1e-4, "成交价必须被截断在卖出委托限价以上");
    }

    [TestMethod]
    public void ImmediateOrder_UnfilledLimit_ShouldBeMarkedPending()
    {
        var config = new BrokerConfig();
        var matcher = new MatchingEngine(config);

        // 买单限价 9.5，当前基准市价 10.0 -> 尚未触碰
        var buyReq = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 1000m);
        var buyResult = matcher.EvaluateImmediateOrder(buyReq, 10.0, null, 1.0, DateTime.UtcNow);

        Assert.IsTrue(buyResult.IsPending);
        Assert.IsFalse(buyResult.IsFilled);

        // 卖单限价 10.5，当前基准市价 10.0 -> 尚未触碰
        var sellReq = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.5, 1000m);
        var sellResult = matcher.EvaluateImmediateOrder(sellReq, 10.0, null, 1.0, DateTime.UtcNow);

        Assert.IsTrue(sellResult.IsPending);
        Assert.IsFalse(sellResult.IsFilled);
    }

    [TestMethod]
    public void PendingOrder_MakerRule_FillsAtLimitPriceWhenTouched()
    {
        var config = new BrokerConfig();
        var matcher = new MatchingEngine(config);

        // 买单挂单：委托限价 9.5
        var buyReq = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.5, 1000m);
        var buyOrder = new SimulatedOrder("ord-1", buyReq, DateTime.UtcNow) { Multiplier = 1.0 };

        // 市场价格跌到 9.4 -> 触碰限价 9.5
        var buyResult = matcher.EvaluatePendingLimitOrder(buyOrder, 9.4, null, DateTime.UtcNow);
        Assert.IsTrue(buyResult.IsFilled);
        Assert.AreEqual(9.5, buyResult.FillPrice, 1e-4, "Maker 买单成交在委托限价");

        // 卖单挂单：委托限价 10.5
        var sellReq = new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Limit, 10.5, 1000m);
        var sellOrder = new SimulatedOrder("ord-2", sellReq, DateTime.UtcNow) { Multiplier = 1.0 };

        // 市场价格上涨到 10.6 -> 触碰限价 10.5
        var sellResult = matcher.EvaluatePendingLimitOrder(sellOrder, 10.6, null, DateTime.UtcNow);
        Assert.IsTrue(sellResult.IsFilled);
        Assert.AreEqual(10.5, sellResult.FillPrice, 1e-4, "Maker 卖单成交在委托限价");
    }

    [TestMethod]
    public void PendingOrder_MakerRule_NotFilledWhenPriceNotCrossed()
    {
        var config = new BrokerConfig();
        var matcher = new MatchingEngine(config);

        // 买单挂单 9.5，市价 9.6 -> 未触碰
        var buyOrder = new SimulatedOrder("o1", new OrderRequest("s", OrderDirection.Buy, OrderType.Limit, 9.5, 100m), DateTime.UtcNow);
        Assert.IsFalse(matcher.EvaluatePendingLimitOrder(buyOrder, 9.6, null, DateTime.UtcNow).IsFilled);

        // 卖单挂单 10.5，市价 10.4 -> 未触碰
        var sellOrder = new SimulatedOrder("o2", new OrderRequest("s", OrderDirection.Sell, OrderType.Limit, 10.5, 100m), DateTime.UtcNow);
        Assert.IsFalse(matcher.EvaluatePendingLimitOrder(sellOrder, 10.4, null, DateTime.UtcNow).IsFilled);
    }

    [TestMethod]
    public void PendingOrder_InvalidMarketPrice_ReturnsNoFill()
    {
        var config = new BrokerConfig();
        var matcher = new MatchingEngine(config);
        var order = new SimulatedOrder("o1", new OrderRequest("s", OrderDirection.Buy, OrderType.Limit, 10.0, 100m), DateTime.UtcNow);

        Assert.IsFalse(matcher.EvaluatePendingLimitOrder(order, 0.0, null, DateTime.UtcNow).IsFilled);
        Assert.IsFalse(matcher.EvaluatePendingLimitOrder(order, -5.0, null, DateTime.UtcNow).IsFilled);
        Assert.IsFalse(matcher.EvaluatePendingLimitOrder(order, double.NaN, null, DateTime.UtcNow).IsFilled);
        Assert.IsFalse(matcher.EvaluatePendingLimitOrder(order, double.PositiveInfinity, null, DateTime.UtcNow).IsFilled);
    }

    [TestMethod]
    public void VolumeLimit_Exceeded_ShouldRejectOrder()
    {
        var config = new BrokerConfig
        {
            VolumeLimit = 0.05 // 最大 5%
        };
        var matcher = new MatchingEngine(config);

        // 当前 Bar 成交量 10,000，委托 600 (占比 6% > 5%)
        var bar = CreateBar(10.0, 10.5, 9.8, 10.2, 10000.0);
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.2, 600m);
        var result = matcher.EvaluateImmediateOrder(req, 10.2, bar, 1.0, DateTime.UtcNow);

        Assert.IsTrue(result.IsRejected);
        StringAssert.Contains(result.RejectReason, "Exceeded volume limit");
    }

    [TestMethod]
    public void PriceLimit_Hook_CanInterceptTrading()
    {
        var config = new BrokerConfig
        {
            CheckLimit = true,
            OnCheckLimit = (order, bar) =>
            {
                // 若收盘价触及涨停则禁止买入
                double limitUp = bar.GetDouble("limit_up");
                double close = bar.GetDouble("close");
                return !(order.IsBuy && close >= limitUp);
            }
        };
        var matcher = new MatchingEngine(config);

        var bar = CreateBar(10.0, 11.0, 9.9, 11.0, 10000.0, limitUp: 11.0);
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 11.0, 100m);
        var result = matcher.EvaluateImmediateOrder(req, 11.0, bar, 1.0, DateTime.UtcNow);

        Assert.IsTrue(result.IsRejected);
        StringAssert.Contains(result.RejectReason, "OnCheckLimit hook");
    }

    [TestMethod]
    public void CustomHooks_SlippageAndFee_ShouldBeInvoked()
    {
        var config = new BrokerConfig
        {
            OnCalculateSlippage = (req, basePrice, bar) => basePrice + 0.123,
            OnCalculateFee = (req, turnover) => 8.88
        };
        var matcher = new MatchingEngine(config);

        var bar = CreateBar(10.0, 10.5, 9.5, 10.0, 5000.0);
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m);
        var result = matcher.EvaluateImmediateOrder(req, 10.0, bar, 1.0, DateTime.UtcNow);

        Assert.IsTrue(result.IsFilled);
        Assert.AreEqual(10.123, result.FillPrice, 1e-4);
        Assert.AreEqual(8.88, result.Commission, 1e-4);
    }

    [TestMethod]
    public void InvalidInputs_EvaluateImmediateOrder_ShouldBeRejected()
    {
        var matcher = new MatchingEngine(new BrokerConfig());

        // 零或负数量
        var reqZeroQty = new OrderRequest("s", OrderDirection.Buy, OrderType.Market, 10.0, 0m);
        Assert.IsTrue(matcher.EvaluateImmediateOrder(reqZeroQty, 10.0, null, 1.0, DateTime.UtcNow).IsRejected);

        // 负基准价
        var req = new OrderRequest("s", OrderDirection.Buy, OrderType.Market, 10.0, 10m);
        Assert.IsTrue(matcher.EvaluateImmediateOrder(req, -1.0, null, 1.0, DateTime.UtcNow).IsRejected);
        Assert.IsTrue(matcher.EvaluateImmediateOrder(req, 0.0, null, 1.0, DateTime.UtcNow).IsRejected);
        Assert.IsTrue(matcher.EvaluateImmediateOrder(req, double.NaN, null, 1.0, DateTime.UtcNow).IsRejected);
        Assert.IsTrue(matcher.EvaluateImmediateOrder(req, double.PositiveInfinity, null, 1.0, DateTime.UtcNow).IsRejected);
    }
}
