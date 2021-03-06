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
                SqliteDatabasePath = UnitTestFilePath.SqliteDatabasePath,

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
                SimulationEndTime = new DateTime(2020, 01, 12),
            };

            BackTestingSimulation simulation = new(options);

            Console.WriteLine($"Time       | sz.000422");
            Console.WriteLine($"{simulation.CurrentTime.FormatDateTime(isDisplayTime: false)} | {simulation.CurrentMarket["sz.000422"].ClosePrice}");
            Assert.IsTrue(simulation.CurrentTime == new DateTime(2020, 1, 2) && simulation.CurrentMarket["sz.000422"].ClosePrice == 30.8581056);
            int count = 1;
            while (simulation.IsSimulating)
            {
                count++;
                simulation.UpdateFrame();
                Console.WriteLine($"{simulation.CurrentTime.FormatDateTime(isDisplayTime: false)} | {simulation.CurrentMarket["sz.000422"].ClosePrice}");
            }
            Assert.IsTrue(count == 7);
            Assert.IsTrue(simulation.CurrentTime == new DateTime(2020, 1, 10) && simulation.CurrentMarket["sz.000422"].ClosePrice == 30.5399808);
        }
    }
}