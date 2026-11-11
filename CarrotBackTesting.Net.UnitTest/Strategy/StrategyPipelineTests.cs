using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Strategy
{
    [TestClass]
    public class StrategyPipelineTests
    {
        private class MockStrategy : IStrategy
        {
            public string Name { get; }
            public int Lookback { get; }

            public MockStrategy(string name, int lookback)
            {
                Name = name;
                Lookback = lookback;
            }

            public void OnUpdate(IEngineContext context) { }
        }

        private class MockPipeline : IStrategyPipeline
        {
            private readonly List<IStrategy> _strategies = new();

            public string Name => "MockPipeline";

            public IStrategyPipeline Add(IStrategy strategy)
            {
                _strategies.Add(strategy);
                return this;
            }

            public IStrategyPipeline Compile() => this;

            public IEnumerable<IStrategy> GetStrategies() => _strategies;

            public void OnUpdate(IEngineContext context)
            {
                foreach (var s in _strategies) s.OnUpdate(context);
            }
        }

        [TestMethod]
        public void TestStrategy_DefaultLookback_Is200()
        {
            IStrategy strategy = new MockStrategy("S1", 0);
            // 显式指定 0 时为 0
            Assert.AreEqual(0, strategy.Lookback);

            // 未重写默认接口方法时为 200
            IStrategy defaultStrategy = new DefaultStrategyMock();
            Assert.AreEqual(200, defaultStrategy.Lookback);
        }

        private class DefaultStrategyMock : IStrategy
        {
            public string Name => "Default";
            public void OnUpdate(IEngineContext context) { }
        }

        [TestMethod]
        public void TestPipeline_AggregatesMaxLookback_FromChildren()
        {
            IStrategyPipeline pipeline = new MockPipeline();
            pipeline.Add(new MockStrategy("S1", 60));
            pipeline.Add(new MockStrategy("S2", 250));
            pipeline.Add(new MockStrategy("S3", 120));

            // 管线应自动聚合取最大值 250
            Assert.AreEqual(250, pipeline.Lookback);
        }
    }
}
