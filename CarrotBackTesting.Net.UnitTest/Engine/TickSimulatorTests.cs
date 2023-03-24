using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.Net.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.Net.Storage;
using CarrotBackTesting.Net.UnitTest.Common;
using CarrotBacktesting.Net.Common;
using System.IO;

namespace CarrotBacktesting.Net.Engine.Tests
{
    [TestClass()]
    public class TickSimulatorTests
    {
        //[TestMethod()]
        //public void UpdateFrameTest()
        //{
        //    string dataDir = Path.Combine(UnitTestDirectory.SqliteDataDirectory, "sz.000400-sz.000499_1d_baostock.db");
        //    string[] stockcodes = UnitTestDirectory.Info["sqlite"]!["daily"]!["stockcode"]!.AsArray().Select(o => (string)o!).ToArray()!;

        //    SimulationOptions options = new()
        //    {
        //        DataFeedSource = DataFeedSource.Sqlite,
        //        DataFeedPath = dataDir,

        //        StockCodes = stockcodes,
        //        Mapper = new DataModel.ShareFrameMapper()
        //        {
        //            ["交易日期"] = "Time",
        //            ["开盘价"] = "Open",
        //            ["最高价"] = "High",
        //            ["最低价"] = "Low",
        //            ["收盘价"] = "Close",

        //            ["滚动市盈率"] = "PE",
        //        },
        //        Fields = new string[] { "交易日期", "开盘价", "最高价", "最低价", "收盘价", "成交量","滚动市盈率", "是否ST" },
        //        SimulationStartTime = new DateTime(2021, 01, 01),
        //        SimulationEndTime = new DateTime(2021, 01, 12),
        //    };

        //    BackTestingSimulation simulation = new(options);

        //    Console.WriteLine($"Time       | sz.000422");
        //    Console.WriteLine($"{simulation.CurrentTime.FormatDateTime(isDisplayTime: false)} | {simulation.CurrentMarket["sz.000422"].Close}");
        //    Assert.IsTrue(simulation.CurrentTime == new DateTime(2021, 1, 4) && simulation.CurrentMarket["sz.000422"].Close == 33.2970624);
        //    int count = 1;
        //    while (simulation.IsSimulating)
        //    {
        //        count++;
        //        simulation.UpdateFrame();
        //        Console.WriteLine($"{simulation.CurrentTime.FormatDateTime(isDisplayTime: false)} | {simulation.CurrentMarket["sz.000422"].Close}");
        //    }
        //    Assert.IsTrue(count == 7);
        //    Assert.IsTrue(simulation.CurrentTime == new DateTime(2021, 1, 12) && simulation.CurrentMarket["sz.000422"].Close == 31.3883136000);
        //}
    }
}