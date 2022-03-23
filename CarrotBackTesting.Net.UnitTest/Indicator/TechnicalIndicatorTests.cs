using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.NET.Indicator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Indicator.Tests
{
    [TestClass()]
    public class TechnicalIndicatorTests
    {
        /// <summary>
        /// AAPL,20220103-20220131不复权, 指标与Sina和TradingView比对误差
        /// </summary>
        public static Dictionary<string, double[]> AAPLPrice = new()
        {
            {
                "open",
                new[] { 177.83, 182.63, 179.61, 172.7, 172.89,
                    169.08, 172.32, 176.12, 175.78, 171.34,
                    171.51, 170, 166.98, 164.415, 160.02,
                    158.98, 163.5, 162.45, 165.71, 170.16 }
            },
            {
                "high",
                new[] { 182.88, 182.94, 180.7, 175.3, 174.14,
                    172.5, 175.18, 177.18, 176.62, 173.78,
                    172.54, 171.08, 169.68, 166.33, 162.3,
                    162.76, 164.389, 163.84, 170.35, 175, }
            },
            {
                "low",
                new[] { 182.01, 179.7, 174.92, 172, 172.17,
                172.19, 175.08, 175.53, 172.19, 173.07,
                169.8, 166.23, 164.51, 162.41, 161.62,
                159.78, 159.69, 159.22, 170.33, 174.78, }
            },
            {
                "close",
                new[] {177.71, 179.12, 174.64, 171.64, 171.03,
                    168.17, 170.82, 174.82, 171.79, 171.09,
                    169.405, 165.94, 164.18, 162.3, 154.7,
                    157.02, 157.82, 158.28, 162.8, 169.51, }
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
            double [] ma = TechnicalIndicator.MA(AAPLPrice["close"]);
            // TODO
        }

        [TestMethod()]
        public void EMATest()
        {
            Assert.Fail();
        }

        [TestMethod()]
        public void MACDTest()
        {
            Assert.Fail();
        }

        [TestMethod()]
        public void KDJTest()
        {
            Assert.Fail();
        }
    }
}