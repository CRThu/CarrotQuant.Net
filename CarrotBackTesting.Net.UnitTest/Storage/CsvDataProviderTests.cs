using Microsoft.VisualStudio.TestTools.UnitTesting;
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
        public void GetShareDataTest1()
        {
            string dataDir = Path.Combine(UnitTestDirectory.CsvDataDirectory, "daliy");

            string[] fields1 = new string[] { "date", "close", "volume" };
            ShareFrameMapper mapper = new ShareFrameMapper()
            {
                ["date"] = "DateTime",
                ["volume"] = "Volume",
                ["close"] = "Close",
            };
            CsvDataProvider csvDataProvider1 = new(dataDir, mapper);
            ShareFrame[] sf1 = csvDataProvider1.GetShareData("sh.000001", fields1, null, null).ToArray();

            // TODO
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