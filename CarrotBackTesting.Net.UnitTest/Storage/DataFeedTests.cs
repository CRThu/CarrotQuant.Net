using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBackTesting.Net.UnitTest.Common;
using System.IO;
using static CarrotBacktesting.Net.Common.Enums;


namespace CarrotBacktesting.Net.Storage.Tests
{
    [TestClass()]
    public class DataFeedTests
    {
        // TODO 删除Sqlite数据源
        /*
        [TestMethod()]
        public void GetMarketDataTest()
        {
            string dataDir = Path.Combine(UnitTestDirectory.SqliteDataDirectory, "sz.000400-sz.000499_1d_baostock.db");
            string codesPath = Path.Combine(UnitTestDirectory.JsonDirectory, "stockcodes.baostock.sqlite.daily.json");
            string fieldsPath = Path.Combine(UnitTestDirectory.JsonDirectory, "fields.baostock.sqlite.daily.json");
            string mapperPath = Path.Combine(UnitTestDirectory.JsonDirectory, "mapper.baostock.sqlite.daily.json");

            DataFeed dataFeed = new(new Engine.SimulationOptions()
            {
                DataFeedSource = DataFeedSource.Sqlite,
                DataFeedPath = dataDir,
                StockCodesJsonFilePath = codesPath,
                FieldsJsonFilePath = fieldsPath,
                MapperJsonFilePath = mapperPath,
                SimulationStartTime = new DateTime(2021, 01, 01),
                SimulationEndTime = new DateTime(2021, 05, 01),
            }.Parse());

            // 载入市场数据天数验证
            Console.WriteLine($"共载入{dataFeed.Count}天市场数据.");
            Assert.IsTrue(dataFeed.Count == 79);

            // 载入股票数据有效性验证
            // 工作日开盘时间
            bool isExist1 = dataFeed.GetMarketData(new DateTime(2021, 02, 01), out DataModel.MarketFrame f1);
            Console.WriteLine($"2021-02-01 是否开盘: {isExist1}, 市场存在: {f1.Frames.Count()}支股票.");
            Assert.IsTrue(isExist1);
            // sz.000405 406 412 418 已退市
            Assert.IsTrue(f1.Frames.Count() == dataFeed.Options.StockCodes!.Length - 4);

            Console.WriteLine($"2021-02-01 | sz.000422 | {f1["sz.000422"].Open:F2} | {f1["sz.000422"].High:F2} | {f1["sz.000422"].Low:F2} | {f1["sz.000422"].Close:F2}");
            (double o, double h, double l, double c) = (31.6003968000, 32.0245632000, 31.0701888000, 31.0701888000);
            Assert.IsTrue(f1["sz.000422"].Open == o);
            Assert.IsTrue(f1["sz.000422"].High == h);
            Assert.IsTrue(f1["sz.000422"].Low == l);
            Assert.IsTrue(f1["sz.000422"].Close == c);
            Assert.IsTrue(f1["sz.000422"]["Close"] == c);

            Console.WriteLine($"2021-02-01 | sz.000422 | PE = {f1["sz.000422"]["PE"]} | 是否ST = {f1["sz.000422"]["ST"]}");
            (string pe, string st) = ("53.450686", "是");
            Assert.IsTrue(f1["sz.000422"]["PE"] == pe);
            Assert.IsTrue(f1["sz.000422"]["ST"] == st);

            // Generate Serialization Json of SimulationOptions
            // string optionsPath = Path.Combine(UnitTestDirectory.JsonDirectory, "simulationoptions.baostock.sqlite.daily.json");
            // dataFeed.Options.Serialize(optionsPath);
            // Console.WriteLine($"Generated simulationOptions json file at {optionsPath}.");
        }
        */
    }
}