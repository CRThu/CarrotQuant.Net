using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBackTesting.Net.UnitTest.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Data;

[TestClass]
public class MarketDataProviderTests
{
    private MockMetadata _metadata = null!;
    private MockTimeseriesRegistry _timeseries = null!;

    [TestInitialize]
    public void Setup()
    {
        var dates = new[]
        {
            new MarketTime(2025, 1, 2),
            new MarketTime(2025, 1, 3),
            new MarketTime(2025, 1, 4)
        };
        var symbols = new[] { "sh.600000", "sz.000001" };
        _metadata = new MockMetadata(symbols, dates);

        _timeseries = new MockTimeseriesRegistry(_metadata);

        // 3 行 2 列时序数据
        // Row 0: sh=10.0, sz=20.0
        // Row 1: sh=11.0, sz=22.0
        // Row 2: sh=12.0, sz=24.0
        _timeseries.SetMatrix("open", new MockMatrix(new double[,] { { 9.0, 19.0 }, { 10.0, 21.0 }, { 11.5, 23.0 } }));
        _timeseries.SetMatrix("high", new MockMatrix(new double[,] { { 10.5, 20.5 }, { 11.5, 22.5 }, { 12.5, 24.5 } }));
        _timeseries.SetMatrix("low", new MockMatrix(new double[,] { { 8.5, 18.5 }, { 9.5, 20.5 }, { 11.0, 22.0 } }));
        _timeseries.SetMatrix("close", new MockMatrix(new double[,] { { 10.0, 20.0 }, { 11.0, 22.0 }, { 12.0, 24.0 } }));
        _timeseries.SetMatrix("volume", new MockMatrix(new double[,] { { 1000.0, 2000.0 }, { 1100.0, 2200.0 }, { 1200.0, 2400.0 } }));
        _timeseries.SetMatrix("amount", new MockMatrix(new double[,] { { 10000.0, 40000.0 }, { 12100.0, 48400.0 }, { 14400.0, 57600.0 } }));
        _timeseries.SetMatrix("pe", new MockMatrix(new double[,] { { 8.5, 12.0 }, { 9.0, 12.5 }, { 9.5, 13.0 } }));
        _timeseries.SetMatrix("vwap", new MockMatrix(new double[,] { { 9.8, 19.8 }, { 10.8, 21.8 }, { 11.8, 23.8 } }));
    }

    [TestMethod]
    public void Constructor_WithExplicitFactorMatrix_InitializesCorrectly()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[0, 0] = 1.0; factorMatrix[0, 1] = 1.0;
        factorMatrix[1, 0] = 2.0; factorMatrix[1, 1] = 1.5;
        factorMatrix[2, 0] = 2.0; factorMatrix[2, 1] = 1.5;

        using var provider = new MarketDataProvider(_timeseries, factorMatrix, new[] { 0, 1, 2 }, priceFields: new[] { "vwap" });

        Assert.IsTrue(provider.HasAdjustments);
        Assert.AreSame(_timeseries, provider.Timeseries);
        Assert.AreSame(_metadata, provider.Metadata);

        // 默认 OHLC 及自定义追加的 vwap 均应被识别为价格字段
        Assert.AreEqual(22.0, provider.GetField(0, "close", 1, PriceMode.Post), 1e-12); // 11.0 * 2.0
        Assert.AreEqual(21.6, provider.GetField(0, "vwap", 1, PriceMode.Post), 1e-12);  // 10.8 * 2.0
    }

    [TestMethod]
    public void Constructor_WithAdjustmentConfig_SelfResolvesAdjustmentData()
    {
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "back_adj_factor", 0 } };
        var eventData = new Dictionary<MarketTime, Dictionary<string, List<DataRow>>>();
        var d1 = _metadata.TradeDates[0];
        eventData[d1] = new Dictionary<string, List<DataRow>>
        {
            ["sh.600000"] = new List<DataRow> { new DataRow("sh.600000", d1, new[] { "2.0" }, ordinals) },
            ["sz.000001"] = new List<DataRow> { new DataRow("sz.000001", d1, new[] { "1.5" }, ordinals) }
        };

        var adjTable = new EventTable("adjustments", new[] { "back_adj_factor" }, eventData);
        var events = new EventRegistry();
        events.Register("adjustments", adjTable);

        var config = new AdjustmentConfig
        {
            Mode = AdjustmentMode.Post,
            TableId = "adjustments",
            FactorField = "back_adj_factor"
        };

        using var provider = new MarketDataProvider(_timeseries, config, resolver: null, events: events);

        Assert.IsTrue(provider.HasAdjustments);
        // 首日因子生效: sh.600000 close 10.0 * 2.0 = 20.0
        Assert.AreEqual(20.0, provider.GetField("sh.600000", "close", 0, PriceMode.Post), 1e-12);
        Assert.AreEqual(30.0, provider.GetField("sz.000001", "close", 0, PriceMode.Post), 1e-12); // 20.0 * 1.5
    }

    [TestMethod]
    public void Constructor_WithRawAdjustmentConfig_DoesNotLoadFactors()
    {
        var config = new AdjustmentConfig
        {
            Mode = AdjustmentMode.None,
            TableId = "adjustments"
        };

        using var provider = new MarketDataProvider(_timeseries, config);

        Assert.IsFalse(provider.HasAdjustments);
        // Post 模式在无因子时安全退化为物理原值
        Assert.AreEqual(10.0, provider.GetField("sh.600000", "close", 0, PriceMode.Post), 1e-12);
    }

    [TestMethod]
    public void GetField_RawMode_ReturnsRawPhysicalPrice()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[1, 0] = 2.0; factorMatrix[1, 1] = 1.5;

        using var provider = new MarketDataProvider(_timeseries, factorMatrix);

        // 无论是价格类还是非价格类，Raw 模式下严格保持物理值
        Assert.AreEqual(11.0, provider.GetField(0, "close", 1, PriceMode.Raw), 1e-12);
        Assert.AreEqual(22.0, provider.GetField(1, "close", 1, PriceMode.Raw), 1e-12);
        Assert.AreEqual(11.0, provider.GetField("sh.600000", "close", 1, PriceMode.Raw), 1e-12);
        Assert.AreEqual(22.0, provider.GetField("sz.000001", "close", 1, PriceMode.Raw), 1e-12);

        Assert.AreEqual(1100.0, provider.GetField(0, "volume", 1, PriceMode.Raw), 1e-12);
        Assert.AreEqual(9.0, provider.GetField(0, "pe", 1, PriceMode.Raw), 1e-12);
    }

    [TestMethod]
    public void GetField_PostMode_ScalesPriceFieldsOnly()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[1, 0] = 2.0;
        factorMatrix[1, 1] = 1.5;

        using var provider = new MarketDataProvider(_timeseries, factorMatrix);

        // 1. 价格类字段（close, open, high, low）必须乘因子
        Assert.AreEqual(22.0, provider.GetField(0, "close", 1, PriceMode.Post), 1e-12); // 11.0 * 2.0
        Assert.AreEqual(33.0, provider.GetField(1, "close", 1, PriceMode.Post), 1e-12); // 22.0 * 1.5
        Assert.AreEqual(20.0, provider.GetField(0, "open", 1, PriceMode.Post), 1e-12);  // 10.0 * 2.0
        Assert.AreEqual(23.0, provider.GetField(0, "high", 1, PriceMode.Post), 1e-12);  // 11.5 * 2.0
        Assert.AreEqual(19.0, provider.GetField(0, "low", 1, PriceMode.Post), 1e-12);   // 9.5 * 2.0

        // 2. 非价格类字段（volume, pe）即使请求 Post 也严禁乘因子
        Assert.AreEqual(1100.0, provider.GetField(0, "volume", 1, PriceMode.Post), 1e-12);
        Assert.AreEqual(9.0, provider.GetField(0, "pe", 1, PriceMode.Post), 1e-12);

        // 3. 因子为异常值（<= 0 或 NaN）时 Fail-Fast 返回 NaN，严禁假数据穿透
        factorMatrix[2, 0] = double.NaN;
        factorMatrix[2, 1] = 0.0;
        Assert.IsTrue(double.IsNaN(provider.GetField(0, "close", 2, PriceMode.Post)));
        Assert.IsTrue(double.IsNaN(provider.GetField(1, "close", 2, PriceMode.Post)));
    }

    [TestMethod]
    public void GetField_InvalidInputs_ReturnsNaN()
    {
        using var provider = new MarketDataProvider(_timeseries);

        // 列越界
        Assert.IsTrue(double.IsNaN(provider.GetField(-1, "close", 0)));
        Assert.IsTrue(double.IsNaN(provider.GetField(2, "close", 0)));

        // 未知标的
        Assert.IsTrue(double.IsNaN(provider.GetField("unknown.sym", "close", 0)));

        // 未知字段
        Assert.IsTrue(double.IsNaN(provider.GetField(0, "non_existent_field", 0)));

        // 未初始化时步（-1）
        Assert.IsTrue(double.IsNaN(provider.GetField(0, "close", -1)));
    }

    [TestMethod]
    public void GetRow_RawAndPostMode_CopiesDataAccurately()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[1, 0] = 2.0; factorMatrix[1, 1] = 1.5;

        using var provider = new MarketDataProvider(_timeseries, factorMatrix);

        var buffer = new double[2];

        // 1. Raw 模式复制收盘价
        provider.GetRow("close", 1, PriceMode.Raw).CopyTo(buffer);
        Assert.AreEqual(11.0, buffer[0], 1e-12);
        Assert.AreEqual(22.0, buffer[1], 1e-12);

        // 2. Post 模式复制收盘价（价格类乘以因子）
        provider.GetRow("close", 1, PriceMode.Post).CopyTo(buffer);
        Assert.AreEqual(22.0, buffer[0], 1e-12);
        Assert.AreEqual(33.0, buffer[1], 1e-12);

        // 3. Post 模式复制成交量（非价格类保持原值）
        provider.GetRow("volume", 1, PriceMode.Post).CopyTo(buffer);
        Assert.AreEqual(1100.0, buffer[0], 1e-12);
        Assert.AreEqual(2200.0, buffer[1], 1e-12);

        // 4. 只读 Span 切片视图
        var span = provider.GetRow("close", 1, PriceMode.Post);
        Assert.AreEqual(2, span.Length);
        Assert.AreEqual(22.0, span[0], 1e-12);
        Assert.AreEqual(33.0, span[1], 1e-12);
    }

    [TestMethod]
    public void GetBar_RawAndPostMode_ConstructsMarketBarAccurately()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[1, 0] = 2.0; factorMatrix[1, 1] = 1.5;

        using var provider = new MarketDataProvider(_timeseries, factorMatrix);

        // 1. Raw 模式 Bar
        var rawBar = provider.GetBar(0, 1, PriceMode.Raw);
        Assert.AreEqual(10.0, rawBar.Open, 1e-12);
        Assert.AreEqual(11.5, rawBar.High, 1e-12);
        Assert.AreEqual(9.5, rawBar.Low, 1e-12);
        Assert.AreEqual(11.0, rawBar.Close, 1e-12);
        Assert.AreEqual(1100.0, rawBar.Volume, 1e-12);
        Assert.AreEqual(12100.0, rawBar.Amount, 1e-12);

        // 2. Post 模式 Bar: OHLC 乘因子，Volume/Amount 保持原值
        var postBar = provider.GetBar(0, 1, PriceMode.Post);
        Assert.AreEqual(20.0, postBar.Open, 1e-12);   // 10.0 * 2.0
        Assert.AreEqual(23.0, postBar.High, 1e-12);   // 11.5 * 2.0
        Assert.AreEqual(19.0, postBar.Low, 1e-12);    // 9.5 * 2.0
        Assert.AreEqual(22.0, postBar.Close, 1e-12);  // 11.0 * 2.0
        Assert.AreEqual(1100.0, postBar.Volume, 1e-12); // 未乘因子
        Assert.AreEqual(12100.0, postBar.Amount, 1e-12); // 未乘因子

        // 3. 通过 symbol 重载
        var postBarBySymbol = provider.GetBar("sh.600000", 1, PriceMode.Post);
        Assert.AreEqual(22.0, postBarBySymbol.Close, 1e-12);
    }

    [TestMethod]
    public void ToRawPrice_And_ToPostPrice_BijectionsAndDefenses()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[1, 0] = 2.0;
        factorMatrix[2, 0] = -1.0; // 异常因子

        using var provider = new MarketDataProvider(_timeseries, factorMatrix);

        // 正常折算
        double raw = 10.5;
        double post = provider.ToPostPrice(0, raw, 1);
        Assert.AreEqual(21.0, post, 1e-12); // 10.5 * 2.0

        double convertedRaw = provider.ToRawPrice(0, post, 1);
        Assert.AreEqual(raw, convertedRaw, 1e-12); // 21.0 / 2.0 = 10.5

        // 异常因子防御（<= 0）Fail-Fast 返回 NaN
        Assert.IsTrue(double.IsNaN(provider.ToPostPrice(0, 15.0, 2)));
        Assert.IsTrue(double.IsNaN(provider.ToRawPrice(0, 15.0, 2)));

        // 无因子矩阵时安全返回输入价格
        using var emptyProvider = new MarketDataProvider(_timeseries);
        Assert.AreEqual(15.0, emptyProvider.ToPostPrice(0, 15.0, 1), 1e-12);
        Assert.AreEqual(15.0, emptyProvider.ToRawPrice(0, 15.0, 1), 1e-12);
    }

    [TestMethod]
    public void HotPath_ZeroHeapAllocation()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[1, 0] = 2.0; factorMatrix[1, 1] = 1.5;

        using var provider = new MarketDataProvider(_timeseries, factorMatrix);

        var destination = new double[2];

        // 预热 JIT 与缓存
        for (int i = 0; i < 100; i++)
        {
            provider.GetField(0, "close", 1, PriceMode.Post);
            provider.GetBar(0, 1, PriceMode.Post);
            provider.GetRow("close", 1, PriceMode.Post).CopyTo(destination);
            provider.ToPostPrice(0, 10.0, 1);
            provider.ToRawPrice(0, 20.0, 1);
        }

        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);

        long memBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10000; i++)
        {
            _ = provider.GetField(0, "close", 1, PriceMode.Post);
            _ = provider.GetBar(0, 1, PriceMode.Post);
            provider.GetRow("close", 1, PriceMode.Post).CopyTo(destination);
            _ = provider.ToPostPrice(0, 10.0, 1);
            _ = provider.ToRawPrice(0, 20.0, 1);
        }

        long memAfter = GC.GetAllocatedBytesForCurrentThread();
        long allocated = memAfter - memBefore;

        // 热路径执行 10000 次必须严格 0 堆内存分配
        Assert.AreEqual(0, allocated, $"热路径存在意外堆内存分配: {allocated} 字节");
    }

    [TestMethod]
    public void Dispose_SafelyReleasesResources()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[0, 0] = 2.0;

        var provider = new MarketDataProvider(_timeseries, factorMatrix);
        Assert.IsTrue(provider.HasAdjustments);
        Assert.AreEqual(20.0, provider.GetField(0, "close", 0, PriceMode.Post), 1e-12);

        // 释放
        provider.Dispose();

        // 释放后 HasAdjustments 为 false，查询安全降级为物理价，零崩溃
        Assert.IsFalse(provider.HasAdjustments);
        Assert.AreEqual(10.0, provider.GetField(0, "close", 0, PriceMode.Post), 1e-12);
    }

    [TestMethod]
    public void GetBar_WhenOnlyOpenExists_ConstructsValidBarWithPostAdjustedOpen()
    {
        // 模拟开盘决策阶段（Phase 1: Open），仅 Open 已产出，Close/High/Low 尚为 NaN
        var meta = new MockMetadata(new[] { "sh.600000" }, new[] { new MarketTime(2025, 1, 2) });
        var ts = new MockTimeseriesRegistry(meta);
        ts.SetMatrix("open", new MockMatrix(new double[,] { { 10.0 } }));

        var factorMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 1);
        factorMatrix[0, 0] = 2.0;

        using var provider = new MarketDataProvider(ts, factorMatrix);

        var barPost = provider.GetBar(0, 0, PriceMode.Post);
        Assert.IsTrue(barPost.IsValid);
        Assert.AreEqual(20.0, barPost.Open, 1e-12); // 10.0 * 2.0
        Assert.IsTrue(double.IsNaN(barPost.Close));
        Assert.IsTrue(double.IsNaN(barPost.High));
        Assert.IsTrue(double.IsNaN(barPost.Low));

        var barRaw = provider.GetBar(0, 0, PriceMode.Raw);
        Assert.IsTrue(barRaw.IsValid);
        Assert.AreEqual(10.0, barRaw.Open, 1e-12);
    }

    [TestMethod]
    public void GetRow_MultiThreadedConcurrency_ThreadLocalBuffersRemainSafe()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[1, 0] = 2.0; factorMatrix[1, 1] = 1.5;

        using var provider = new MarketDataProvider(_timeseries, factorMatrix);

        // 多线程并发调用同一 provider.GetRow，断言无死锁、无字典并发异常、数据 100% 准确
        Parallel.For(0, 100, _ =>
        {
            var spanClose = provider.GetRow("close", 1, PriceMode.Post);
            Assert.AreEqual(2, spanClose.Length);
            Assert.AreEqual(22.0, spanClose[0], 1e-12);
            Assert.AreEqual(33.0, spanClose[1], 1e-12);

            var spanVol = provider.GetRow("volume", 1, PriceMode.Post);
            Assert.AreEqual(2, spanVol.Length);
            Assert.AreEqual(1100.0, spanVol[0], 1e-12);
            Assert.AreEqual(2200.0, spanVol[1], 1e-12);
        });
    }

    [TestMethod]
    public void ToRawPrice_And_ToPostPrice_NegativeIndices_FailFastReturnNaN()
    {
        var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 3);
        factorMatrix[1, 0] = 2.0; factorMatrix[1, 1] = 1.5;

        using var provider = new MarketDataProvider(_timeseries, factorMatrix);

        // 未初始化时步（-1）或未知标的（-1）必须 Fail-Fast 返回 NaN，严禁隐式假兜底
        Assert.IsTrue(double.IsNaN(provider.ToRawPrice(-1, 100.0, 1)));
        Assert.IsTrue(double.IsNaN(provider.ToRawPrice(0, 100.0, -1)));
        Assert.IsTrue(double.IsNaN(provider.ToRawPrice("unknown.symbol", 100.0, 1)));

        Assert.IsTrue(double.IsNaN(provider.ToPostPrice(-1, 100.0, 1)));
        Assert.IsTrue(double.IsNaN(provider.ToPostPrice(0, 100.0, -1)));
        Assert.IsTrue(double.IsNaN(provider.ToPostPrice("unknown.symbol", 100.0, 1)));
    }

    [TestMethod]
    public void SimulatedExchangeGateway_WhenOnCheckVolumeConfiguredWithoutVolumeMatrix_ThrowsInvalidOperationException()
    {
        var meta = new MockMetadata(new[] { "sh.600000" }, new[] { new MarketTime(2025, 1, 2) });
        var ts = new MockTimeseriesRegistry(meta);
        ts.SetMatrix("close", new MockMatrix(new double[,] { { 10.0 } }));
        // 故意不注册 volume 矩阵

        var brokerConfig = new BrokerConfig
        {
            OnCheckVolume = (req, bar) => true
        };
        var gateway = new SimulatedExchangeGateway(brokerConfig);

        using var marketData = new MarketDataProvider(ts);

        // 在时钟推进第一步时必须立即 Fail-Fast 阻断并抛出包含明确指引的强类型异常
        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
        {
            gateway.Step(meta.TradeDates[0], marketData, 0, EnginePhase.Close);
        });

        StringAssert.Contains(ex.Message, "OnCheckVolume");
        StringAssert.Contains(ex.Message, "volume");
    }

    [TestMethod]
    public void GetLatestClose_WhenNormalOrSuspended_ForwardFillsCorrectly()
    {
        // 构造三步时序：step 0: 10.0, step 1: NaN (停牌), step 2: NaN (连续停牌)
        var dates = new[] { new MarketTime(2025, 1, 2), new MarketTime(2025, 1, 3), new MarketTime(2025, 1, 4) };
        var meta = new MockMetadata(new[] { "sh.600000" }, dates);
        var ts = new MockTimeseriesRegistry(meta);
        ts.SetMatrix("close", new MockMatrix(new double[,]
        {
            { 10.0 },
            { double.NaN },
            { double.NaN }
        }));

        var factorMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 3);
        factorMatrix[0, 0] = 2.0;
        factorMatrix[1, 0] = 2.0;
        factorMatrix[2, 0] = 2.0;

        using var provider = new MarketDataProvider(ts, factorMatrix);

        // Step 0: 正常日，返回当期价格
        Assert.AreEqual(10.0, provider.GetLatestClose(0, 0, PriceMode.Raw), 1e-12);
        Assert.AreEqual(20.0, provider.GetLatestClose("sh.600000", 0, PriceMode.Post), 1e-12);

        // Step 1: 停牌日，自动向前回溯使用 Step 0 的价格 (ffill)
        Assert.AreEqual(10.0, provider.GetLatestClose(0, 1, PriceMode.Raw), 1e-12);
        Assert.AreEqual(20.0, provider.GetLatestClose("sh.600000", 1, PriceMode.Post), 1e-12);

        // Step 2: 连续停牌日，继续向前回溯使用 Step 0 的价格 (ffill)
        Assert.AreEqual(10.0, provider.GetLatestClose(0, 2, PriceMode.Raw), 1e-12);
        Assert.AreEqual(20.0, provider.GetLatestClose("sh.600000", 2, PriceMode.Post), 1e-12);

        // 越界或不存在历史有效价格的标的返回 NaN (包含负数越界与 stepIndex >= RowCount 严格越界)
        Assert.IsTrue(double.IsNaN(provider.GetLatestClose(-1, 0)));
        Assert.IsTrue(double.IsNaN(provider.GetLatestClose(0, -1)));
        Assert.IsTrue(double.IsNaN(provider.GetLatestClose(0, 999)));
        Assert.IsTrue(double.IsNaN(provider.GetLatestClose("unknown.symbol", 0)));
    }
}
