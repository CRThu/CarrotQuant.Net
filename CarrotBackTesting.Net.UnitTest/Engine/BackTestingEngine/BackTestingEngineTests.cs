using Microsoft.VisualStudio.TestTools.UnitTesting;
using CarrotBacktesting.NET.Engine.BackTestingEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Strategy;

namespace CarrotBacktesting.NET.Engine.BackTestingEngine.Tests
{
    [TestClass()]
    public class BackTestingEngineTests
    {
        [TestMethod()]
        public void RunTest()
        {
            IEngine engine = new BackTestingEngine(new BasicStrategy());
            engine.Run();
        }
    }
}