using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CarrotBacktesting.NET.Indicators;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Indicators
{
    public record BaselineBar(
        double Open, double High, double Low, double Close, double Volume,
        double? Sma5, double? Sma20, double? Sma60, double? Sma120, double? Sma250,
        double? Ema5, double? Ema10, double? Ema20, double? Ema60, double? Ema120, double? Ema250,
        double? Wma5, double? Wma20, double? Wma60, double? Wma120,
        double? BollUp, double? BollMid, double? BollLow,
        double? MacdDif, double? MacdDea, double? MacdHist,
        double? Rsi6, double? Rsi14, double? Atr14,
        double? Sar, double? Vwap, double? Vwap20,
        double? KdjK, double? KdjD, double? KdjJ);

    [TestClass]
    public class TalibBaselineTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        [TestMethod]
        public void Indicators_AgainstRealMarketBaseline_ShouldCoverFullMovingAverageSpectrum()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Indicators", "talib_baseline_market.json");
            Assert.IsTrue(File.Exists(path), $"基准文件缺失: {path}");

            var bars = JsonSerializer.Deserialize<List<BaselineBar>>(File.ReadAllText(path), JsonOptions)!;
            Assert.IsTrue(bars.Count >= 250, "必须包含 250 根以上 Bar 以完整覆盖年线");

            // 1. SMA 完整长短均线谱系 (5, 20, 60, 120, 250)
            var sma5 = new Sma(5);
            var sma20 = new Sma(20);
            var sma60 = new Sma(60);
            var sma120 = new Sma(120);
            var sma250 = new Sma(250);

            // 2. EMA 完整长短均线谱系 (5, 10, 20, 60, 120, 250)
            var ema5 = new Ema(5);
            var ema10 = new Ema(10);
            var ema20 = new Ema(20);
            var ema60 = new Ema(60);
            var ema120 = new Ema(120);
            var ema250 = new Ema(250);

            // 3. WMA 加权均线谱系 (5, 20, 60, 120)
            var wma5 = new Wma(5);
            var wma20 = new Wma(20);
            var wma60 = new Wma(60);
            var wma120 = new Wma(120);

            // 4. 复合与振荡指标
            var boll = new BollingerBands(20, 2.0);
            var macd = new Macd(12, 26, 9);
            var rsi6 = new Rsi(6);
            var rsi14 = new Rsi(14);
            var atr = new Atr(14);
            var sar = new Sar(0.02, 0.2);
            var vwap = new Vwap();
            var vwap20 = new Vwap(20);
            var kdj = new Kdj(9, 3, 3);

            for (int i = 0; i < bars.Count; i++)
            {
                var b = bars[i];
                sma5.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                sma20.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                sma60.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                sma120.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                sma250.Update(b.Open, b.High, b.Low, b.Close, b.Volume);

                ema5.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                ema10.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                ema20.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                ema60.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                ema120.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                ema250.Update(b.Open, b.High, b.Low, b.Close, b.Volume);

                wma5.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                wma20.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                wma60.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                wma120.Update(b.Open, b.High, b.Low, b.Close, b.Volume);

                boll.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                macd.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                rsi6.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                rsi14.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                atr.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                sar.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                vwap.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                vwap20.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                kdj.Update(b.Open, b.High, b.Low, b.Close, b.Volume);

                // --- SMA 断言 (超短 5 -> 短 20 -> 中 60 -> 长 120 -> 年线 250) ---
                if (b.Sma5.HasValue) Assert.AreEqual(b.Sma5.Value, sma5.Current, 1e-11, $"Bar {i} SMA5");
                if (b.Sma20.HasValue) Assert.AreEqual(b.Sma20.Value, sma20.Current, 1e-11, $"Bar {i} SMA20");
                if (b.Sma60.HasValue) Assert.AreEqual(b.Sma60.Value, sma60.Current, 1e-11, $"Bar {i} SMA60 (季线)");
                if (b.Sma120.HasValue) Assert.AreEqual(b.Sma120.Value, sma120.Current, 1e-11, $"Bar {i} SMA120 (半年线)");
                if (b.Sma250.HasValue) Assert.AreEqual(b.Sma250.Value, sma250.Current, 1e-11, $"Bar {i} SMA250 (年线)");

                // --- EMA 断言 (超短 5 -> 双周 10 -> 月 20 -> 季 60 -> 半年 120 -> 年线 250) ---
                if (b.Ema5.HasValue) Assert.AreEqual(b.Ema5.Value, ema5.Current, 1e-10, $"Bar {i} EMA5");
                if (b.Ema10.HasValue) Assert.AreEqual(b.Ema10.Value, ema10.Current, 1e-10, $"Bar {i} EMA10");
                if (b.Ema20.HasValue) Assert.AreEqual(b.Ema20.Value, ema20.Current, 1e-10, $"Bar {i} EMA20");
                if (b.Ema60.HasValue) Assert.AreEqual(b.Ema60.Value, ema60.Current, 1e-10, $"Bar {i} EMA60 (季线)");
                if (b.Ema120.HasValue) Assert.AreEqual(b.Ema120.Value, ema120.Current, 1e-10, $"Bar {i} EMA120 (半年线)");
                if (b.Ema250.HasValue) Assert.AreEqual(b.Ema250.Value, ema250.Current, 1e-10, $"Bar {i} EMA250 (年线)");

                // --- WMA 断言 (超短 5 -> 短 20 -> 中 60 -> 长 120) ---
                if (b.Wma5.HasValue) Assert.AreEqual(b.Wma5.Value, wma5.Current, 1e-10, $"Bar {i} WMA5");
                if (b.Wma20.HasValue) Assert.AreEqual(b.Wma20.Value, wma20.Current, 1e-10, $"Bar {i} WMA20");
                if (b.Wma60.HasValue) Assert.AreEqual(b.Wma60.Value, wma60.Current, 1e-10, $"Bar {i} WMA60 (季线)");
                if (b.Wma120.HasValue) Assert.AreEqual(b.Wma120.Value, wma120.Current, 1e-10, $"Bar {i} WMA120 (半年线)");

                // --- 布林带 ---
                if (b.BollMid.HasValue)
                {
                    Assert.AreEqual(b.BollMid.Value, boll.Middle.Current, 1e-10, $"Bar {i} BOLL Mid");
                    Assert.AreEqual(b.BollUp!.Value, boll.Upper.Current, 1e-10, $"Bar {i} BOLL Up");
                    Assert.AreEqual(b.BollLow!.Value, boll.Lower.Current, 1e-10, $"Bar {i} BOLL Low");
                }

                // --- MACD ---
                if (b.MacdDif.HasValue)
                    Assert.AreEqual(b.MacdDif.Value, macd.Dif[0], 1e-9, $"Bar {i} MACD DIF");
                if (b.MacdDea.HasValue)
                {
                    Assert.AreEqual(b.MacdDea.Value, macd.Dea[0], 1e-9, $"Bar {i} MACD DEA");
                    Assert.AreEqual(b.MacdHist!.Value, macd.Hist[0], 1e-9, $"Bar {i} MACD HIST");
                }

                // --- RSI 谱系 (超短 6 vs 标准 14) ---
                if (b.Rsi6.HasValue)
                    Assert.AreEqual(b.Rsi6.Value, rsi6.Current, 1e-7, $"Bar {i} RSI6");
                if (b.Rsi14.HasValue)
                    Assert.AreEqual(b.Rsi14.Value, rsi14.Current, 1e-7, $"Bar {i} RSI14");

                // --- ATR 衰减收敛性分阶严密断言 ---
                if (i >= 130 && b.Atr14.HasValue)
                    Assert.AreEqual(b.Atr14.Value, atr.Current, 1e-6, $"Bar {i} ATR14 (Step 130+ 收敛至 1e-6)");
                if (i >= 200 && b.Atr14.HasValue)
                    Assert.AreEqual(b.Atr14.Value, atr.Current, 1e-8, $"Bar {i} ATR14 (Step 200+ 收敛至 1e-8)");

                // --- SAR 抛物转向系统 (对齐 TA-Lib C 内核) ---
                if (b.Sar.HasValue)
                    Assert.AreEqual(b.Sar.Value, sar.Current, 1e-10, $"Bar {i} SAR");

                // --- VWAP ---
                if (b.Vwap.HasValue)
                    Assert.AreEqual(b.Vwap.Value, vwap.Current, 1e-10, $"Bar {i} VWAP (全周期累计)");
                if (b.Vwap20.HasValue)
                    Assert.AreEqual(b.Vwap20.Value, vwap20.Current, 1e-10, $"Bar {i} VWAP20 (滚动20)");

                // --- KDJ 谱系 ---
                if (b.KdjK.HasValue)
                {
                    Assert.AreEqual(b.KdjK.Value, kdj.K.Current, 1e-10, $"Bar {i} KDJ K");
                    Assert.AreEqual(b.KdjD!.Value, kdj.D.Current, 1e-10, $"Bar {i} KDJ D");
                    Assert.AreEqual(b.KdjJ!.Value, kdj.J.Current, 1e-10, $"Bar {i} KDJ J");
                }
            }
        }

        [TestMethod]
        public void Indicators_AgainstSyntheticAxiomaticBaseline_ShouldMatchTalib()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Indicators", "talib_baseline_synthetic.json");
            Assert.IsTrue(File.Exists(path), $"基准文件缺失: {path}");

            var bars = JsonSerializer.Deserialize<List<BaselineBar>>(File.ReadAllText(path), JsonOptions)!;
            Assert.IsTrue(bars.Count >= 300, "合成公理数据条数必须达到 300 步");

            var sma5 = new Sma(5);
            var sma20 = new Sma(20);
            var sma60 = new Sma(60);
            var sma120 = new Sma(120);

            var ema5 = new Ema(5);
            var ema10 = new Ema(10);
            var ema20 = new Ema(20);
            var ema60 = new Ema(60);
            var ema120 = new Ema(120);

            var wma5 = new Wma(5);
            var wma20 = new Wma(20);
            var wma60 = new Wma(60);
            var wma120 = new Wma(120);

            var boll = new BollingerBands(20, 2.0);
            var macd = new Macd(12, 26, 9);
            var rsi6 = new Rsi(6);
            var rsi14 = new Rsi(14);
            var atr = new Atr(14);
            var sar = new Sar(0.02, 0.2);
            var vwap = new Vwap();
            var vwap20 = new Vwap(20);
            var kdj = new Kdj(9, 3, 3);

            for (int i = 0; i < bars.Count; i++)
            {
                var b = bars[i];
                sma5.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                sma20.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                sma60.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                sma120.Update(b.Open, b.High, b.Low, b.Close, b.Volume);

                ema5.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                ema10.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                ema20.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                ema60.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                ema120.Update(b.Open, b.High, b.Low, b.Close, b.Volume);

                wma5.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                wma20.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                wma60.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                wma120.Update(b.Open, b.High, b.Low, b.Close, b.Volume);

                boll.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                macd.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                rsi6.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                rsi14.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                atr.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                sar.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                vwap.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                vwap20.Update(b.Open, b.High, b.Low, b.Close, b.Volume);
                kdj.Update(b.Open, b.High, b.Low, b.Close, b.Volume);

                if (b.Sma5.HasValue) Assert.AreEqual(b.Sma5.Value, sma5.Current, 1e-11, $"Synthetic Bar {i} SMA5");
                if (b.Sma20.HasValue) Assert.AreEqual(b.Sma20.Value, sma20.Current, 1e-11, $"Synthetic Bar {i} SMA20");
                if (b.Sma60.HasValue) Assert.AreEqual(b.Sma60.Value, sma60.Current, 1e-11, $"Synthetic Bar {i} SMA60");
                if (b.Sma120.HasValue) Assert.AreEqual(b.Sma120.Value, sma120.Current, 1e-11, $"Synthetic Bar {i} SMA120");

                if (b.Ema5.HasValue) Assert.AreEqual(b.Ema5.Value, ema5.Current, 1e-10, $"Synthetic Bar {i} EMA5");
                if (b.Ema10.HasValue) Assert.AreEqual(b.Ema10.Value, ema10.Current, 1e-10, $"Synthetic Bar {i} EMA10");
                if (b.Ema20.HasValue) Assert.AreEqual(b.Ema20.Value, ema20.Current, 1e-10, $"Synthetic Bar {i} EMA20");
                if (b.Ema60.HasValue) Assert.AreEqual(b.Ema60.Value, ema60.Current, 1e-10, $"Synthetic Bar {i} EMA60");
                if (b.Ema120.HasValue) Assert.AreEqual(b.Ema120.Value, ema120.Current, 1e-10, $"Synthetic Bar {i} EMA120");

                if (b.Wma5.HasValue) Assert.AreEqual(b.Wma5.Value, wma5.Current, 1e-10, $"Synthetic Bar {i} WMA5");
                if (b.Wma20.HasValue) Assert.AreEqual(b.Wma20.Value, wma20.Current, 1e-10, $"Synthetic Bar {i} WMA20");
                if (b.Wma60.HasValue) Assert.AreEqual(b.Wma60.Value, wma60.Current, 1e-10, $"Synthetic Bar {i} WMA60");
                if (b.Wma120.HasValue) Assert.AreEqual(b.Wma120.Value, wma120.Current, 1e-10, $"Synthetic Bar {i} WMA120");

                if (b.BollMid.HasValue)
                {
                    Assert.AreEqual(b.BollMid.Value, boll.Middle.Current, 1e-10, $"Synthetic Bar {i} BOLL Mid");
                    Assert.AreEqual(b.BollUp!.Value, boll.Upper.Current, 1e-10, $"Synthetic Bar {i} BOLL Up");
                    Assert.AreEqual(b.BollLow!.Value, boll.Lower.Current, 1e-10, $"Synthetic Bar {i} BOLL Low");
                }

                if (b.MacdDif.HasValue)
                    Assert.AreEqual(b.MacdDif.Value, macd.Dif[0], 1e-9, $"Synthetic Bar {i} MACD DIF");
                if (b.MacdDea.HasValue)
                {
                    Assert.AreEqual(b.MacdDea.Value, macd.Dea[0], 1e-9, $"Synthetic Bar {i} MACD DEA");
                    Assert.AreEqual(b.MacdHist!.Value, macd.Hist[0], 1e-9, $"Synthetic Bar {i} MACD HIST");
                }

                if (b.Rsi6.HasValue)
                    Assert.AreEqual(b.Rsi6.Value, rsi6.Current, 1e-7, $"Synthetic Bar {i} RSI6");
                if (b.Rsi14.HasValue)
                    Assert.AreEqual(b.Rsi14.Value, rsi14.Current, 1e-7, $"Synthetic Bar {i} RSI14");

                // --- ATR 在合成公理走势中完全消除跳空偏差，从就绪起达到高精度 (1e-10) ---
                if (b.Atr14.HasValue)
                    Assert.AreEqual(b.Atr14.Value, atr.Current, 1e-10, $"Synthetic Bar {i} ATR14");

                if (b.Sar.HasValue)
                    Assert.AreEqual(b.Sar.Value, sar.Current, 1e-10, $"Synthetic Bar {i} SAR");

                if (b.Vwap.HasValue)
                    Assert.AreEqual(b.Vwap.Value, vwap.Current, 1e-10, $"Synthetic Bar {i} VWAP");
                if (b.Vwap20.HasValue)
                    Assert.AreEqual(b.Vwap20.Value, vwap20.Current, 1e-10, $"Synthetic Bar {i} VWAP20");

                if (b.KdjK.HasValue)
                {
                    Assert.AreEqual(b.KdjK.Value, kdj.K.Current, 1e-10, $"Synthetic Bar {i} KDJ K");
                    Assert.AreEqual(b.KdjD!.Value, kdj.D.Current, 1e-10, $"Synthetic Bar {i} KDJ D");
                    Assert.AreEqual(b.KdjJ!.Value, kdj.J.Current, 1e-10, $"Synthetic Bar {i} KDJ J");
                }
            }
        }
    }
}
