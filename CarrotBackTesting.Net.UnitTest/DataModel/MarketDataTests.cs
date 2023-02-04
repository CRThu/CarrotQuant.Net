using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel.Tests
{
    [TestClass()]
    public class MarketDataTests
    {
        [TestMethod()]
        public void MarketDataTest1()
        {
            MarketData marketData = new();

            DateTime t = new DateTime(2022, 01, 01, 11, 22, 33);
            MarketFrame marketFrame1 = new(t);

            ShareFrame? shareFrame1 = new(
                code: "SH.000001",
                time: t,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 10000, status: true);
            marketFrame1.Add(shareFrame1);
            shareFrame1 = new(
                code: "SH.000002",
                time: t,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 20000, status: true);
            marketFrame1.Add(shareFrame1);

            DateTime t2 = new DateTime(2022, 01, 02, 11, 22, 33);
            MarketFrame marketFrame2 = new(t2);
            shareFrame1 = new(
                code: "SH.000001",
                time: t2,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 10000, status: true);
            marketFrame2.Add(shareFrame1);
            shareFrame1 = new(
                code: "SH.000002",
                time: t2,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 20000, status: true);
            marketFrame2.Add(shareFrame1);

            marketData.Add(marketFrame1);
            marketData.Add(marketFrame2);

            Assert.AreEqual(2, marketData.Count);
            Assert.AreEqual(2, marketData.Frames.First().Count);
            Assert.AreEqual(t, marketData[0].Time);
            Assert.AreEqual(t2, marketData[1].Time);
            Assert.AreEqual(10000, marketData[t]["SH.000001"]!.Volume);
            Assert.AreEqual(20000, marketData[t2]["SH.000002"]!.Volume);
        }

        [TestMethod()]
        public void MarketDataTest2()
        {
            MarketData marketData = new();

            DateTime t = new DateTime(2022, 01, 01, 11, 22, 33);
            ShareFrame? shareFrame1 = new(
                code: "SH.000001",
                time: t,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 10000, status: true);
            marketData.Add(shareFrame1);
            shareFrame1 = new(
                code: "SH.000002",
                time: t,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 20000, status: true);
            marketData.Add(shareFrame1);

            DateTime t2 = new DateTime(2022, 01, 02, 11, 22, 33);
            shareFrame1 = new(
                code: "SH.000001",
                time: t2,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 10000, status: true);
            marketData.Add(shareFrame1);
            shareFrame1 = new(
                code: "SH.000002",
                time: t2,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 20000, status: true);
            marketData.Add(shareFrame1);

            Assert.AreEqual(2, marketData.Count);
            Assert.AreEqual(2, marketData.Frames.First().Count);
            Assert.AreEqual(t, marketData[0].Time);
            Assert.AreEqual(t2, marketData[1].Time);
            Assert.AreEqual(10000, marketData[t]["SH.000001"]!.Volume);
            Assert.AreEqual(20000, marketData[t2]["SH.000002"]!.Volume);
        }

        [TestMethod()]
        public void TryGetNearbyTest()
        {

        }
    }
}