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
            Assert.Fail();
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