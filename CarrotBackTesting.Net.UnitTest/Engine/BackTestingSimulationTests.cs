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

namespace CarrotBacktesting.Net.Engine.Tests
{
    [TestClass()]
    public class BackTestingSimulationTests
    {
        [TestMethod()]
        public void UpdateFrameTest()
        {
            SimulationOptions options = new()
            {
                DataFeedSource = DataFeedSource.Sqlite,
                DataFeedPath = UnitTestFilePath.SqliteDatabasePath,

                StockCodes = UnitTestFilePath.StockCodes,
                Mapper = new DataModel.ShareFrameMapper()
                {
                    ["交易日期"] = "Time",
                    ["开盘价"] = "Open",
                    ["最高价"] = "High",
                    ["最低价"] = "Low",
                    ["收盘价"] = "Close",

                    ["滚动市盈率"] = "PE",
                },
                Fields = new string[] { "交易日期", "开盘价", "最高价", "最低价", "收盘价", "成交量","滚动市盈率", "是否ST" },
                SimulationStartTime = new DateTime(2020, 01, 01),
                SimulationEndTime = new DateTime(2020, 01, 12),
            };

            BackTestingSimulation simulation = new(options);

            Console.WriteLine($"Time       | sz.000422");
            Console.WriteLine($"{simulation.CurrentTime.FormatDateTime(isDisplayTime: false)} | {simulation.CurrentMarket["sz.000422"].Close}");
            Assert.IsTrue(simulation.CurrentTime == new DateTime(2020, 1, 2) && simulation.CurrentMarket["sz.000422"].Close == 30.8581056);
            int count = 1;
            while (simulation.IsSimulating)
            {
                count++;
                simulation.UpdateFrame();
                Console.WriteLine($"{simulation.CurrentTime.FormatDateTime(isDisplayTime: false)} | {simulation.CurrentMarket["sz.000422"].Close}");
            }
            Assert.IsTrue(count == 7);
            Assert.IsTrue(simulation.CurrentTime == new DateTime(2020, 1, 10) && simulation.CurrentMarket["sz.000422"].Close == 30.5399808);
        }
    }
}