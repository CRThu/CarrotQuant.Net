﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBackTesting.Net.UnitTest.Common;
using System.IO;
using CarrotBackTesting.Net.Common;
using CarrotBacktesting.Net.DataModel;

namespace CarrotBacktesting.Net.Storage.Tests
{
    [TestClass()]
    public class CsvDataProviderTests
    {
        [TestMethod()]
        public void GetShareDataTest()
        {
            string dataDir = Path.Combine(UnitTestDirectory.CsvDataDirectory, "daliy");

            string[] fields1 = new string[] { "date", "close", "volume", "amount" };
            ShareFrameMapper mapper = new ShareFrameMapper()
            {
                ["date"] = "DateTime",
                ["volume"] = "Volume",
                ["close"] = "Close",
                ["amount"] = "Amount",
            };
            CsvDataProvider csvDataProvider1 = new(dataDir, mapper);
            ShareFrame[] sf1 = csvDataProvider1.GetShareData("sh.000001", fields1, null, null).ToArray();

            Assert.IsTrue(sf1.Where(sf => sf.DateTime == new DateTime(2022, 06, 01)).First().Params!.Count == 1);
            Assert.AreEqual("402419941241.2000", sf1.Where(sf => sf.DateTime == new DateTime(2022, 06, 01)).First()["Amount"]);
            Assert.AreEqual(0, sf1.Where(sf => sf.DateTime == new DateTime(2022, 06, 01)).First().OpenPrice);
            Assert.AreEqual(3182.1566, sf1.Where(sf => sf.DateTime == new DateTime(2022, 06, 01)).First().ClosePrice);
            Assert.AreEqual(36566443200, sf1.Where(sf => sf.DateTime == new DateTime(2022, 06, 01)).First().Volume);
        }

        [TestMethod()]
        public void GetAllStockCodeTest()
        {
            string[] stockcodes = UnitTestDirectory.Info["csv"]!["daliy"]!["stockcode"]!.AsArray().Select(o => (string)o!).ToArray()!;

            string dataDir = Path.Combine(UnitTestDirectory.CsvDataDirectory, "daliy");
            CsvDataProvider csvDataProvider = new(dataDir, null);
            string[] getstockcodes = csvDataProvider.GetAllStockCode();

            Assert.IsTrue(CollectionVerify.CompareArray(stockcodes, getstockcodes));
        }
    }
}