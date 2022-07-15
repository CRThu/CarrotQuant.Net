using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBackTesting.Net.UnitTest.Common;
using CarrotBacktesting.Net.DataModel;

namespace CarrotBacktesting.Net.Storage.Tests
{
    [TestClass()]
    public class SqliteDataProviderTests
    {
        [TestMethod()]
        public void GetShareDataTest()
        {
            SqliteDataProvider sqliteDataProvider = new(UnitTestFilePath.SqliteDatabasePath,
                new ShareFrameMapper()
                {
                    ["[index]"] = "[index]",
                    ["交易日期"] = "DateTime",
                    ["收盘价"] = "close"
                });
            string[] codes = new[] { "sz.000400", "sz.000401", "sz.000402", "sz.000403", "sz.000404" };
            string[] fields = new[] { "[index]", "交易日期", "收盘价" };
            DateTime startTime = new(2020, 01, 01);
            DateTime endTime = new(2022, 12, 31);
            var frames = sqliteDataProvider.GetShareData(codes, fields, startTime, endTime);

            // 使用sz.000400-sz.000404获取
            // SELECT [index] as [index],交易日期 as DateTime,收盘价 as close FROM 'sz.000400' WHERE DateTime >= '2020-01-01 00:00:00' AND DateTime <= '2022-12-31 00:00:00';
            // LINES IN DATABASE: 446*5=2230
            Console.WriteLine($"Frames Count: {frames.Count()}");
            Assert.IsTrue(frames.Count() == 2230);
            foreach(var stockInfo in frames.GroupBy(f => f.StockCode))
            {
                Console.WriteLine($"{stockInfo.Key}:{stockInfo.Count()}");
                Assert.IsTrue(stockInfo.Count() == 446);
            }
        }
    }
}