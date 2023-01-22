using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.DataModel;
using System;
using System.Collections.Generic;

namespace CarrotBacktesting.Net.DataModel.Tests
{
    [TestClass()]
    public class ShareFrameTests
    {
        [TestMethod()]
        public void ShareFrameTest1()
        {
            ShareFrame shareFrame1 = new(
                stockCode: "SH.000001",
                dateTime: new DateTime(2022, 01, 01, 11, 22, 33),
                openPrice: 1.5, highPrice: 3.5, lowPrice: 1.0, closePrice: 2.0,
                volume: 30000, isTrading: true);

            Assert.AreEqual("SH.000001", shareFrame1.StockCode);
            Assert.AreEqual(new DateTime(2022, 01, 01, 11, 22, 33), shareFrame1.DateTime);
            Assert.AreEqual(1.5, shareFrame1.OpenPrice);
            Assert.AreEqual(3.5, shareFrame1.HighPrice);
            Assert.AreEqual(1.0, shareFrame1.LowPrice);
            Assert.AreEqual(2.0, shareFrame1.ClosePrice);
            Assert.AreEqual(30000, shareFrame1.Volume);
            Assert.AreEqual(true, shareFrame1.IsTrading);

            Assert.AreEqual("SH.000001", shareFrame1["StockCode"]);
            Assert.AreEqual(new DateTime(2022, 01, 01, 11, 22, 33), shareFrame1["DateTime"]);
            Assert.AreEqual(1.5, shareFrame1["Open"]);
            Assert.AreEqual(3.5, shareFrame1["High"]);
            Assert.AreEqual(1.0, shareFrame1["Low"]);
            Assert.AreEqual(2.0, shareFrame1["Close"]);
            Assert.AreEqual(30000, shareFrame1["Volume"]);
            Assert.AreEqual(true, shareFrame1["IsTrading"]);
            Assert.IsTrue(shareFrame1["par123"] is null);
            Assert.IsTrue(shareFrame1["par456"] is null);
        }


        [TestMethod()]
        public void ShareFrameTest2()
        {
            var sharedict1 = new Dictionary<string, object>() {
                { "DateTime", "2022-01-01 09:00:00" },
                { "Close", "123.456" },
                { "IsTrading", "false" } };

            var sharedict2 = new Dictionary<string, object>() {
                { "StockCode", "code.001" },
                { "DateTime", "2022-01-01 09:00:00" },
                { "Close", "123.456" },
                { "PE", "333" },
                { "IsTrading", "false" } };


            ShareFrame shareFrame1 = new(sharedict1, "code.001");
            ShareFrame shareFrame2 = new(sharedict2);

            // TODO
        }
    }
}