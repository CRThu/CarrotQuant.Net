using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine
{
    [TestClass]
    public class EngineContextTests
    {
        private class MockTimeseriesRegistry : ITimeseriesRegistry
        {
            public IMarketMetadata Metadata { get; }

            public MockTimeseriesRegistry(IMarketMetadata metadata)
            {
                Metadata = metadata;
            }

            public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged => null!;
            public bool HasField(string fieldName) => false;
            public void StepTo(int currentLogicalRow) { }
            public int RegisterTable(string tableName) => 0;
            public int GetTableIndex(string tableName) => 0;
            public string GetTableName(int tableIndex) => string.Empty;
        }

        private static ITimeseriesRegistry CreateMockRegistry()
        {
            var dates = new[] { new MarketTime(2025, 1, 1), new MarketTime(2025, 1, 2) };
            var symbols = new[] { "sh.600000" };
            var meta = new DynamicMarketMetadata(symbols, dates);
            return new MockTimeseriesRegistry(meta);
        }

        [TestMethod]
        public void Constructor_NullRequiredParams_ThrowsException()
        {
            var ts = CreateMockRegistry();
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());

            Assert.ThrowsException<ArgumentNullException>(() => new EngineContext(null!, broker));
            Assert.ThrowsException<ArgumentNullException>(() => new EngineContext(ts, null!));
        }

        [TestMethod]
        public void Constructor_DefaultOptionalParameters_ShouldInitializeNonNull()
        {
            var ts = CreateMockRegistry();
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());

            var ctx = new EngineContext(ts, broker);

            Assert.IsNotNull(ctx.Timeseries);
            Assert.IsNotNull(ctx.Broker);
            Assert.IsNotNull(ctx.Market);
            Assert.IsNotNull(ctx.Events);
            Assert.IsNotNull(ctx.Fields);
            Assert.AreEqual(-1, ctx.CurrentIndex);
            Assert.AreEqual(DateTime.MinValue, ctx.CurrentTime);
        }

        [TestMethod]
        public void State_IndexAndTime_CanBeUpdated()
        {
            var ts = CreateMockRegistry();
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var ctx = new EngineContext(ts, broker);

            var now = DateTime.UtcNow;
            ctx.UpdateStep(5, now);

            Assert.AreEqual(5, ctx.CurrentIndex);
            Assert.AreEqual(now, ctx.CurrentTime);
        }

        private class SamplePlugin
        {
            public string Name { get; set; } = "Sample";
        }

        [TestMethod]
        public void Extensions_SetAndGet_WorksProperly()
        {
            var ts = CreateMockRegistry();
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var ctx = new EngineContext(ts, broker);

            Assert.IsNull(ctx.GetExtension<SamplePlugin>());

            var plugin = new SamplePlugin { Name = "TestPlugin" };
            ctx.SetExtension(plugin);

            var retrieved = ctx.GetExtension<SamplePlugin>();
            Assert.IsNotNull(retrieved);
            Assert.AreEqual("TestPlugin", retrieved.Name);
            Assert.AreSame(plugin, retrieved);
        }

        [TestMethod]
        public void Facades_AccountAndPosition_ForwardToBrokerAccurately()
        {
            var ts = CreateMockRegistry();
            var config = new BrokerConfig { InitialCash = 50_000.0, SettleDays = 0, FeeRate = 0.0, Slippage = 0.0 };
            var broker = new Broker(new SimulatedExchangeGateway(config), config);
            var ctx = new EngineContext(ts, broker);

            Assert.AreEqual(50_000.0, ctx.AvailableCash);
            Assert.AreEqual(50_000.0, ctx.TotalEquity);
            Assert.AreEqual(0m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(0m, ctx.GetAvailablePosition("sh.600000"));
            Assert.AreEqual(0.0, ctx.GetPositionCost("sh.600000"), "无持仓时成本价必须为 0.0");

            // 1. 第一批以 10.0 买入 100 股
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m));
            Assert.AreEqual(100m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(10.0, ctx.GetPositionCost("sh.600000"), 1e-6, "第一批买入后持仓成本必须准确为 10.0");

            // 2. 第二批以 20.0 加仓 100 股 -> 加权成本 (100*10 + 100*20) / 200 = 15.0
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 20.0, 100m));
            Assert.AreEqual(200m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(15.0, ctx.GetPositionCost("sh.600000"), 1e-6, "加仓后加权平均持仓成本必须精确为 15.0");

            // 3. 部分平仓 50 股 -> 成本保持 15.0 不变
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 15.0, 50m));
            Assert.AreEqual(150m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(15.0, ctx.GetPositionCost("sh.600000"), 1e-6, "部分平仓后剩余持仓成本守恒保持 15.0");

            // 4. 全部清仓平仓 150 股 -> 成本归零
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 15.0, 150m));
            Assert.AreEqual(0m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(0.0, ctx.GetPositionCost("sh.600000"), 1e-6, "完全平仓后持仓成本必须准确归零");
        }

        [TestMethod]
        public void Facades_Metadata_SymbolsAndIndex_ForwardAccurately()
        {
            var dates = new[] { new MarketTime(2025, 1, 1) };
            var symbols = new[] { "sh.600000", "sz.000001", "bj.920001" };
            var meta = new DynamicMarketMetadata(symbols, dates);
            var ts = new MockTimeseriesRegistry(meta);
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var ctx = new EngineContext(ts, broker);

            Assert.AreEqual(3, ctx.SymbolCount);
            Assert.AreEqual(3, ctx.Symbols.Count);
            Assert.AreEqual("sh.600000", ctx.Symbols[0]);
            Assert.AreEqual("sz.000001", ctx.Symbols[1]);
            Assert.AreEqual("bj.920001", ctx.Symbols[2]);

            Assert.AreEqual(0, ctx.GetSymbolIndex("sh.600000"));
            Assert.AreEqual(1, ctx.GetSymbolIndex("sz.000001"));
            Assert.AreEqual(2, ctx.GetSymbolIndex("bj.920001"));
            Assert.AreEqual(-1, ctx.GetSymbolIndex("invalid_symbol"));
        }

        [TestMethod]
        public void Facades_TradeVerbs_BuySellClosePosition_ForwardToBrokerAccurately()
        {
            var ts = CreateMockRegistry();
            var config = new BrokerConfig { InitialCash = 100_000.0, SettleDays = 0, FeeRate = 0.0, Slippage = 0.0 };
            var broker = new Broker(new SimulatedExchangeGateway(config), config);
            IEngineContext ctx = new EngineContext(ts, broker);

            // 1. context.Buy 动词（默认市价单）
            long buyId = ctx.Buy("sh.600000", 200m, 10.0, orderTag: "ctx_buy_01");
            Assert.IsTrue(buyId > 0);
            Assert.AreEqual(200m, ctx.GetPosition("sh.600000"));

            // 2. context.Sell 动词卖出 50 股
            long sellId = ctx.Sell("sh.600000", 50m, 10.0, orderTag: "ctx_sell_01");
            Assert.IsTrue(sellId > 0);
            Assert.AreEqual(150m, ctx.GetPosition("sh.600000"));

            // 3. context.ClosePosition 动词全额平仓剩余 150 股
            long closeId = ctx.ClosePosition("sh.600000", 10.0, orderTag: "ctx_close_01");
            Assert.IsTrue(closeId > 0);
            Assert.AreEqual(0m, ctx.GetPosition("sh.600000"));

            // 4. 空仓调用 ClosePosition 返回 0L
            Assert.AreEqual(0L, ctx.ClosePosition("sh.600000"));
        }

        [TestMethod]
        public void EngineContext_StateBlackboardFacades_ShouldForwardProperly()
        {
            var ts = CreateMockRegistry();
            var config = new BrokerConfig();
            var broker = new Broker(new SimulatedExchangeGateway(config), config);
            IEngineContext ctx = new EngineContext(ts, broker);

            // 1. context.SetState 存储
            ctx.SetState("sig_direct", "DirectValue");
            Assert.IsTrue(ctx.HasState<string>("sig_direct"));
            Assert.AreEqual("DirectValue", ctx.GetState<string>("sig_direct"));

            // 2. context.TryTakeState 原子消费（输出时间戳）
            bool taken = ctx.TryTakeState<string>("sig_direct", out var val, out var takenTime);
            Assert.IsTrue(taken);
            Assert.AreEqual("DirectValue", val);
            Assert.AreEqual(ctx.CurrentTime, takenTime);
            Assert.IsFalse(ctx.HasState<string>("sig_direct"));

            // 3. context.ClearState 清理
            ctx.SetState("scalar_key", 12345);
            Assert.IsTrue(ctx.HasState<int>("scalar_key"));
            bool cleared = ctx.ClearState<int>("scalar_key");
            Assert.IsTrue(cleared);
            Assert.IsFalse(ctx.HasState<int>("scalar_key"));
        }

        #region 行情读取门面与复权全字段覆盖测试 (Market Data Facades & Post Adjustment)

        private class FullMockTimeseriesRegistry : ITimeseriesRegistry
        {
            private readonly Dictionary<string, IReadOnlyMatrix2D<double>> _matrices;
            public IMarketMetadata Metadata { get; }

            public FullMockTimeseriesRegistry(IMarketMetadata metadata, Dictionary<string, IReadOnlyMatrix2D<double>> matrices)
            {
                Metadata = metadata;
                _matrices = matrices;
            }

            public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged
            {
                if (_matrices.TryGetValue(fieldName, out var mat)) return (IReadOnlyMatrix2D<T>)mat;
                throw new KeyNotFoundException($"Field '{fieldName}' not found.");
            }

            public bool HasField(string fieldName) => _matrices.ContainsKey(fieldName);
            public void StepTo(int currentLogicalRow) { }
            public int RegisterTable(string tableName) => 0;
            public int GetTableIndex(string tableName) => 0;
            public string GetTableName(int tableIndex) => string.Empty;
        }

        private static (EngineContext ctx, DynamicMarketMetadata meta) CreateFullContextWithFactors()
        {
            var symbols = new[] { "sh.600000", "sz.000001" };
            var dates = new[] { new MarketTime(2025, 1, 2), new MarketTime(2025, 1, 3) };
            var meta = new DynamicMarketMetadata(symbols, dates);

            var openMat = new ChunkMatrix2D<double>(2, 4096, 2);
            var highMat = new ChunkMatrix2D<double>(2, 4096, 2);
            var lowMat = new ChunkMatrix2D<double>(2, 4096, 2);
            var closeMat = new ChunkMatrix2D<double>(2, 4096, 2);
            var volMat = new ChunkMatrix2D<double>(2, 4096, 2);
            var vwapMat = new ChunkMatrix2D<double>(2, 4096, 2);
            var peMat = new ChunkMatrix2D<double>(2, 4096, 2);

            // Row 0
            openMat[0, 0] = 10.0; openMat[0, 1] = 20.0;
            highMat[0, 0] = 10.5; highMat[0, 1] = 20.5;
            lowMat[0, 0] = 9.5;   lowMat[0, 1] = 19.5;
            closeMat[0, 0] = 10.2; closeMat[0, 1] = 20.2;
            volMat[0, 0] = 1000.0; volMat[0, 1] = 2000.0;
            vwapMat[0, 0] = 10.1; vwapMat[0, 1] = 20.1;
            peMat[0, 0] = 15.0;   peMat[0, 1] = 25.0;

            // Row 1
            openMat[1, 0] = 11.0; openMat[1, 1] = 21.0;
            highMat[1, 0] = 11.5; highMat[1, 1] = 21.5;
            lowMat[1, 0] = 10.5;  lowMat[1, 1] = 20.5;
            closeMat[1, 0] = 11.2; closeMat[1, 1] = 21.2;
            volMat[1, 0] = 1100.0; volMat[1, 1] = 2100.0;
            vwapMat[1, 0] = 11.1; vwapMat[1, 1] = 21.1;
            peMat[1, 0] = 16.0;   peMat[1, 1] = 26.0;

            var matrices = new Dictionary<string, IReadOnlyMatrix2D<double>>(StringComparer.OrdinalIgnoreCase)
            {
                ["open"] = openMat,
                ["high"] = highMat,
                ["low"] = lowMat,
                ["close"] = closeMat,
                ["volume"] = volMat,
                ["vwap"] = vwapMat,
                ["pe_ratio"] = peMat
            };

            var ts = new FullMockTimeseriesRegistry(meta, matrices);
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());

            // 注入复权因子矩阵：Row 0 因子为 1.0，Row 1 因子为 2.0 (sh) 和 1.5 (sz)
            var factorMat = new ChunkMatrix2D<double>(2, 4096, 2);
            factorMat[0, 0] = 1.0; factorMat[0, 1] = 1.0;
            factorMat[1, 0] = 2.0; factorMat[1, 1] = 1.5;

            var ctx = new EngineContext(ts, broker, priceFields: new[] { "vwap" }, factorMatrix: factorMat, stepToDateIndex: null);

            return (ctx, meta);
        }

        [TestMethod]
        public void Facades_MarketData_AllPriceModesAndFactors_ShouldCalculateAccurately()
        {
            var (ctx, meta) = CreateFullContextWithFactors();

            // 1. CurrentIndex = -1 时读取全部返回 NaN
            Assert.IsTrue(double.IsNaN(ctx.GetClose(0)));
            Assert.IsTrue(double.IsNaN(ctx.GetOpen(0)));
            Assert.IsTrue(double.IsNaN(ctx.GetHigh(0)));
            Assert.IsTrue(double.IsNaN(ctx.GetLow(0)));
            Assert.IsTrue(double.IsNaN(ctx.GetVolume(0)));
            Assert.IsTrue(double.IsNaN(ctx.GetField(0, "vwap")));
            // 2. 推进到 Step 1
            ctx.UpdateStep(1, meta.TradeDates[1]);
            Assert.AreEqual(2.0, ctx.MarketData.ToPostPrice(0, 1.0, 1));
            Assert.AreEqual(1.5, ctx.MarketData.ToPostPrice(1, 1.0, 1));

            // 3. Raw 模式验证 (返回原始物理价格与物理量)
            Assert.AreEqual(11.0, ctx.GetOpen(0, 0, PriceMode.Raw));
            Assert.AreEqual(11.5, ctx.GetHigh(0, 0, PriceMode.Raw));
            Assert.AreEqual(10.5, ctx.GetLow(0, 0, PriceMode.Raw));
            Assert.AreEqual(11.2, ctx.GetClose(0, 0, PriceMode.Raw));
            Assert.AreEqual(1100.0, ctx.GetVolume(0, 0, PriceMode.Raw));

            // 按代码点查与按索引点查一致
            Assert.AreEqual(11.0, ctx.GetOpen("sh.600000", 0, PriceMode.Raw));
            Assert.AreEqual(11.5, ctx.GetHigh("sh.600000", 0, PriceMode.Raw));
            Assert.AreEqual(10.5, ctx.GetLow("sh.600000", 0, PriceMode.Raw));
            Assert.AreEqual(11.2, ctx.GetClose("sh.600000", 0, PriceMode.Raw));
            Assert.AreEqual(1100.0, ctx.GetVolume("sh.600000", 0, PriceMode.Raw));

            // 4. Post 后复权模式验证 (价格类乘复权因子 2.0，成交量严格保持物理原始量)
            Assert.AreEqual(11.0 * 2.0, ctx.GetOpen(0, 0, PriceMode.Post));
            Assert.AreEqual(11.5 * 2.0, ctx.GetHigh(0, 0, PriceMode.Post));
            Assert.AreEqual(10.5 * 2.0, ctx.GetLow(0, 0, PriceMode.Post));
            Assert.AreEqual(11.2 * 2.0, ctx.GetClose(0, 0, PriceMode.Post));
            Assert.AreEqual(1100.0, ctx.GetVolume(0, 0, PriceMode.Post), "成交量绝不乘复权因子");

            // 按代码点查 Post 模式
            Assert.AreEqual(21.0 * 1.5, ctx.GetOpen("sz.000001", 0, PriceMode.Post));
            Assert.AreEqual(21.5 * 1.5, ctx.GetHigh("sz.000001", 0, PriceMode.Post));
            Assert.AreEqual(20.5 * 1.5, ctx.GetLow("sz.000001", 0, PriceMode.Post));
            Assert.AreEqual(21.2 * 1.5, ctx.GetClose("sz.000001", 0, PriceMode.Post));
            Assert.AreEqual(2100.0, ctx.GetVolume("sz.000001", 0, PriceMode.Post));

            // 5. 历史回溯 offset = -1 验证 (Row 0 的因子为 1.0)
            Assert.AreEqual(10.0, ctx.GetOpen(0, -1, PriceMode.Post));
            Assert.AreEqual(10.5, ctx.GetHigh(0, -1, PriceMode.Post));
            Assert.AreEqual(9.5, ctx.GetLow(0, -1, PriceMode.Post));
            Assert.AreEqual(10.2, ctx.GetClose(0, -1, PriceMode.Post));
            Assert.AreEqual(1000.0, ctx.GetVolume(0, -1, PriceMode.Post));

            // 6. GetField 通用字段点查 (价格字段 vwap 乘因子，非价格字段 pe_ratio 保持原值)
            Assert.AreEqual(11.1 * 2.0, ctx.GetField(0, "vwap", 0, PriceMode.Post));
            Assert.AreEqual(11.1 * 2.0, ctx.GetField("sh.600000", "vwap", 0, PriceMode.Post));
            Assert.AreEqual(11.1, ctx.GetField(0, "vwap", 0, PriceMode.Raw));
            Assert.AreEqual(16.0, ctx.GetField(0, "pe_ratio", 0, PriceMode.Post), "非价格字段在 Post 模式下不乘因子");
            Assert.AreEqual(16.0, ctx.GetField("sh.600000", "pe_ratio", 0, PriceMode.Raw));

            // GetField 直通 OHLCV 字段
            Assert.AreEqual(ctx.GetClose(0), ctx.GetField(0, "close"));
            Assert.AreEqual(ctx.GetOpen(0), ctx.GetField(0, "open"));
            Assert.AreEqual(ctx.GetHigh(0), ctx.GetField(0, "high"));
            Assert.AreEqual(ctx.GetLow(0), ctx.GetField(0, "low"));
            Assert.AreEqual(ctx.GetVolume(0), ctx.GetField(0, "volume"));

            // 7. 未知标的、越界与不存在字段防御
            Assert.IsTrue(double.IsNaN(ctx.GetClose("unknown.symbol")));
            Assert.IsTrue(double.IsNaN(ctx.GetOpen("unknown.symbol")));
            Assert.IsTrue(double.IsNaN(ctx.GetHigh("unknown.symbol")));
            Assert.IsTrue(double.IsNaN(ctx.GetLow("unknown.symbol")));
            Assert.IsTrue(double.IsNaN(ctx.GetVolume("unknown.symbol")));
            Assert.IsTrue(double.IsNaN(ctx.GetField("unknown.symbol", "close")));
            Assert.IsTrue(double.IsNaN(ctx.GetField(0, "non_existent_field")));
            Assert.IsTrue(double.IsNaN(ctx.GetField(0, "")));
            Assert.IsTrue(double.IsNaN(ctx.GetClose(999))); // 列越界
            Assert.IsTrue(double.IsNaN(ctx.GetClose(0, -10))); // 行历史越界
        }

        [TestMethod]
        public void Facades_MarketData_LookAheadBiasGuards_ShouldThrowExceptions()
        {
            var (ctx, meta) = CreateFullContextWithFactors();
            ctx.UpdateStep(0, meta.TradeDates[0]);

            // 1. 未来偏移 offset > 0 坚决抛出 ArgumentOutOfRangeException
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => ctx.GetClose(0, 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => ctx.GetOpen(0, 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => ctx.GetHigh(0, 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => ctx.GetLow(0, 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => ctx.GetVolume(0, 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => ctx.GetField(0, "vwap", 1));

            // 2. 模拟 OnOpen 开盘决策阶段 (IsInOpenPhase = true)
            ctx.IsInOpenPhase = true;

            // 当期 Open 允许正常读取（开盘时 open 已产出）
            Assert.AreEqual(10.0, ctx.GetOpen(0, 0));
            Assert.AreEqual(10.0, ctx.GetField(0, "open", 0));

            // 当期 High, Low, Close, Volume 及非 open 字段严禁读取，坚决杜绝前瞻偷价！
            Assert.ThrowsException<InvalidOperationException>(() => ctx.GetClose(0, 0));
            Assert.ThrowsException<InvalidOperationException>(() => ctx.GetHigh(0, 0));
            Assert.ThrowsException<InvalidOperationException>(() => ctx.GetLow(0, 0));
            Assert.ThrowsException<InvalidOperationException>(() => ctx.GetVolume(0, 0));
            Assert.ThrowsException<InvalidOperationException>(() => ctx.GetField(0, "close", 0));
            Assert.ThrowsException<InvalidOperationException>(() => ctx.GetField(0, "vwap", 0));

            // 恢复常规决策相位
            ctx.IsInOpenPhase = false;
            Assert.AreEqual(10.2, ctx.GetClose(0, 0));
        }

        [TestMethod]
        public void RecordSignal_WithAndWithoutRecorder_ShouldBehaveCorrectly()
        {
            var ts = CreateMockRegistry();
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());

            // 1. 无 Recorder 时调用安全不报错
            var ctxWithoutRecorder = new EngineContext(ts, broker);
            ctxWithoutRecorder.RecordSignal("sh.600000", 1.5, "momentum", "breakout");

            // 2. 挂载真实 Recorder
            var recorder = new CarrotBacktesting.NET.Analysis.TradingRecorder();
            var ctxWithRecorder = new EngineContext(ts, broker, recorder: recorder);
            ctxWithRecorder.UpdateStep(0, new MarketTime(2025, 1, 2));

            ctxWithRecorder.RecordSignal("sh.600000", 0.88, "alpha_01", "golden_cross");
            var result = recorder.GetResult(Array.Empty<CarrotBacktesting.NET.Abstraction.Analysis.PositionSnapshot>());
            Assert.AreEqual(1, result.Signals.Count);
            Assert.AreEqual("sh.600000", result.Signals[0].Symbol);
            Assert.AreEqual(0.88, result.Signals[0].SignalValue, 1e-6);
            Assert.AreEqual("alpha_01", result.Signals[0].SignalGroup);
            Assert.AreEqual("golden_cross", result.Signals[0].Reason);
        }

        #endregion
    }
}
