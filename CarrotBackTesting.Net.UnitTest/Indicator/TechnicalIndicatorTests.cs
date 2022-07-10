#define TA_SAR_LOOSE

using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.Indicator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using CarrotBackTesting.Net.Common;

namespace CarrotBacktesting.Net.Indicator.Tests
{
    [TestClass()]
    public class TechnicalIndicatorTests
    {
        /// <summary>
        /// AAPL, 20211231-20220131不复权, 指标与新浪或同花顺或TradingView比对误差
        /// </summary>
        public static Dictionary<string, double[]> AAPLPrice = new()
        {
            {
                "open",
                new[] { 167.48, 158.735, 164.02, 164.29, 169.08, 172.125, 174.91, 175.205, 181.115, 175.25, 175.11, 179.28, 169.93, 168.28, 171.555, 173.04, 175.85, 177.085, 180.16, 179.33, 179.47, 178.085, 177.83, 182.63, 179.61, 172.7, 172.89, 169.08, 172.32, 176.12, 175.78, 171.34, 171.51, 170, 166.98, 164.415, 160.02, 158.98, 163.5, 162.45, 165.71, 170.16 }
            },
            {
                "high",
                new[] { 170.30, 164.2, 164.96, 167.8799, 171.58, 175.96, 176.75, 179.63, 182.13, 177.74, 179.5, 181.14, 173.47, 170.58, 173.2, 175.86, 176.8499, 180.42, 181.33, 180.63, 180.57, 179.23, 182.88, 182.94, 180.17, 175.3, 174.14, 172.5, 175.18, 177.18, 176.62, 173.78, 172.54, 171.08, 169.68, 166.33, 162.3, 162.76, 164.3894, 163.84, 170.35, 175 }
            },
            {
                "low",
                new[] { 164.53, 157.8, 159.72, 164.28, 168.34, 170.7, 173.92, 174.69, 175.53, 172.21, 172.3108, 170.75, 169.69, 167.46, 169.12, 172.15, 175.27, 177.07, 178.53, 178.14, 178.09, 177.26, 177.71, 179.12, 174.64, 171.64, 171.03, 168.17, 170.82, 174.82, 171.79, 171.09, 169.405, 165.94, 164.18, 162.3, 154.7, 157.02, 157.82, 158.28, 162.8, 169.51 }
            },
            {
                "close",
                new[] { 164.77, 163.76, 161.84, 165.32, 171.18, 175.08, 174.56, 179.45, 175.74, 174.33, 179.3, 172.26, 171.14, 169.75, 172.99, 175.64, 176.28, 180.33, 179.29, 179.38, 178.2, 177.57, 182.01, 179.7, 174.92, 172, 172.17, 172.19, 175.08, 175.53, 172.19, 173.07, 169.8, 166.23, 164.51, 162.41, 161.62, 159.78, 159.69, 159.22, 170.33, 174.78 }
            },
        };
        /// <summary>
        /// AAPL 20220125-20220131指标数据, 指标可能需特别设定以保证能在20220125时处于稳定
        /// </summary>
        public static Dictionary<string, double[]> AAPLTAReference = new()
        {
            {
                "MA5_THS",
                new[] { 162.91, 161.602, 160.544, 162.128, 164.760 }
            },
            {
                "EMA5_SINA",
                new[] { 163.123, 161.979, 161.059, 164.149, 167.693 }
            },
            {
                "MACD5_10_3_DIFF_TV",
                new[] { -3.28, -3.21, -3.04, -1.08, 0.72 }
            },
            {
                "MACD5_10_3_DEA_TV",
                new[] { -3.01, -3.11, -3.07, -2.08, -0.68 }
            },
            {
                "MACD5_10_3_MACD_TV",
                new[] { -0.27, -0.10, 0.03, 1.00, 1.40 }
            },
            {
                "KDJ_9_3_3_K_THS",
                new[] { 18.53, 19.94, 21.19, 43.33, 61.86 }
            },
            {
                "KDJ_9_3_3_D_THS",
                new[] { 18.26, 18.82, 19.61, 27.52, 38.96 }
            },
            {
                "KDJ_9_3_3_J_THS",
                new[] { 19.08, 22.19, 24.35, 74.96, 107.65 }
            },
            {
                "RSI6_THS",
                new[] { 13.61, 13.49, 12.76, 65.59, 73.35 }
            },
            {
                "BOLL20_2_UP_TV",
                new[] { 185.10, 184.84, 184.30, 183.44, 183.04 }
            },
            {
                "BOLL20_2_MID_TV",
                new[] { 172.38, 171.40, 170.39, 170, 169.86 }
            },
            {
                "BOLL20_2_DOWN_TV",
                new[] { 159.67, 157.97, 156.49, 156.56, 156.68 }
            },
            {
                "WR10_THS",
                new[] { 77.4, 77.8, 79.38, 18.08, 1.08 }
            },
            {
                "SAR0.02_0.02_0.2_TV",
                new[] { 171.34, 169.01, 167.01, 154.7, 155.01 }
            },
            {
                "CCI14_THS",
                new[] { -145.39, -109.34, -94.24, 6.36, 74.75 }
            },
        };

        [TestMethod()]
        public void ArrayMoveBackTest()
        {
            double[] vs = new double[] { 1, 2, 3, 4, 5, 6, 0, 0, 0, 0 };
            double[] exp = new double[] { 0, 0, 0, 0, 1, 2, 3, 4, 5, 6 };
            int cnt = 6;
            int idx = 4;
            TechnicalIndicator.ArrayMoveBack(vs, idx, cnt);
            Assert.AreEqual(string.Join(',', vs), string.Join(',', exp));
        }

        [TestMethod()]
        public void MATest()
        {
            int MAx = 5;
            string refKey = "MA5_THS";

            double[] ma = TechnicalIndicator.MA(AAPLPrice["close"], MAx);
            (double min, double max, double avg) = CollectionVerify.CollectionElementsVerify(ma[^(AAPLTAReference[refKey].Length)..], AAPLTAReference[refKey]);
            Console.WriteLine($"{refKey} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
        }

        [TestMethod()]
        public void EMATest()
        {
            int MAx = 5;
            string refKey = "EMA5_SINA";

            double[] ma = TechnicalIndicator.EMA(AAPLPrice["close"], MAx);
            // Console.WriteLine(string.Join(',', ma));

            (double min, double max, double avg) = CollectionVerify.CollectionElementsVerify(ma[^(AAPLTAReference[refKey].Length)..], AAPLTAReference[refKey]);
            Console.WriteLine($"{refKey} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
        }

        [TestMethod()]
        public void MACDTest()
        {
            int fast = 5;
            int slow = 10;
            int sign = 3;
            string refKey1 = "MACD5_10_3_DIFF_TV";
            string refKey2 = "MACD5_10_3_DEA_TV";
            string refKey3 = "MACD5_10_3_MACD_TV";

            (double[] dif, double[] dea, double[] macd) = TechnicalIndicator.MACD(AAPLPrice["close"], fast, slow, sign);

            (double min, double max, double avg) = CollectionVerify.CollectionElementsVerify(dif[^(AAPLTAReference[refKey1].Length)..], AAPLTAReference[refKey1]);
            Console.WriteLine($"{refKey1} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
            (min, max, avg) = CollectionVerify.CollectionElementsVerify(dea[^(AAPLTAReference[refKey2].Length)..], AAPLTAReference[refKey2]);
            Console.WriteLine($"{refKey2} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
            (min, max, avg) = CollectionVerify.CollectionElementsVerify(macd[^(AAPLTAReference[refKey3].Length)..], AAPLTAReference[refKey3]);
            Console.WriteLine($"{refKey3} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
        }

        [TestMethod()]
        public void KDJTest()
        {
            int fastk = 9;
            int slowk = 3;
            int slowd = 3;
            string refKey1 = "KDJ_9_3_3_K_THS";
            string refKey2 = "KDJ_9_3_3_D_THS";
            string refKey3 = "KDJ_9_3_3_J_THS";

            (double[] k, double[] d, double[] j) = TechnicalIndicator.KDJ(AAPLPrice["high"], AAPLPrice["low"], AAPLPrice["close"], fastk, slowk, slowd);

            (double min, double max, double avg) = CollectionVerify.CollectionElementsVerify(k[^(AAPLTAReference[refKey1].Length)..], AAPLTAReference[refKey1]);
            Console.WriteLine($"{refKey1} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
            (min, max, avg) = CollectionVerify.CollectionElementsVerify(d[^(AAPLTAReference[refKey2].Length)..], AAPLTAReference[refKey2]);
            Console.WriteLine($"{refKey2} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
            (min, max, avg) = CollectionVerify.CollectionElementsVerify(j[^(AAPLTAReference[refKey3].Length)..], AAPLTAReference[refKey3]);
            Console.WriteLine($"{refKey3} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
        }

        [TestMethod()]
        public void RSITest()
        {
            int period = 6;
            string refKey = "RSI6_THS";

            double[] rsi = TechnicalIndicator.RSI(AAPLPrice["close"], period);

            (double min, double max, double avg) = CollectionVerify.CollectionElementsVerify(rsi[^(AAPLTAReference[refKey].Length)..], AAPLTAReference[refKey]);
            Console.WriteLine($"{refKey} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
        }

        [TestMethod()]
        public void BOLLTest()
        {
            int period = 20;
            int nbDev = 2;
            string refKey1 = "BOLL20_2_UP_TV";
            string refKey2 = "BOLL20_2_MID_TV";
            string refKey3 = "BOLL20_2_DOWN_TV";

            (double[] up, double[] mid, double[] down) = TechnicalIndicator.BOLL(AAPLPrice["close"], period, nbDev);

            (double min, double max, double avg) = CollectionVerify.CollectionElementsVerify(up[^(AAPLTAReference[refKey1].Length)..], AAPLTAReference[refKey1]);
            Console.WriteLine($"{refKey1} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
            (min, max, avg) = CollectionVerify.CollectionElementsVerify(mid[^(AAPLTAReference[refKey2].Length)..], AAPLTAReference[refKey2]);
            Console.WriteLine($"{refKey2} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
            (min, max, avg) = CollectionVerify.CollectionElementsVerify(down[^(AAPLTAReference[refKey3].Length)..], AAPLTAReference[refKey3]);
            Console.WriteLine($"{refKey3} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
        }

        [TestMethod()]
        public void WRTest()
        {
            int period = 10;
            string refKey = "WR10_THS";

            double[] wr = TechnicalIndicator.WR(AAPLPrice["high"], AAPLPrice["low"], AAPLPrice["close"], period);

            (double min, double max, double avg) = CollectionVerify.CollectionElementsVerify(wr[^(AAPLTAReference[refKey].Length)..], AAPLTAReference[refKey]);
            Console.WriteLine($"{refKey} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
        }

        [TestMethod()]
        public void SARTest()
        {
            string refKey = "SAR0.02_0.02_0.2_TV";

            double[] sar = TechnicalIndicator.SAR(AAPLPrice["high"], AAPLPrice["low"], 0.02, 0.1);
            //Console.WriteLine(string.Join(',', sar[^5..]));

            (double min, double max, double avg) = CollectionVerify.CollectionElementsVerify(sar[^(AAPLTAReference[refKey].Length)..], AAPLTAReference[refKey]);
            Console.WriteLine($"{refKey} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
#if TA_SAR_LOOSE
            Assert.IsTrue(avg < 2 && max < 5);
#else
            Assert.IsTrue(avg < 0.01 && max < 0.02);
#endif
        }

        [TestMethod()]
        public void CCITest()
        {
            int period = 14;
            string refKey = "CCI14_THS";

            double[] cci = TechnicalIndicator.CCI(AAPLPrice["high"], AAPLPrice["low"], AAPLPrice["close"], period);

            (double min, double max, double avg) = CollectionVerify.CollectionElementsVerify(cci[^(AAPLTAReference[refKey].Length)..], AAPLTAReference[refKey]);
            Console.WriteLine($"{refKey} Test Result: Min:{min:F2}, Max:{max:F2}, Avg:{avg:F2}.");
            Assert.IsTrue(avg < 0.01 && max < 0.02);
        }
    }
}