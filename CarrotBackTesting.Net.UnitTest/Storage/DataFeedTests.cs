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
            DataFeed dataFeed = new(new Engine.BackTestingSimulationOptions()
            {
                IsSqliteDataFeed = true,
                SqliteDatabasePath = UnitTestFilePath.SqliteDatabasePath,
            });
            Assert.Fail();
        }
    }
}