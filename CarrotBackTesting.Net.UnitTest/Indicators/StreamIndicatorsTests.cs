using System;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Indicators;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Indicators
{
    internal static class TestIndicatorExtensions
    {
        public static void Update(this IStreamIndicator indicator, double close)
        {
            indicator.Update(close, close, close, close, 1000.0);
        }
    }

    [TestClass]
    public class StreamIndicatorsTests
    {
        #region 1. SMA 简单移动平均测试

        [TestMethod]
        public void Sma_StandardCalculation_ShouldMatchExpected()
        {
            var sma = new Sma(3);
            Assert.IsFalse(sma.IsReady);
            Assert.IsTrue(double.IsNaN(sma.Current));

            sma.Update(10.0);
            Assert.IsFalse(sma.IsReady);
            Assert.IsTrue(double.IsNaN(sma.Current));

            sma.Update(20.0);
            Assert.IsFalse(sma.IsReady);

            sma.Update(30.0);
            Assert.IsTrue(sma.IsReady);
            Assert.AreEqual(20.0, sma.Current, 1e-12); // (10 + 20 + 30) / 3 = 20

            sma.Update(40.0);
            Assert.AreEqual(30.0, sma.Current, 1e-12); // (20 + 30 + 40) / 3 = 30
            Assert.AreEqual(20.0, sma[-1], 1e-12);      // 上一期值为 20
        }

        [TestMethod]
        public void Sma_LookaheadBias_ShouldThrowException()
        {
            var sma = new Sma(3);
            sma.Update(10.0);
            sma.Update(20.0);
            sma.Update(30.0);

            // 访问未来 Bar (offset > 0) 必须强制抛错
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = sma[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = sma[5]);
        }

        [TestMethod]
        public void Sma_SuspendedNan_ShouldFreezeAndRecover()
        {
            var sma = new Sma(3);
            sma.Update(10.0);
            sma.Update(20.0);
            sma.Update(30.0);
            Assert.AreEqual(20.0, sma.Current, 1e-12);

            // 连续 3 天停牌（输入 double.NaN 缺失值）
            sma.Update(double.NaN);
            sma.Update(double.NaN);
            sma.Update(double.NaN);

            // 状态机完全冻结，当前值与回溯值保持不变
            Assert.IsTrue(sma.IsReady);
            Assert.AreEqual(20.0, sma.Current, 1e-12);

            // 复牌第一天输入 40，平滑顺延计算 (20 + 30 + 40) / 3 = 30
            sma.Update(40.0);
            Assert.AreEqual(30.0, sma.Current, 1e-12);
            Assert.AreEqual(20.0, sma[-1], 1e-12);
        }

        [TestMethod]
        public void Sma_DoublePrecision_NoFloatDriftOverOneMillionSteps()
        {
            const int period = 20;
            var sma = new Sma(period);
            var random = new Random(42);

            double[] recent = new double[period];
            int count = 0;

            for (int i = 0; i < 1_000_000; i++)
            {
                double price = 10.0 + random.NextDouble() * 100.0;
                sma.Update(price);

                recent[count % period] = price;
                count++;

                if (i >= period - 1 && i % 100_000 == 0)
                {
                    // 离线真实均值
                    double sum = 0.0;
                    for (int j = 0; j < period; j++) sum += recent[j];
                    double expected = sum / period;

                    // 误差必须严格小于 1e-10，彻底免疫浮点漂移
                    Assert.AreEqual(expected, sma.Current, 1e-10, $"Step {i} drift exceeds tolerance!");
                }
            }
        }

        [TestMethod]
        public void Sma_Reset_ShouldClearAllStates()
        {
            var sma = new Sma(3);
            sma.Update(10.0);
            sma.Update(20.0);
            sma.Update(30.0);
            Assert.IsTrue(sma.IsReady);

            sma.Reset();
            Assert.IsFalse(sma.IsReady);
            Assert.IsTrue(double.IsNaN(sma.Current));
            Assert.IsTrue(double.IsNaN(sma[-1]));
        }

        #endregion

        #region 2. EMA 指数移动平均测试

        [TestMethod]
        public void Ema_StandardCalculation_ShouldMatchExpected()
        {
            var ema = new Ema(3);
            ema.Update(10.0);
            ema.Update(20.0);
            ema.Update(30.0);

            // 预热期 3 天简单平均: (10 + 20 + 30) / 3 = 20.0
            Assert.IsTrue(ema.IsReady);
            Assert.AreEqual(20.0, ema.Current, 1e-12);

            // 第 4 天: multiplier = 2 / (3 + 1) = 0.5
            // EMA_4 = (40 - 20) * 0.5 + 20 = 30.0
            ema.Update(40.0);
            Assert.AreEqual(30.0, ema.Current, 1e-12);
            Assert.AreEqual(20.0, ema[-1], 1e-12);
        }

        [TestMethod]
        public void Ema_SuspendedNan_ShouldFreeze()
        {
            var ema = new Ema(3);
            ema.Update(10.0);
            ema.Update(20.0);
            ema.Update(30.0);
            Assert.AreEqual(20.0, ema.Current, 1e-12);

            ema.Update(double.NaN);
            Assert.AreEqual(20.0, ema.Current, 1e-12);

            ema.Update(40.0);
            Assert.AreEqual(30.0, ema.Current, 1e-12);
        }

        [TestMethod]
        public void Ema_WithCustomAlpha_ShouldCalculateAndValidateProperly()
        {
            // 1. 验证自定义 alpha 模式
            var ema = new Ema(0.1);
            Assert.AreEqual(0.1, ema.Alpha, 1e-12);
            Assert.AreEqual(0.1, ema.Multiplier, 1e-12);
            // 2/0.1 - 1 = 19
            Assert.AreEqual(19, ema.Period);

            // 2. 异常边界防护
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Ema(0.0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Ema(-0.5));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Ema(1.5));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Ema(double.NaN));

            // 3. 经典周期构造模式的 Alpha 属性对齐
            var ema20 = new Ema(19);
            Assert.AreEqual(2.0 / 20.0, ema20.Alpha, 1e-12);
        }

        #endregion

        #region 3. RSI 相对强弱指标测试

        [TestMethod]
        public void Rsi_MonotonicIncreases_ShouldReach100()
        {
            var rsi = new Rsi(5);
            double price = 10.0;
            // 持续上涨 10 天
            for (int i = 0; i < 10; i++)
            {
                price += 2.0;
                rsi.Update(price);
            }

            Assert.IsTrue(rsi.IsReady);
            Assert.AreEqual(100.0, rsi.Current, 1e-6);
        }

        [TestMethod]
        public void Rsi_MonotonicDecreases_ShouldReach0()
        {
            var rsi = new Rsi(5);
            double price = 100.0;
            // 持续下跌 10 天
            for (int i = 0; i < 10; i++)
            {
                price -= 2.0;
                rsi.Update(price);
            }

            Assert.IsTrue(rsi.IsReady);
            Assert.AreEqual(0.0, rsi.Current, 1e-6);
        }

        [TestMethod]
        public void Rsi_SuspendedNan_ShouldFreeze()
        {
            var rsi = new Rsi(5);
            double price = 10.0;
            for (int i = 0; i < 8; i++)
            {
                price += (i % 2 == 0 ? 2.0 : -1.0);
                rsi.Update(price);
            }

            double valBefore = rsi.Current;
            Assert.IsTrue(rsi.IsReady);

            // 停牌
            rsi.Update(double.NaN);
            Assert.AreEqual(valBefore, rsi.Current, 1e-12);
        }

        #endregion

        #region 4. ATR 真实波幅指标测试

        [TestMethod]
        public void Atr_StandardCalculation_ShouldHandleGaps()
        {
            var atr = new Atr(3);

            // Bar 1: H=12, L=8, C=10 (TR = 4)
            atr.Update(10, 12, 8, 10, 100);
            Assert.IsFalse(atr.IsReady);

            // Bar 2: H=16, L=14, C=15 (跳空高开，TR = max(16-14, |16-10|, |14-10|) = 6)
            atr.Update(14, 16, 14, 15, 100);
            Assert.IsFalse(atr.IsReady);

            // Bar 3: H=17, L=12, C=13 (TR = max(17-12, |17-15|, |12-15|) = 5)
            atr.Update(15, 17, 12, 13, 100);
            Assert.IsTrue(atr.IsReady);

            // 预热均值: (4 + 6 + 5) / 3 = 5.0
            Assert.AreEqual(5.0, atr.Current, 1e-12);
        }

        [TestMethod]
        public void Atr_SuspendedNan_ShouldFreeze()
        {
            var atr = new Atr(3);
            atr.Update(10, 12, 8, 10, 100);
            atr.Update(14, 16, 14, 15, 100);
            atr.Update(15, 17, 12, 13, 100);
            Assert.AreEqual(5.0, atr.Current, 1e-12);

            atr.Update(double.NaN, double.NaN, double.NaN, double.NaN, 0);
            Assert.AreEqual(5.0, atr.Current, 1e-12);
        }

        #endregion

        #region 5. MACD 组合模式指标测试

        [TestMethod]
        public void Macd_CompositePattern_ShouldProvideDifDeaHist()
        {
            var macd = new Macd(3, 5, 3);
            Assert.IsFalse(macd.IsReady);
            Assert.IsFalse(macd.Dif.IsReady);
            Assert.IsFalse(macd.Dea.IsReady);
            Assert.IsFalse(macd.Hist.IsReady);

            // 连续灌入价格直至慢线和信号线就绪
            double[] prices = { 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30 };
            foreach (var p in prices)
            {
                macd.Update(p);
            }

            Assert.IsTrue(macd.IsReady);
            Assert.IsTrue(macd.Dif.IsReady);
            Assert.IsTrue(macd.Dea.IsReady);
            Assert.IsTrue(macd.Hist.IsReady);

            // 默认主索引器指向 Dif
            Assert.AreEqual(macd.Dif.Current, macd.Current, 1e-12);
            Assert.AreEqual(macd.Dif[-1], macd[-1], 1e-12);

            // Hist 必须严格等于 (Dif - Dea) * 2
            double expectedHist = (macd.Dif.Current - macd.Dea.Current) * 2.0;
            Assert.AreEqual(expectedHist, macd.Hist.Current, 1e-12);

            // 防偷价熔断
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = macd.Dif[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = macd.Dea[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = macd.Hist[1]);
        }

        [TestMethod]
        public void Macd_SuspendedNan_ShouldFreezeAllSubStreams()
        {
            var macd = new Macd(3, 5, 3);
            for (int i = 0; i < 15; i++)
            {
                macd.Update(10 + i * 2);
            }

            double difBefore = macd.Dif.Current;
            double deaBefore = macd.Dea.Current;
            double histBefore = macd.Hist.Current;

            // 停牌
            macd.Update(double.NaN);
            Assert.AreEqual(difBefore, macd.Dif.Current, 1e-12);
            Assert.AreEqual(deaBefore, macd.Dea.Current, 1e-12);
            Assert.AreEqual(histBefore, macd.Hist.Current, 1e-12);
        }

        #endregion

        #region 6. BollingerBands 布林带测试

        [TestMethod]
        public void BollingerBands_CompositePattern_ShouldProvideUpperMiddleLower()
        {
            var boll = new BollingerBands(5, 2.0);

            for (int i = 1; i <= 5; i++)
            {
                boll.Update(i * 10); // 10, 20, 30, 40, 50
            }

            Assert.IsTrue(boll.IsReady);
            Assert.IsTrue(boll.Upper.IsReady);
            Assert.IsTrue(boll.Middle.IsReady);
            Assert.IsTrue(boll.Lower.IsReady);

            // Mean = (10+20+30+40+50)/5 = 30
            // Diff: -20, -10, 0, 10, 20 -> Sq: 400 + 100 + 0 + 100 + 400 = 1000
            // Var = 1000 / 5 = 200 -> StdDev = sqrt(200) = 14.1421356...
            double expectedMean = 30.0;
            double expectedStdDev = Math.Sqrt(200.0);

            Assert.AreEqual(expectedMean, boll.Middle.Current, 1e-12);
            Assert.AreEqual(expectedMean + 2.0 * expectedStdDev, boll.Upper.Current, 1e-12);
            Assert.AreEqual(expectedMean - 2.0 * expectedStdDev, boll.Lower.Current, 1e-12);

            // 默认主索引器指向中轨 Middle
            Assert.AreEqual(boll.Middle.Current, boll.Current, 1e-12);
        }

        [TestMethod]
        public void BollingerBands_LookaheadBias_ShouldThrowException()
        {
            var boll = new BollingerBands(3, 2.0);
            boll.Update(10);
            boll.Update(20);
            boll.Update(30);

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = boll[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = boll.Middle[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = boll.Upper[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = boll.Lower[1]);
        }

        [TestMethod]
        public void BollingerBands_SuspendedNan_ShouldFreezeAndRecover()
        {
            var boll = new BollingerBands(3, 2.0);
            boll.Update(10);
            boll.Update(20);
            boll.Update(30);
            Assert.IsTrue(boll.IsReady);

            double midBefore = boll.Middle.Current;
            double upBefore = boll.Upper.Current;
            double lowBefore = boll.Lower.Current;

            // 停牌
            boll.Update(double.NaN);
            boll.Update(0.0);
            Assert.AreEqual(midBefore, boll.Middle.Current, 1e-12);
            Assert.AreEqual(upBefore, boll.Upper.Current, 1e-12);
            Assert.AreEqual(lowBefore, boll.Lower.Current, 1e-12);

            // 复牌输入有效值 40
            boll.Update(40);
            // 新窗口 20, 30, 40 -> 均值 30
            Assert.AreEqual(30.0, boll.Middle.Current, 1e-12);
        }

        [TestMethod]
        public void BollingerBands_Reset_ShouldClearAllStates()
        {
            var boll = new BollingerBands(3, 2.0);
            boll.Update(10);
            boll.Update(20);
            boll.Update(30);
            Assert.IsTrue(boll.IsReady);

            boll.Reset();
            Assert.IsFalse(boll.IsReady);
            Assert.IsFalse(boll.Upper.IsReady);
            Assert.IsFalse(boll.Middle.IsReady);
            Assert.IsFalse(boll.Lower.IsReady);
            Assert.IsTrue(double.IsNaN(boll.Current));
            Assert.IsTrue(double.IsNaN(boll.Upper.Current));
            Assert.IsTrue(double.IsNaN(boll.Middle.Current));
            Assert.IsTrue(double.IsNaN(boll.Lower.Current));
        }

        [TestMethod]
        public void Ema_Reset_ShouldClearAllStates()
        {
            var ema = new Ema(3);
            ema.Update(10);
            ema.Update(20);
            ema.Update(30);
            Assert.IsTrue(ema.IsReady);

            ema.Reset();
            Assert.IsFalse(ema.IsReady);
            Assert.IsTrue(double.IsNaN(ema.Current));
        }

        [TestMethod]
        public void Rsi_Reset_ShouldClearAllStates()
        {
            var rsi = new Rsi(3);
            for (int i = 0; i < 5; i++) rsi.Update(10 + i * 2);
            Assert.IsTrue(rsi.IsReady);

            rsi.Reset();
            Assert.IsFalse(rsi.IsReady);
            Assert.IsTrue(double.IsNaN(rsi.Current));
        }

        [TestMethod]
        public void Atr_Reset_ShouldClearAllStates()
        {
            var atr = new Atr(3);
            atr.Update(10, 12, 8, 10, 100);
            atr.Update(14, 16, 14, 15, 100);
            atr.Update(15, 17, 12, 13, 100);
            Assert.IsTrue(atr.IsReady);

            atr.Reset();
            Assert.IsFalse(atr.IsReady);
            Assert.IsTrue(double.IsNaN(atr.Current));
        }

        #endregion

        #region 7. 深入边界与缺陷防御测试 (MACD熊市死叉/环形回卷/未就绪防御)

        [TestMethod]
        public void Macd_NegativeDifBearMarket_SignalEmaShouldCalculateCorrectly()
        {
            // 测试单边下跌行情：DIF 为负数（快线 EMA < 慢线 EMA，死叉空头）
            // 验证 Ema 算子不误杀负数，信号线 DEA 与 HIST 必须平滑正常递推
            var macd = new Macd(3, 6, 3);

            // 持续下跌行情: 100 -> 90 -> 80 -> 70 -> 60 -> 50 -> 40 -> 30
            double[] bearPrices = { 100, 90, 80, 70, 60, 50, 40, 30, 20, 10 };
            for (int i = 0; i < bearPrices.Length; i++)
            {
                macd.Update(bearPrices[i]);
            }

            // 下跌过程中快线必然低于慢线，DIF 必然为负数
            Assert.IsTrue(macd.Dif[0] < 0.0, $"DIF 应为负数，实际为: {macd.Dif[0]}");
            Assert.IsTrue(macd.IsReady, "MACD 在负数 DIF 持续输入下必须能够正常完成预热并处于就绪状态！");
            Assert.IsFalse(double.IsNaN(macd.Dea[0]), "信号线 DEA 不得因输入为负数而挂死为 NaN！");
            Assert.IsTrue(macd.Dea[0] < 0.0, $"DEA 应随 DIF 递推进入负数区间，实际为: {macd.Dea[0]}");
            Assert.IsFalse(double.IsNaN(macd.Hist[0]), "HIST 柱状图不得为 NaN！");
        }

        [TestMethod]
        public void Macd_Reset_ShouldClearAllStates()
        {
            var macd = new Macd(3, 6, 3);
            for (int i = 0; i < 15; i++)
            {
                macd.Update(10.0 + i);
            }
            Assert.IsTrue(macd.IsReady);

            macd.Reset();
            Assert.IsFalse(macd.IsReady);
            Assert.IsTrue(double.IsNaN(macd.Current));
            Assert.IsTrue(double.IsNaN(macd.Dif[0]));
            Assert.IsTrue(double.IsNaN(macd.Dea[0]));
            Assert.IsTrue(double.IsNaN(macd.Hist[0]));
        }

        #endregion

        #region 7. WMA 加权移动平均测试

        [TestMethod]
        public void Wma_BoundaryAndHistoryIndexing_ShouldWorkProperly()
        {
            var wma = new Wma(3);
            Assert.IsFalse(wma.IsReady);
            Assert.IsTrue(double.IsNaN(wma.Current));

            // Feed bar 1: close = 10
            wma.Update(10, 10, 10, 10, 100);
            Assert.IsFalse(wma.IsReady);

            // Feed bar 2: close = 20
            wma.Update(20, 20, 20, 20, 100);
            Assert.IsFalse(wma.IsReady);

            // Feed bar 3: close = 30
            // WMA3 = (10*1 + 20*2 + 30*3) / (1+2+3) = (10 + 40 + 90) / 6 = 140 / 6 = 23.333333
            wma.Update(30, 30, 30, 30, 100);
            Assert.IsTrue(wma.IsReady);
            Assert.AreEqual(140.0 / 6.0, wma.Current, 1e-10);

            // Feed bar 4: close = 40
            // WMA3 = (20*1 + 30*2 + 40*3) / 6 = (20 + 60 + 120) / 6 = 200 / 6 = 33.333333
            wma.Update(40, 40, 40, 40, 100);
            Assert.AreEqual(200.0 / 6.0, wma.Current, 1e-10);
            Assert.AreEqual(140.0 / 6.0, wma[-1], 1e-10);

            // 防未来函数
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => wma[1]);

            // Reset
            wma.Reset();
            Assert.IsFalse(wma.IsReady);
            Assert.IsTrue(double.IsNaN(wma.Current));
        }

        #endregion

        #region 8. VWAP 成交量加权平均价测试

        [TestMethod]
        public void Vwap_CumulativeAndRolling_ShouldHandleZeroVolumeAndReset()
        {
            // 1. 累计模式
            var vwapCum = new Vwap(0);
            // TP1 = (10+10+10)/3 = 10, Vol = 100 -> PV = 1000
            vwapCum.Update(10, 10, 10, 10, 100);
            Assert.IsTrue(vwapCum.IsReady);
            Assert.AreEqual(10.0, vwapCum.Current, 1e-10);

            // TP2 = (20+20+20)/3 = 20, Vol = 300 -> PV = 6000
            // CumPV = 7000, CumVol = 400 => VWAP = 7000 / 400 = 17.5
            vwapCum.Update(20, 20, 20, 20, 300);
            Assert.AreEqual(17.5, vwapCum.Current, 1e-10);

            // 零成交量时，VWAP 保持当前有效值
            vwapCum.Update(30, 30, 30, 30, 0);
            Assert.AreEqual(17.5, vwapCum.Current, 1e-10);

            // Reset
            vwapCum.Reset();
            Assert.IsFalse(vwapCum.IsReady);

            // 2. 滚动 2 周期模式
            var vwap2 = new Vwap(2);
            vwap2.Update(10, 10, 10, 10, 100);
            Assert.IsFalse(vwap2.IsReady);
            vwap2.Update(20, 20, 20, 20, 100);
            Assert.IsTrue(vwap2.IsReady);
            Assert.AreEqual(15.0, vwap2.Current, 1e-10);

            // Feed bar 3: TP = 30, Vol = 100 -> 滑出 bar 1 (10*100), 剩余 (20*100 + 30*100)/200 = 25.0
            vwap2.Update(30, 30, 30, 30, 100);
            Assert.AreEqual(25.0, vwap2.Current, 1e-10);
        }

        #endregion

        #region 9. KDJ 随机指标测试

        [TestMethod]
        public void Kdj_SubStreamsAndFlatMarketZeroDivisionDefense_ShouldSucceed()
        {
            var kdj = new Kdj(3, 3, 3);
            Assert.IsFalse(kdj.IsReady);
            Assert.IsFalse(kdj.K.IsReady);

            // 极端平盘无波动测试（H = L = C），验证除以零防御
            for (int i = 0; i < 5; i++)
            {
                kdj.Update(10, 10, 10, 10, 100);
            }

            Assert.IsTrue(kdj.IsReady);
            Assert.IsFalse(double.IsNaN(kdj.K.Current));
            Assert.IsFalse(double.IsNaN(kdj.D.Current));
            Assert.IsFalse(double.IsNaN(kdj.J.Current));
            Assert.AreEqual(50.0, kdj.K.Current, 1e-8);
            Assert.AreEqual(50.0, kdj.D.Current, 1e-8);
            Assert.AreEqual(50.0, kdj.J.Current, 1e-8);

            // 子流只读保护：外部调用 Update 必须显式抛错
            Assert.ThrowsException<NotSupportedException>(() => kdj.K.Update(1, 1, 1, 1, 1));
        }

        #endregion

        #region 10. SAR 抛物转向系统测试

        [TestMethod]
        public void Sar_DirectionReversalAndBoundaries_ShouldTrackCorrectly()
        {
            var sar = new Sar(0.02, 0.2);
            Assert.IsFalse(sar.IsReady);

            // Bar 0
            sar.Update(10, 11, 9, 10, 100);
            Assert.IsFalse(sar.IsReady);

            // Bar 1: 走高进入多头
            sar.Update(10.5, 12, 10, 11.5, 100);
            Assert.IsTrue(sar.IsReady);
            Assert.IsTrue(sar.IsLong);
            Assert.AreEqual(9.0, sar.Current, 1e-10);

            // 连续暴跌击穿 SAR 触发反转
            sar.Update(8, 8.5, 6.0, 7.0, 100);
            Assert.IsFalse(sar.IsLong);
            Assert.IsTrue(sar.Current >= 10.0);

            // Reset
            sar.Reset();
            Assert.IsFalse(sar.IsReady);
        }

        #endregion

        #region 11. 指标基础契约与通用行为测试

        [TestMethod]
        public void Indicators_ColdStartUnready_ShouldReturnNaN()
        {
            var sma = new Sma(5);
            var ema = new Ema(5);
            var wma = new Wma(5);
            var vwap = new Vwap(5);
            var kdj = new Kdj(5, 3, 3);
            var sar = new Sar(0.02, 0.2);
            var rsi = new Rsi(5);
            var atr = new Atr(5);
            var macd = new Macd(3, 6, 3);
            var boll = new BollingerBands(5);

            // 未就绪状态下，访问 Current / this[0] 必须全部严格返回 double.NaN
            Assert.IsTrue(double.IsNaN(sma.Current));
            Assert.IsTrue(double.IsNaN(sma[0]));
            Assert.IsTrue(double.IsNaN(ema.Current));
            Assert.IsTrue(double.IsNaN(ema[0]));
            Assert.IsTrue(double.IsNaN(wma.Current));
            Assert.IsTrue(double.IsNaN(wma[0]));
            Assert.IsTrue(double.IsNaN(vwap.Current));
            Assert.IsTrue(double.IsNaN(vwap[0]));
            Assert.IsTrue(double.IsNaN(kdj.Current));
            Assert.IsTrue(double.IsNaN(kdj[0]));
            Assert.IsTrue(double.IsNaN(sar.Current));
            Assert.IsTrue(double.IsNaN(sar[0]));
            Assert.IsTrue(double.IsNaN(rsi.Current));
            Assert.IsTrue(double.IsNaN(rsi[0]));
            Assert.IsTrue(double.IsNaN(atr.Current));
            Assert.IsTrue(double.IsNaN(atr[0]));
            Assert.IsTrue(double.IsNaN(macd.Current));
            Assert.IsTrue(double.IsNaN(macd[0]));
            Assert.IsTrue(double.IsNaN(macd.Dif[0]));
            Assert.IsTrue(double.IsNaN(macd.Dea[0]));
            Assert.IsTrue(double.IsNaN(boll.Current));
            Assert.IsTrue(double.IsNaN(boll[0]));
            Assert.IsTrue(double.IsNaN(boll.Upper[0]));
        }

        [TestMethod]
        public void Indicators_LookaheadBias_ShouldThrowException()
        {
            var ema = new Ema(3);
            var wma = new Wma(3);
            var rsi = new Rsi(3);
            var atr = new Atr(3);
            var macd = new Macd(3, 6, 3);

            for (int i = 0; i < 10; i++)
            {
                ema.Update(10 + i);
                wma.Update(10 + i, 10 + i, 10 + i, 10 + i, 100);
                rsi.Update(10 + i);
                atr.Update(10 + i, 12 + i, 8 + i, 10 + i, 100);
                macd.Update(10 + i);
            }

            // offset > 0 访问未来 Bar 必须全部抛出 ArgumentOutOfRangeException
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = ema[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = wma[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = rsi[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = atr[1]);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _ = macd[1]);
        }

        [TestMethod]
        public void StreamIndicator_CircularBufferWrapAround_ShouldMaintainHistory()
        {
            // 环形缓冲容量为 8，推入 20 个值测试回卷（Wrap-around）
            var sma = new Sma(3, historyCapacity: 8);

            for (int i = 1; i <= 20; i++)
            {
                sma.Update(i * 10.0); // 10, 20, 30, ..., 200
            }

            Assert.IsTrue(sma.IsReady);
            // 最新值为 200，SMA(3) = (180 + 190 + 200) / 3 = 190
            Assert.AreEqual(190.0, sma.Current, 1e-12);
            Assert.AreEqual(190.0, sma[0], 1e-12);

            // 回溯最近 8 期：
            // offset 0: 190
            // offset -1: (170+180+190)/3 = 180
            // offset -2: 170
            // ...
            // offset -7: 120
            for (int k = 0; k < 8; k++)
            {
                double expected = 190.0 - k * 10.0;
                Assert.AreEqual(expected, sma[-k], 1e-12, $"offset -{k} history check failed");
            }

            // offset -8 超出容量 8，必须安全返回 NaN
            Assert.IsTrue(double.IsNaN(sma[-8]));
        }

        #endregion
    }
}
