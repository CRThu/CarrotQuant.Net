using System;
using System.Collections.Generic;
using CarrotBacktesting.NET;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Indicators;
using CarrotBackTesting.Net.UnitTest.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Indicators
{
    [TestClass]
    public class CustomFieldAndAdjustmentTests
    {
        #region 1. 专项 27: 单字段与多字段指标池矩阵切片计算测试

        [TestMethod]
        public void IndicatorPool_SingleFieldBinding_ShouldCalculateVolumeSmaAccurately()
        {
            // 准备 3 只股票、4 个时间步的模拟市场
            var symbols = new[] { "SH.600000", "SZ.000001", "SH.600519" };
            var dates = new[]
            {
                new MarketTime(2026, 1, 1),
                new MarketTime(2026, 1, 2),
                new MarketTime(2026, 1, 3),
                new MarketTime(2026, 1, 4)
            };
            var meta = new MockMetadata(symbols, dates, new[] { "close", "volume" });

            // 构造 Close 矩阵与 Volume 矩阵（数值完全不同，用于验证字段隔离性）
            var closeData = new double[4, 3];
            var volumeData = new double[4, 3];

            for (int r = 0; r < dates.Length; r++)
            {
                for (int c = 0; c < symbols.Length; c++)
                {
                    closeData[r, c] = 10.0 + r;            // 价格: 10, 11, 12, 13
                    volumeData[r, c] = (r + 1) * 1000.0;    // 成交量: 1000, 2000, 3000, 4000
                }
            }

            var tsRegistry = new MockTimeseriesRegistry(meta);
            tsRegistry.SetMatrix("close", new MockMatrix(closeData));
            tsRegistry.SetMatrix("volume", new MockMatrix(volumeData));

            var indicatorRegistry = new IndicatorRegistry(meta);
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var context = new EngineContext(tsRegistry, broker, indicators: indicatorRegistry);

            // 注册成交量均线指标池 (vol_sma3)，显式绑定 "volume" 字段
            var volSma3 = indicatorRegistry.GetOrAdd("vol_sma3", () => new Sma(3), field: "volume", mode: PriceMode.Raw);

            // 推进时间步 0, 1, 2
            context.UpdateStep(0, dates[0]);
            indicatorRegistry.Step(context);
            Assert.IsFalse(volSma3[0].IsReady);

            context.UpdateStep(1, dates[1]);
            indicatorRegistry.Step(context);
            Assert.IsFalse(volSma3[0].IsReady);

            context.UpdateStep(2, dates[2]);
            indicatorRegistry.Step(context);

            // 验证时间步 2: 前 3 步成交量均值 = (1000 + 2000 + 3000) / 3 = 2000.0
            Assert.IsTrue(volSma3[0].IsReady);
            Assert.AreEqual(2000.0, volSma3[0].Current, 1e-9);
            Assert.AreEqual(2000.0, volSma3["SH.600000"].Current, 1e-9);

            // 推进到时间步 3: (2000 + 3000 + 4000) / 3 = 3000.0
            context.UpdateStep(3, dates[3]);
            indicatorRegistry.Step(context);
            Assert.AreEqual(3000.0, volSma3[0].Current, 1e-9);
            Assert.AreEqual(2000.0, volSma3[0][-1], 1e-9); // 上期回溯值为 2000.0
        }

        [TestMethod]
        public void IndicatorPool_CustomFieldBinding_Turnover_ShouldCalculateAccurately()
        {
            var symbols = new[] { "SH.600000" };
            var dates = new[] { new MarketTime(2026, 1, 1), new MarketTime(2026, 1, 2) };
            var meta = new MockMetadata(symbols, dates, new[] { "turnover" });

            // 模拟换手率自定义列
            var toData = new double[,] { { 0.05 }, { 0.07 } };

            var tsRegistry = new MockTimeseriesRegistry(meta);
            tsRegistry.SetMatrix("turnover", new MockMatrix(toData));

            var indicatorRegistry = new IndicatorRegistry(meta);
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var context = new EngineContext(tsRegistry, broker, indicators: indicatorRegistry);

            // 绑定自定义换手率列
            var toSma2 = indicatorRegistry.GetOrAdd("turnover_sma2", () => new Sma(2), field: "turnover", mode: PriceMode.Raw);

            context.UpdateStep(0, dates[0]);
            indicatorRegistry.Step(context);

            context.UpdateStep(1, dates[1]);
            indicatorRegistry.Step(context);

            Assert.IsTrue(toSma2[0].IsReady);
            Assert.AreEqual(0.06, toSma2[0].Current, 1e-9); // (0.05 + 0.07) / 2 = 0.06
        }

        [TestMethod]
        public void IndicatorRegistry_DuplicateRegistrationWithConflictingFields_ShouldThrowInvalidOperationException()
        {
            var meta = new MockMetadata(new[] { "SH.600000" }, new[] { new MarketTime(2026, 1, 1) }, new[] { "close", "high", "low" });
            var registry = new IndicatorRegistry(meta);

            // 首次注册：绑定 high, low, close
            registry.GetOrAdd("kdj", () => new Kdj(9, 3, 3), fields: new[] { "high", "low", "close" });

            // 重复注册相同名称但不同字段列表：应显式抛出强类型异常防御冲突
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                registry.GetOrAdd("kdj", () => new Kdj(9, 3, 3), fields: new[] { "open", "high", "low" });
            });
        }

        #endregion

        #region 2. 专项 33.2: 策略后复权价与底层网关物理价格对齐测试

        [TestMethod]
        public void EngineContext_AdjustmentOrderAlignment_ShouldConvertPostLimitPriceToRawPriceForBroker()
        {
            var symbol = "SH.600000";
            var symbols = new[] { symbol };
            var dates = new[] { new MarketTime(2026, 1, 1), new MarketTime(2026, 1, 2) };
            var meta = new MockMetadata(symbols, dates, new[] { "close", "open" });

            // 物理真实收盘价: 步 0 为 50.0 元，步 1 跌至 47.0 元
            var closeData = new double[,] { { 50.0 }, { 47.0 } };
            // 复权因子: 2.0 (相当于 10 送 10 除权后，后复权价格在步 0 为 100.0 元)
            var factorData = new double[,] { { 2.0 }, { 2.0 } };

            var tsRegistry = new MockTimeseriesRegistry(meta);
            tsRegistry.SetMatrix("close", new MockMatrix(closeData));

            var brokerConfig = new BrokerConfig
            {
                InitialCash = 100000.0,
                FeeRate = 0.0,
                MinFee = 0.0,
                Slippage = 0.0
            };
            var simExchange = new SimulatedExchangeGateway(brokerConfig);
            var broker = new Broker(simExchange, brokerConfig);

            var context = new EngineContext(tsRegistry, broker);
            var factorMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 2);
            factorMatrix[0, 0] = 2.0;
            factorMatrix[1, 0] = 2.0;
            context.SetAdjustmentData(factorMatrix, new[] { 0, 1 });

            // 步进至第 0 步
            context.UpdateStep(0, dates[0]);
            simExchange.Step(dates[0], tsRegistry, 0);

            // 1. 验证策略端读取后复权价格为 100.0 元
            double postClose = context.GetClose(symbol);
            Assert.AreEqual(100.0, postClose, 1e-9);

            // 验证转换门面
            Assert.AreEqual(50.0, context.ToRawPrice(symbol, 100.0), 1e-9);
            Assert.AreEqual(100.0, context.ToPostPrice(symbol, 50.0), 1e-9);

            // 2. 策略在后复权价视角发出限价买单：打算在 95.0 元（折合物理价格 47.5 元）挂买单 100 股
            // 明确指定 TimeInForce.GoodTilCanceled 跨日有效，门面完全透明折算
            long orderId = context.Buy(symbol, 100m, price: 95.0, type: OrderType.Limit, timeInForce: TimeInForce.GoodTilCanceled);

            // 3. 验证 Broker 的资金预冻结金额：
            //    必须基于真实的物理价格 47.5 元进行预冻结（47.5 * 100 = 4750 元），
            //    绝对严禁按后复权 95.0 元虚假预冻结 9500 元！
            double expectedCash = 100000.0 - 4750.0;
            Assert.AreEqual(expectedCash, context.AvailableCash, 1e-6);

            // 4. 验证撮合对齐：
            //    当期真实收盘价 50.0 元 > 限价 47.5 元，未触碰限价，订单必须处于挂单状态未成交
            simExchange.Step(dates[0], tsRegistry, 0);
            Assert.AreEqual(0m, context.GetPosition(symbol));

            // 5. 跨入时间步 1：次日真实收盘价跌至 47.0 元（低于物理限价 47.5 元）
            context.UpdateStep(1, dates[1]);
            simExchange.Step(dates[1], tsRegistry, 1);

            // 撮合成交：成功买入 100 股！
            Assert.AreEqual(100m, context.GetPosition(symbol));
            // 真实成交持仓成本为真实物理价格（限价 47.5 元成交）
            Assert.AreEqual(47.5, context.GetPositionCost(symbol), 1e-6);
        }

        [TestMethod]
        public void EngineContext_AdjustmentOrderAlignment_BracketOrder_ShouldConvertBothStopAndTakeProfitPrices()
        {
            var symbol = "SZ.000001";
            var dates = new[] { new MarketTime(2026, 1, 1), new MarketTime(2026, 1, 2) };
            var meta = new MockMetadata(new[] { symbol }, dates, new[] { "close" });

            var closeData = new double[,] { { 50.0 }, { 44.0 } };

            var tsRegistry = new MockTimeseriesRegistry(meta);
            tsRegistry.SetMatrix("close", new MockMatrix(closeData));

            var brokerConfig = new BrokerConfig { InitialCash = 100000.0, Slippage = 0.0, FeeRate = 0.0, MinFee = 0.0 };
            var simExchange = new SimulatedExchangeGateway(brokerConfig);
            var broker = new Broker(simExchange, brokerConfig);
            var brokerDriver = (IBrokerEngineDriver)broker;

            var context = new EngineContext(tsRegistry, broker);
            var factorMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 2);
            factorMatrix[0, 0] = 2.0;
            factorMatrix[1, 0] = 2.0;
            context.SetAdjustmentData(factorMatrix, new[] { 0, 1 });

            context.UpdateStep(0, dates[0]);
            simExchange.Step(dates[0], tsRegistry, 0);
            brokerDriver.Step(dates[0]);

            // 预设买入持仓 100 股（后复权限价 100.0 元，折合物理价格 50.0 元，即时成交）
            context.Buy(symbol, 100m, price: 100.0, type: OrderType.Limit);
            Assert.AreEqual(100m, context.GetPosition(symbol));

            // 策略在后复权价视角下挂 Bracket 括号单（门面完全无感透明逆折算）：
            // 后复权止损价: 90.0 元（折合物理 45.0 元）
            // 后复权止盈价: 120.0 元（折合物理 60.0 元）
            var (stopId, takeProfitId) = context.Bracket(symbol, 100m, stopPrice: 90.0, takeProfitPrice: 120.0);

            Assert.IsTrue(stopId > 0);
            Assert.IsTrue(takeProfitId > 0);

            // 跨入时间步 1：标的物理价格跌至 44.0 元（折合后复权 88.0 元，跌破 90.0 止损线，物理价格 44.0 <= 45.0）
            context.UpdateStep(1, dates[1]);
            simExchange.Step(dates[1], tsRegistry, 1);
            brokerDriver.Step(dates[1]);

            // 止损单成功触发并撮合成交，持仓安全归零！
            Assert.AreEqual(0m, context.GetAvailablePosition(symbol));
            Assert.AreEqual(0m, context.GetPosition(symbol));
        }

        #endregion

        #region 3. 热路径循环外提零开销抽象与行切片验证 (Zero-Cost Row-Span Optimization Tests)

        [TestMethod]
        public void EngineContext_GetFieldRow_SafetyAndLookAheadProtection_ShouldEnforceRules()
        {
            var symbols = new[] { "SH.600000", "SZ.000001" };
            var dates = new[] { new MarketTime(2026, 1, 1), new MarketTime(2026, 1, 2) };
            var meta = new MockMetadata(symbols, dates, new[] { "open", "close", "volume" });

            var closeData = new double[,] { { 10.0, 20.0 }, { 11.0, 21.0 } };
            var openData = new double[,] { { 9.0, 19.0 }, { 10.0, 20.0 } };
            var volData = new double[,] { { 1000.0, 2000.0 }, { 1100.0, 2100.0 } };

            var tsRegistry = new MockTimeseriesRegistry(meta);
            tsRegistry.SetMatrix("close", new MockMatrix(closeData));
            tsRegistry.SetMatrix("open", new MockMatrix(openData));
            tsRegistry.SetMatrix("volume", new MockMatrix(volData));

            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var context = new EngineContext(tsRegistry, broker);

            // 未步进时 (CurrentIndex = -1)，返回空切片
            Assert.IsTrue(context.GetFieldRow("close").IsEmpty);

            // 步进至第 0 步
            context.UpdateStep(0, dates[0]);

            // 1. 验证正向偏移（未来函数）防御性抛错
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => context.GetFieldRow("close", offset: 1));

            // 2. 验证未知字段返回空切片
            Assert.IsTrue(context.GetFieldRow("unknown_col").IsEmpty);
            Assert.IsTrue(context.GetFieldRow("").IsEmpty);

            // 3. 验证正常提取行切片
            var closeRow = context.GetFieldRow("close");
            Assert.AreEqual(2, closeRow.Length);
            Assert.AreEqual(10.0, closeRow[0], 1e-9);
            Assert.AreEqual(20.0, closeRow[1], 1e-9);

            // 4. 验证开盘决策模式（IsInOpenPhase）防前瞻偏差
            context.IsInOpenPhase = true;
            // 开盘时允许读取 open
            var openRow = context.GetFieldRow("open");
            Assert.AreEqual(2, openRow.Length);
            Assert.AreEqual(9.0, openRow[0], 1e-9);
            // 开盘时严禁读取当期 close
            Assert.ThrowsException<InvalidOperationException>(() => context.GetFieldRow("close", offset: 0));
            context.IsInOpenPhase = false;

            // 5. 验证价格字段判定
            Assert.IsTrue(context.IsPriceField("close"));
            Assert.IsTrue(context.IsPriceField("open"));
            Assert.IsTrue(context.IsPriceField("high"));
            Assert.IsTrue(context.IsPriceField("low"));
            Assert.IsFalse(context.IsPriceField("volume"));
            Assert.IsFalse(context.IsPriceField("turnover"));
        }

        [TestMethod]
        public void IndicatorPool_ZeroCostRowSpan_SingleField_PostAndRaw_ShouldCalculateAccurately()
        {
            var symbols = new[] { "SH.600000", "SZ.000001" };
            var dates = new[] { new MarketTime(2026, 1, 1), new MarketTime(2026, 1, 2) };
            var meta = new MockMetadata(symbols, dates, new[] { "close", "volume" });

            // 物理真实价格: SH=10->12, SZ=20->24
            var closeData = new double[,] { { 10.0, 20.0 }, { 12.0, 24.0 } };
            // 成交量: SH=1000->2000, SZ=3000->4000
            var volData = new double[,] { { 1000.0, 3000.0 }, { 2000.0, 4000.0 } };

            var tsRegistry = new MockTimeseriesRegistry(meta);
            tsRegistry.SetMatrix("close", new MockMatrix(closeData));
            tsRegistry.SetMatrix("volume", new MockMatrix(volData));

            var indicatorRegistry = new IndicatorRegistry(meta);
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var context = new EngineContext(tsRegistry, broker, indicators: indicatorRegistry);

            // 注入复权因子: SH 因子为 2.0，SZ 因子为 1.5
            var factorMatrix = new ChunkMatrix2D<double>(width: 2, pageSize: 4096, initialRowCount: 2);
            factorMatrix[0, 0] = 2.0; factorMatrix[0, 1] = 1.5;
            factorMatrix[1, 0] = 2.0; factorMatrix[1, 1] = 1.5;
            context.SetAdjustmentData(factorMatrix, new[] { 0, 1 });

            // 注册收盘价后复权均线（SMA2）
            var closeSmaPost = indicatorRegistry.GetOrAdd("close_sma_post", () => new Sma(2), field: "close", mode: PriceMode.Post);
            // 注册收盘价原始未复权均线（SMA2）
            var closeSmaRaw = indicatorRegistry.GetOrAdd("close_sma_raw", () => new Sma(2), field: "close", mode: PriceMode.Raw);
            // 注册成交量均线（SMA2），即使请求 Post 模式，也绝不乘因子！
            var volSma = indicatorRegistry.GetOrAdd("vol_sma", () => new Sma(2), field: "volume", mode: PriceMode.Post);

            // 步进第 0 步 (采样数 1 < 周期 2，处于未就绪预热态)
            context.UpdateStep(0, dates[0]);
            indicatorRegistry.Step(context);

            Assert.IsFalse(closeSmaPost[0].IsReady);
            Assert.IsTrue(double.IsNaN(closeSmaPost[0].Current));
            Assert.IsFalse(closeSmaRaw[0].IsReady);
            Assert.IsTrue(double.IsNaN(closeSmaRaw[0].Current));
            Assert.IsFalse(volSma[0].IsReady);
            Assert.IsTrue(double.IsNaN(volSma[0].Current));

            // 步进第 1 步
            context.UpdateStep(1, dates[1]);
            indicatorRegistry.Step(context);

            Assert.IsTrue(closeSmaPost[0].IsReady);
            // SH: ((10 * 2) + (12 * 2)) / 2 = (20 + 24) / 2 = 22.0
            Assert.AreEqual(22.0, closeSmaPost[0].Current, 1e-9);
            // SZ: ((20 * 1.5) + (24 * 1.5)) / 2 = (30 + 36) / 2 = 33.0
            Assert.AreEqual(33.0, closeSmaPost[1].Current, 1e-9);

            // SH Raw: (10 + 12) / 2 = 11.0
            Assert.AreEqual(11.0, closeSmaRaw[0].Current, 1e-9);

            // SH Volume: (1000 + 2000) / 2 = 1500.0
            Assert.AreEqual(1500.0, volSma[0].Current, 1e-9);
            // SZ Volume: (3000 + 4000) / 2 = 3500.0
            Assert.AreEqual(3500.0, volSma[1].Current, 1e-9);
        }

        [TestMethod]
        public void IndicatorPool_ZeroCostRowSpan_MultiField_VWAP_SelectiveAdjustment_ShouldKeepVolumeRaw()
        {
            var symbols = new[] { "SH.600000" };
            var dates = new[] { new MarketTime(2026, 1, 1), new MarketTime(2026, 1, 2) };
            var meta = new MockMetadata(symbols, dates, new[] { "high", "low", "close", "volume" });

            // 步 0: high=12, low=8, close=10, vol=1000
            // 步 1: high=14, low=10, close=12, vol=2000
            var highData = new double[,] { { 12.0 }, { 14.0 } };
            var lowData = new double[,] { { 8.0 }, { 10.0 } };
            var closeData = new double[,] { { 10.0 }, { 12.0 } };
            var volData = new double[,] { { 1000.0 }, { 2000.0 } };

            var tsRegistry = new MockTimeseriesRegistry(meta);
            tsRegistry.SetMatrix("high", new MockMatrix(highData));
            tsRegistry.SetMatrix("low", new MockMatrix(lowData));
            tsRegistry.SetMatrix("close", new MockMatrix(closeData));
            tsRegistry.SetMatrix("volume", new MockMatrix(volData));

            var indicatorRegistry = new IndicatorRegistry(meta);
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var context = new EngineContext(tsRegistry, broker, indicators: indicatorRegistry);

            // 复权因子为 2.0 (10 送 10)
            var factorMatrix = new ChunkMatrix2D<double>(width: 1, pageSize: 4096, initialRowCount: 2);
            factorMatrix[0, 0] = 2.0; factorMatrix[1, 0] = 2.0;
            context.SetAdjustmentData(factorMatrix, new[] { 0, 1 });

            // 注册 4 字段 VWAP（全周期累计模式：典型点价 * 物理成交量 / 物理累计成交量）
            // 字段顺序: high, low, close, volume
            var vwapPool = indicatorRegistry.GetOrAdd("vwap", () => new Vwap(0), fields: new[] { "high", "low", "close", "volume" }, mode: PriceMode.Post);

            // 步进第 0 步:
            // 后复权价格: high=24, low=16, close=20 -> typical = (24 + 16 + 20) / 3 = 20.0
            // volume 必须保持原始 1000 (严禁乘以 2.0 变成 2000!)
            // vwap = (20.0 * 1000) / 1000 = 20.0
            context.UpdateStep(0, dates[0]);
            indicatorRegistry.Step(context);

            Assert.AreEqual(20.0, vwapPool[0].Current, 1e-9);

            // 步进第 1 步:
            // 后复权价格: high=28, low=20, close=24 -> typical = (28 + 20 + 24) / 3 = 24.0
            // volume = 2000
            // 累计成交金额 = (20.0 * 1000) + (24.0 * 2000) = 20000 + 48000 = 68000
            // 累计成交量 = 1000 + 2000 = 3000
            // vwap = 68000 / 3000 = 22.666666666666668
            context.UpdateStep(1, dates[1]);
            indicatorRegistry.Step(context);

            Assert.AreEqual(68000.0 / 3000.0, vwapPool[0].Current, 1e-9);
        }

        #endregion
    }
}
