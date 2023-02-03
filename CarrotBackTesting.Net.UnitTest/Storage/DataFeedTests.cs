using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBackTesting.Net.UnitTest.Common;

namespace CarrotBacktesting.Net.Storage.Tests
{
    [TestClass()]
    public class DataFeedTests
    {
        [TestMethod()]
        public void GetMarketDataTest()
        {
            DataFeed dataFeed = new(new Engine.SimulationOptions()
            {
                DataFeedSource = DataFeedSource.Sqlite,
                DataFeedPath = UnitTestFilePath.SqliteDatabasePath,

                ShareNames = UnitTestFilePath.StockCodes,
                FieldsMapper = new DataModel.ShareFrameMapper()
                {
                    ["交易日期"] = "DateTime",
                    ["开盘价"] = "Open",
                    ["最高价"] = "High",
                    ["最低价"] = "Low",
                    ["收盘价"] = "Close",

                    ["滚动市盈率"] = "PE",
                },
                AdditionalFields = new string[] { "滚动市盈率", "是否ST" },
                SimulationStartTime = new DateTime(2020, 01, 01),
                SimulationEndTime = new DateTime(2020, 05, 01),
            });

            // 载入市场数据天数验证
            Console.WriteLine($"共载入{dataFeed.Count}天市场数据.");
            Assert.IsTrue(dataFeed.Count == 79);
            // 载入股票数据有效性验证
            // 工作日开盘时间
            bool isExist1 = dataFeed.GetMarketData(new DateTime(2020, 02, 10), out DataModel.MarketFrame f1);
            Console.WriteLine($"2020-02-10 是否开盘: {isExist1}, 市场存在: {f1.Frames.Count()}支股票.");
            Assert.IsTrue(isExist1);
            // sz.000405 406 412 418 已退市
            Assert.IsTrue(f1.Frames.Count() == UnitTestFilePath.StockCodes.Length - 4);
            Console.WriteLine($"2020-02-10 | sz.000422 | {f1["sz.000422"].OpenPrice:F2} | {f1["sz.000422"].HighPrice:F2} | {f1["sz.000422"].LowPrice:F2} | {f1["sz.000422"].ClosePrice:F2}");
            (double o, double h, double l, double c) d20200210_sz000422_ohlc = (26.1922752000, 27.8889408000, 26.1922752000, 27.8889408000);
            Assert.IsTrue(f1["sz.000422"].OpenPrice == d20200210_sz000422_ohlc.o);
            Assert.IsTrue(f1["sz.000422"].HighPrice == d20200210_sz000422_ohlc.h);
            Assert.IsTrue(f1["sz.000422"].LowPrice == d20200210_sz000422_ohlc.l);
            Assert.IsTrue(f1["sz.000422"].ClosePrice == d20200210_sz000422_ohlc.c);
            Assert.IsTrue(f1["sz.000422"]["Close"] == d20200210_sz000422_ohlc.c);

            Console.WriteLine($"2020-02-10 | sz.000422 | PE = {f1["sz.000422"]["PE"]} | 是否ST = {f1["sz.000422"]["是否ST"]}");
            (string pe, string st) d20200210_sz000422_otherData = ("50.460782", "是");
            Assert.IsTrue(f1["sz.000422"]["PE"] == d20200210_sz000422_otherData.pe);
            Assert.IsTrue(f1["sz.000422"]["是否ST"] == d20200210_sz000422_otherData.st);

            // 周末不开盘时间
            bool isExist2 = dataFeed.GetMarketData(new DateTime(2020, 02, 08), out DataModel.MarketFrame f2);
            Console.WriteLine($"2020-02-07 是否开盘: {isExist2}, 市场存在: {f2.Frames.Count()}支股票.");
            Assert.IsTrue(!isExist2);
            // sz.000405 406 412 418 已退市
            Assert.IsTrue(f2.Frames.Count() == UnitTestFilePath.StockCodes.Length - 4);
            Console.WriteLine($"2020-02-07 | sz.000422 | {f2["sz.000422"].OpenPrice:F2} | {f2["sz.000422"].HighPrice:F2} | {f2["sz.000422"].LowPrice:F2} | {f2["sz.000422"].ClosePrice:F2}");
            (double o, double h, double l, double c) d20200207_sz000422_ohlc = (26.2983168000, 26.7224832000, 26.0862336000, 26.5104000000);
            Assert.IsTrue(f2["sz.000422"].OpenPrice == d20200207_sz000422_ohlc.o);
            Assert.IsTrue(f2["sz.000422"].HighPrice == d20200207_sz000422_ohlc.h);
            Assert.IsTrue(f2["sz.000422"].LowPrice == d20200207_sz000422_ohlc.l);
            Assert.IsTrue(f2["sz.000422"].ClosePrice == d20200207_sz000422_ohlc.c);
            Assert.IsTrue(f2["sz.000422"]["Close"] == d20200207_sz000422_ohlc.c);

            Console.WriteLine($"2020-02-07 | sz.000422 | PE = {f2["sz.000422"]["PE"]} | 是否ST = {f2["sz.000422"]["是否ST"]}");
            (string pe, string st) d20200207_sz000422_otherData = ("47.966522", "是");
            Assert.IsTrue(f2["sz.000422"]["PE"] == d20200207_sz000422_otherData.pe);
            Assert.IsTrue(f2["sz.000422"]["是否ST"] == d20200207_sz000422_otherData.st);
        }
    }
}