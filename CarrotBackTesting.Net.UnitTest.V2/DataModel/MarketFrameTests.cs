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
    public class MarketFrameTests
    {
        [TestMethod()]
        public void MarketFrameTest()
        {
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
            shareFrame1 = new(
                code: "SH.000003",
                time: t,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 30000, status: true);
            marketFrame1.Add(shareFrame1);
            shareFrame1 = null;

            Assert.AreEqual(3, marketFrame1.Count);
            Assert.AreEqual(t, marketFrame1.Time);
            Assert.AreEqual("SH.000001", marketFrame1["SH.000001"]!.Code);
            Assert.AreEqual(10000, marketFrame1["SH.000001"]!.Volume);
            Assert.AreEqual("SH.000002", marketFrame1["SH.000002"]!.Code);
            Assert.AreEqual(20000, marketFrame1["SH.000002"]!.Volume);
            Assert.AreEqual("SH.000003", marketFrame1["SH.000003"]!.Code);
            Assert.AreEqual(30000, marketFrame1["SH.000003"]!.Volume);
        }

        [TestMethod()]
        public void MarketFrameTest1()
        {
            DateTime t = new DateTime(2022, 01, 01, 11, 22, 33);

            ShareFrame? shareFrame1 = new(
                code: "SH.000001",
                time: t,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 10000, status: true);
            ShareFrame? shareFrame2 = new(
                code: "SH.000002",
                time: t,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 20000, status: true);
            ShareFrame? shareFrame3 = new(
                code: "SH.000003",
                time: t,
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 30000, status: true);

            Dictionary<string, ShareFrame> sfd = new()
            {
                { shareFrame1.Code, shareFrame1 },
                { shareFrame2.Code, shareFrame2 },
                { shareFrame3.Code, shareFrame3 },
            };
            MarketFrame marketFrame1 = new(t, sfd);
            Assert.AreEqual(3, marketFrame1.Count);
            Assert.AreEqual(t, marketFrame1.Time);
            Assert.AreEqual(10000, marketFrame1["SH.000001"]!.Volume);
            Assert.AreEqual(20000, marketFrame1["SH.000002"]!.Volume);
            Assert.AreEqual(30000, marketFrame1["SH.000003"]!.Volume);
        }
    }
}