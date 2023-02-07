using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBackTesting.Net.UnitTest.Common;
using System.IO;

namespace CarrotBacktesting.Net.Storage.Tests
{
    [TestClass()]
    public class DataFeedTests
    {
        [TestMethod()]
        public void GetMarketDataTest()
        {
            string dataDir = Path.Combine(UnitTestDirectory.SqliteDataDirectory, "sz.000400-sz.000499_1d_baostock.db");
            string[] stockcodes = UnitTestDirectory.Info["sqlite"]!["daliy"]!["stockcode"]!.AsArray().Select(o => (string)o!).ToArray()!;

            DataFeed dataFeed = new(new Engine.SimulationOptions()
            {
                DataFeedSource = DataFeedSource.Sqlite,
                DataFeedPath = dataDir,

                StockCodes = stockcodes,
                Mapper = new DataModel.ShareFrameMapper()
                {
                    ["交易日期"] = "Time",
                    ["开盘价"] = "Open",
                    ["最高价"] = "High",
                    ["最低价"] = "Low",
                    ["收盘价"] = "Close",

                    ["滚动市盈率"] = "PE",
                },
                Fields = new string[] { "交易日期", "开盘价", "最高价", "最低价", "收盘价", "成交量", "滚动市盈率", "是否ST" },
                SimulationStartTime = new DateTime(2021, 01, 01),
                SimulationEndTime = new DateTime(2021, 05, 01),
            });

            // 载入市场数据天数验证
            Console.WriteLine($"共载入{dataFeed.Count}天市场数据.");
            Assert.IsTrue(dataFeed.Count == 79);
            // 载入股票数据有效性验证
            // 工作日开盘时间
            bool isExist1 = dataFeed.GetMarketData(new DateTime(2021, 02, 01), out DataModel.MarketFrame f1);
            Console.WriteLine($"2021-02-01 是否开盘: {isExist1}, 市场存在: {f1.Frames.Count()}支股票.");
            Assert.IsTrue(isExist1);
            // sz.000405 406 412 418 已退市
            Assert.IsTrue(f1.Frames.Count() == stockcodes.Length - 4);
            Console.WriteLine($"2021-02-01 | sz.000422 | {f1["sz.000422"].Open:F2} | {f1["sz.000422"].High:F2} | {f1["sz.000422"].Low:F2} | {f1["sz.000422"].Close:F2}");
            (double o, double h, double l, double c) d20210201_sz000422_ohlc = (31.6003968000, 32.0245632000, 31.0701888000, 31.0701888000);
            Assert.IsTrue(f1["sz.000422"].Open == d20210201_sz000422_ohlc.o);
            Assert.IsTrue(f1["sz.000422"].High == d20210201_sz000422_ohlc.h);
            Assert.IsTrue(f1["sz.000422"].Low == d20210201_sz000422_ohlc.l);
            Assert.IsTrue(f1["sz.000422"].Close == d20210201_sz000422_ohlc.c);
            Assert.IsTrue(f1["sz.000422"]["Close"] == d20210201_sz000422_ohlc.c);

            Console.WriteLine($"2021-02-01 | sz.000422 | PE = {f1["sz.000422"]["PE"]} | 是否ST = {f1["sz.000422"]["是否ST"]}");
            (string pe, string st) d20200210_sz000422_otherData = ("53.450686", "是");
            Assert.IsTrue(f1["sz.000422"]["PE"] == d20200210_sz000422_otherData.pe);
            Assert.IsTrue(f1["sz.000422"]["是否ST"] == d20200210_sz000422_otherData.st);
        }
    }
}