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
                code: "SH.000001",
                time: new DateTime(2022, 01, 01, 11, 22, 33),
                open: 1.5, high: 3.5, low: 1.0, close: 2.0,
                volume: 30000, status: true);

            Assert.AreEqual("SH.000001", shareFrame1.Code);
            Assert.AreEqual(new DateTime(2022, 01, 01, 11, 22, 33), shareFrame1.Time);
            Assert.AreEqual(1.5, shareFrame1.Open);
            Assert.AreEqual(3.5, shareFrame1.High);
            Assert.AreEqual(1.0, shareFrame1.Low);
            Assert.AreEqual(2.0, shareFrame1.Close);
            Assert.AreEqual(30000, shareFrame1.Volume);
            Assert.AreEqual(true, shareFrame1.Status);
            Assert.IsTrue(shareFrame1.Params is null);

            Assert.AreEqual("SH.000001", shareFrame1["Code"]);
            Assert.AreEqual(new DateTime(2022, 01, 01, 11, 22, 33), shareFrame1["Time"]);
            Assert.AreEqual(1.5, shareFrame1["Open"]);
            Assert.AreEqual(3.5, shareFrame1["High"]);
            Assert.AreEqual(1.0, shareFrame1["Low"]);
            Assert.AreEqual(2.0, shareFrame1["Close"]);
            Assert.AreEqual(30000, shareFrame1["Volume"]);
            Assert.AreEqual(true, shareFrame1["Status"]);
            Assert.IsTrue(shareFrame1["par123"] is null);
            Assert.IsTrue(shareFrame1["par456"] is null);
        }


        [TestMethod()]
        public void ShareFrameTest2()
        {
            var sharedict1 = new Dictionary<string, object>() {
                { "Time", "2022-01-01 09:00:00" },
                { "Close", "123.456" },
                { "Status", "false" } };

            var sharedict2 = new Dictionary<string, object>() {
                { "Code", "code.001" },
                { "Time", "2022-01-01 09:00:00" },
                { "Close", "123.456" },
                { "PEInt", 333 },
                { "PEStr", "333" },
                { "CODE", "CODE.001" },
                { "Status", "false" } };


            ShareFrame shareFrame1 = new(sharedict1, "code.001");
            ShareFrame shareFrame2 = new(sharedict2);

            Assert.AreEqual("code.001", shareFrame1.Code);
            Assert.AreEqual(new DateTime(2022, 01, 01, 09, 00, 00), shareFrame1.Time);
            Assert.AreEqual(0, shareFrame1.Open);
            Assert.AreEqual(0, shareFrame1.High);
            Assert.AreEqual(0, shareFrame1.Low);
            Assert.AreEqual(123.456, shareFrame1.Close);
            Assert.AreEqual(0, shareFrame1.Volume);
            Assert.AreEqual(false, shareFrame1.Status);
            Assert.IsTrue(shareFrame1.Params is null);

            Assert.AreEqual("code.001", shareFrame2.Code);
            Assert.AreEqual(new DateTime(2022, 01, 01, 09, 00, 00), shareFrame2.Time);
            Assert.AreEqual(0, shareFrame2.Open);
            Assert.AreEqual(0, shareFrame2.High);
            Assert.AreEqual(0, shareFrame2.Low);
            Assert.AreEqual(123.456, shareFrame2.Close);
            Assert.AreEqual(0, shareFrame2.Volume);
            Assert.AreEqual(false, shareFrame2.Status);
            Assert.IsTrue(shareFrame2.Params!.Count == 3);
            Assert.AreEqual(333, shareFrame2["PEInt"]);
            Assert.AreEqual("333", shareFrame2["PEStr"]);
            Assert.AreEqual("Int32", shareFrame2["PEInt"]!.GetType().Name);
            Assert.AreEqual("String", shareFrame2["PEStr"]!.GetType().Name);
            Assert.AreEqual("CODE.001", shareFrame2["CODE"]);
            Assert.IsTrue(shareFrame2["PB"] is null);
        }

        [TestMethod()]
        public void ShareFrameTest3()
        {
            var sharedict = new Dictionary<string, object>() {
                { "Code", "code.001" },
                { "Time", "2022-01-01 09:00:00" },
                { "Close", "123.456" },
                { "PEInt", 333 },
                { "PEStr", "333" },
                { "CODE", "CODE.001" },
                { "Status", "false" } };
            ShareFrame? shareFrame1 = new(sharedict);
            ShareFrame shareFrame2 = new(shareFrame1!);
            shareFrame1 = null;

            Assert.AreEqual("code.001", shareFrame2.Code);
            Assert.AreEqual(new DateTime(2022, 01, 01, 09, 00, 00), shareFrame2.Time);
            Assert.AreEqual(0, shareFrame2.Open);
            Assert.AreEqual(0, shareFrame2.High);
            Assert.AreEqual(0, shareFrame2.Low);
            Assert.AreEqual(123.456, shareFrame2.Close);
            Assert.AreEqual(0, shareFrame2.Volume);
            Assert.AreEqual(false, shareFrame2.Status);
            Assert.IsTrue(shareFrame2.Params!.Count == 3);
            Assert.AreEqual(333, shareFrame2["PEInt"]);
            Assert.AreEqual("333", shareFrame2["PEStr"]);
            Assert.AreEqual("Int32", shareFrame2["PEInt"]!.GetType().Name);
            Assert.AreEqual("String", shareFrame2["PEStr"]!.GetType().Name);
            Assert.AreEqual("CODE.001", shareFrame2["CODE"]);
            Assert.IsTrue(shareFrame2["PB"] is null);
        }

        [TestMethod()]
        public void ShareFrameTest4()
        {
            var cols = new string[] { "Code", "Time", "Close", "PEStr", "CODE", "Status" };
            var dat = new string[] { "code.001", "2022-01-01 09:00:00", "123.456", "333", "CODE.001", "false" };
            ShareFrame? shareFrame1 = new(cols, dat);

            Assert.AreEqual("code.001", shareFrame1.Code);
            Assert.AreEqual(new DateTime(2022, 01, 01, 09, 00, 00), shareFrame1.Time);
            Assert.AreEqual(0, shareFrame1.Open);
            Assert.AreEqual(0, shareFrame1.High);
            Assert.AreEqual(0, shareFrame1.Low);
            Assert.AreEqual(123.456, shareFrame1.Close);
            Assert.AreEqual(0, shareFrame1.Volume);
            Assert.AreEqual(false, shareFrame1.Status);
            Assert.IsTrue(shareFrame1.Params!.Count == 2);
            Assert.AreEqual("333", shareFrame1["PEStr"]);
            Assert.AreEqual("String", shareFrame1["PEStr"]!.GetType().Name);
            Assert.AreEqual("CODE.001", shareFrame1["CODE"]);
            Assert.IsTrue(shareFrame1["PB"] is null);
        }

        [TestMethod()]
        public void ShareFrameTest5()
        {
            var cols = new string[] { "Code", "Time", "Close", "PEStr", "PENum", "CODE", "Status" };
            var mask = new bool[] { true, true, true, true, true, false, true };
            var types = new string?[] { null, null, null, null, "System.Double", null, null };
            var dat = new string[] { "code.001", "2022-01-01 09:00:00", "123.456", "333.33", "333.33", "CODE.001", "false" };
            ShareFrame? shareFrame1 = new(cols, dat, mask, types);
            
            Assert.AreEqual("code.001", shareFrame1.Code);
            Assert.AreEqual(new DateTime(2022, 01, 01, 09, 00, 00), shareFrame1.Time);
            Assert.AreEqual(0, shareFrame1.Open);
            Assert.AreEqual(0, shareFrame1.High);
            Assert.AreEqual(0, shareFrame1.Low);
            Assert.AreEqual(123.456, shareFrame1.Close);
            Assert.AreEqual(0, shareFrame1.Volume);
            Assert.AreEqual(false, shareFrame1.Status);
            Assert.IsTrue(shareFrame1.Params!.Count == 2);
            Assert.AreEqual("333.33", shareFrame1["PEStr"]);
            Assert.AreEqual(333.33, shareFrame1["PENum"]);
            Assert.AreEqual("System.String", shareFrame1["PEStr"]!.GetType().FullName);
            Assert.AreEqual("System.Double", shareFrame1["PENum"]!.GetType().FullName);
            Assert.IsTrue(shareFrame1["PB"] is null);
        }
    }
}