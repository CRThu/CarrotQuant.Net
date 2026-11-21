using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Exchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class BarSnapshotTests
{
    [TestMethod]
    public void MemoryLayout_MustBeExactly48Bytes_ForSingleCacheLineThroughput()
    {
        // 6 个 8 字节 double (Open, High, Low, Close, Volume, Amount) = 48 字节
        int size = Unsafe.SizeOf<BarSnapshot>();
        Assert.AreEqual(48, size, "BarSnapshot 必须严格为 48 字节非托管紧凑内存，杜绝任何托管引用与内存膨胀。");
    }

    [TestMethod]
    public void Empty_AllFieldsMustBeNaN_AndHasFlagsMustBeFalse()
    {
        var bar = BarSnapshot.Empty;

        Assert.IsFalse(bar.IsValid);
        Assert.IsFalse(bar.HasOpen);
        Assert.IsFalse(bar.HasHigh);
        Assert.IsFalse(bar.HasLow);
        Assert.IsFalse(bar.HasClose);
        Assert.IsFalse(bar.HasVolume);
        Assert.IsFalse(bar.HasAmount);

        Assert.IsTrue(double.IsNaN(bar.Open));
        Assert.IsTrue(double.IsNaN(bar.High));
        Assert.IsTrue(double.IsNaN(bar.Low));
        Assert.IsTrue(double.IsNaN(bar.Close));
        Assert.IsTrue(double.IsNaN(bar.Volume));
        Assert.IsTrue(double.IsNaN(bar.Amount));

        Assert.IsNull(bar.Vwap);
    }

    [TestMethod]
    public void NextOpenExecution_OnlyOpenProvided_ShouldBeValidAndFallbackGracefully()
    {
        // 次步开盘撮合场景：仅能观测到 Open
        var bar = new BarSnapshot(open: 10.5);

        Assert.IsTrue(bar.IsValid);
        Assert.IsTrue(bar.HasOpen);
        Assert.IsFalse(bar.HasClose);
        Assert.IsFalse(bar.HasHigh);
        Assert.IsFalse(bar.HasLow);
        Assert.IsFalse(bar.HasVolume);
        Assert.IsFalse(bar.HasAmount);

        Assert.AreEqual(10.5, bar.Open);
        Assert.AreEqual(10.5, bar.OpenOr(0.0));
        Assert.AreEqual(20.0, bar.CloseOr(20.0));
        Assert.AreEqual(10.5, bar.LowOr(bar.Open));
    }

    [TestMethod]
    public void VwapDerivation_ShouldCalculateAccurately_AndHandleZeroOrMissing()
    {
        // 均价撮合场景：提供 Amount 与 Volume
        var bar = new BarSnapshot(volume: 2000.0, amount: 21000.0);

        Assert.IsTrue(bar.HasVolume);
        Assert.IsTrue(bar.HasAmount);
        Assert.IsNotNull(bar.Vwap);
        Assert.AreEqual(10.5, bar.Vwap!.Value, 1e-6);

        // Volume 为 0 或 NaN 时应安全返回 null
        var zeroVolBar = new BarSnapshot(volume: 0.0, amount: 100.0);
        Assert.IsNull(zeroVolBar.Vwap);

        var noAmountBar = new BarSnapshot(volume: 100.0);
        Assert.IsNull(noAmountBar.Vwap);
    }

    [TestMethod]
    public void SymmetricFallbackMethods_ShouldReturnCorrectValues()
    {
        var bar = new BarSnapshot(open: 10.1, high: 10.8, low: 9.9, close: 10.5);

        Assert.AreEqual(10.1, bar.OpenOr(0.0));
        Assert.AreEqual(10.8, bar.HighOr(0.0));
        Assert.AreEqual(9.9, bar.LowOr(0.0));
        Assert.AreEqual(10.5, bar.CloseOr(0.0));
        Assert.AreEqual(500.0, bar.VolumeOr(500.0)); // 未赋值，回退到 500.0
        Assert.AreEqual(10000.0, bar.AmountOr(10000.0)); // 未赋值，回退到 10000.0
    }

    [TestMethod]
    public void FromDataRow_ShouldExtractFieldsCorrectly_AndIgnoreMissing()
    {
        var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = 0,
            ["close"] = 1,
            ["volume"] = 2
        };
        var values = new[] { "10.0", "10.5", "10000" };
        var row = new DataRow("SH.600000", values, cols);

        var bar = BarSnapshot.FromDataRow(row);

        Assert.IsTrue(bar.HasOpen);
        Assert.IsTrue(bar.HasClose);
        Assert.IsTrue(bar.HasVolume);
        Assert.IsFalse(bar.HasHigh); // 未给出，自动为 NaN
        Assert.IsFalse(bar.HasLow);
        Assert.IsFalse(bar.HasAmount);

        Assert.AreEqual(10.0, bar.Open);
        Assert.AreEqual(10.5, bar.Close);
        Assert.AreEqual(10000.0, bar.Volume);
    }

    [TestMethod]
    public void EqualityAndOperators_ShouldTreatNaNCorrectly()
    {
        var bar1 = new BarSnapshot(open: 10.0, close: 10.5);
        var bar2 = new BarSnapshot(open: 10.0, close: 10.5);
        var bar3 = new BarSnapshot(open: 10.0, close: 10.6);

        Assert.IsTrue(bar1 == bar2);
        Assert.IsTrue(bar1.Equals(bar2));
        Assert.AreEqual(bar1.GetHashCode(), bar2.GetHashCode());

        Assert.IsTrue(bar1 != bar3);
        Assert.IsFalse(bar1 == bar3);
    }

    [TestMethod]
    public void MatchingEngine_AdaptiveHitTest_WhenLowHighMissing_ShouldFallbackToExecPrice()
    {
        var config = new BrokerConfig();
        var matcher = new MatchingEngine(config);
        var tradeTime = DateTime.UtcNow;

        // 仅有开盘价 10.0，High 和 Low 为 null/NaN（次步开盘撮合时态）
        var openOnlyBar = new BarSnapshot(open: 10.0);

        // 1. 限价买单 10.2（委托价高于开盘价 10.0）：开盘触碰且享受 Maker 跳空价格改善为 10.0
        var buyOrderHit = new SimulatedOrder(1L, new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 10.2, 100m), tradeTime);
        var resultHit = matcher.EvaluatePendingLimitOrder(buyOrderHit, 10.0, openOnlyBar, tradeTime);
        Assert.IsTrue(resultHit.IsFilled);
        Assert.AreEqual(10.0, resultHit.FillPrice); // 享受开盘跳空价格改善（Math.Min(10.2, 10.0) == 10.0）

        // 2. 限价买单 9.8（委托价低于开盘价 10.0）：未触碰
        var buyOrderMiss = new SimulatedOrder(2L, new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Limit, 9.8, 100m), tradeTime);
        var resultMiss = matcher.EvaluatePendingLimitOrder(buyOrderMiss, 10.0, openOnlyBar, tradeTime);
        Assert.IsFalse(resultMiss.IsFilled);
    }

    [TestMethod]
    public void MatchingEngine_WhenVolumeMissing_ShouldNotEnforceVolumeLimit()
    {
        var config = new BrokerConfig
        {
            VolumeLimit = 0.1 // 10% 限制
        };
        var matcher = new MatchingEngine(config);
        var tradeTime = DateTime.UtcNow;

        // 缺少 Volume 的 Bar（Volume 为 null/NaN）
        var barWithoutVol = new BarSnapshot(close: 10.0);
        var req = new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 1000000m);

        // 由于 Bar 缺少 Volume，不应被流动性限制误拒
        var result = matcher.EvaluateImmediateOrder(req, 10.0, barWithoutVol, 1.0, tradeTime);
        Assert.IsTrue(result.IsFilled);
    }
}
