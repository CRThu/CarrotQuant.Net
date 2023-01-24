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
                stockCode: "SH.000001",
                dateTime: t,
                openPrice: 1.5, highPrice: 3.5, lowPrice: 1.0, closePrice: 2.0,
                volume: 10000, isTrading: true);
            marketFrame1.Add(shareFrame1);
            shareFrame1 = new(
                stockCode: "SH.000002",
                dateTime: t,
                openPrice: 1.5, highPrice: 3.5, lowPrice: 1.0, closePrice: 2.0,
                volume: 20000, isTrading: true);
            marketFrame1.Add(shareFrame1);
            shareFrame1 = new(
                stockCode: "SH.000003",
                dateTime: t,
                openPrice: 1.5, highPrice: 3.5, lowPrice: 1.0, closePrice: 2.0,
                volume: 30000, isTrading: true);
            marketFrame1.Add(shareFrame1);
            shareFrame1 = null;

            Assert.AreEqual(3, marketFrame1.Count);
            Assert.AreEqual(t, marketFrame1.DateTime);
            Assert.AreEqual("SH.000001", marketFrame1["SH.000001"]!.StockCode);
            Assert.AreEqual(10000, marketFrame1["SH.000001"]!.Volume);
            Assert.AreEqual("SH.000002", marketFrame1["SH.000002"]!.StockCode);
            Assert.AreEqual(20000, marketFrame1["SH.000002"]!.Volume);
            Assert.AreEqual("SH.000003", marketFrame1["SH.000003"]!.StockCode);
            Assert.AreEqual(30000, marketFrame1["SH.000003"]!.Volume);
        }

        [TestMethod()]
        public void MarketFrameTest1()
        {
            DateTime t = new DateTime(2022, 01, 01, 11, 22, 33);

            ShareFrame? shareFrame1 = new(
                stockCode: "SH.000001",
                dateTime: t,
                openPrice: 1.5, highPrice: 3.5, lowPrice: 1.0, closePrice: 2.0,
                volume: 10000, isTrading: true);
            ShareFrame? shareFrame2 = new(
                stockCode: "SH.000002",
                dateTime: t,
                openPrice: 1.5, highPrice: 3.5, lowPrice: 1.0, closePrice: 2.0,
                volume: 20000, isTrading: true);
            ShareFrame? shareFrame3 = new(
                stockCode: "SH.000003",
                dateTime: t,
                openPrice: 1.5, highPrice: 3.5, lowPrice: 1.0, closePrice: 2.0,
                volume: 30000, isTrading: true);

            Dictionary<string, ShareFrame> sfd = new()
            {
                { shareFrame1.StockCode, shareFrame1 },
                { shareFrame2.StockCode, shareFrame2 },
                { shareFrame3.StockCode, shareFrame3 },
            };
            MarketFrame marketFrame1 = new(t, sfd);
            Assert.AreEqual(3, marketFrame1.Count);
            Assert.AreEqual(t, marketFrame1.DateTime);
            Assert.AreEqual(10000, marketFrame1["SH.000001"]!.Volume);
            Assert.AreEqual(20000, marketFrame1["SH.000002"]!.Volume);
            Assert.AreEqual(30000, marketFrame1["SH.000003"]!.Volume);
        }
    }
}