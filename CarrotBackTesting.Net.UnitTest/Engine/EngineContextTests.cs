using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine
{
    [TestClass]
    public class EngineContextTests
    {
        private class MockTimeseriesRegistry : ITimeseriesRegistry
        {
            public IMarketMetadata Metadata { get; }

            public MockTimeseriesRegistry(IMarketMetadata metadata)
            {
                Metadata = metadata;
            }

            public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged => null!;
            public bool HasField(string fieldName) => false;
            public void StepTo(int currentLogicalRow) { }
            public int RegisterTable(string tableName) => 0;
            public int GetTableIndex(string tableName) => 0;
            public string GetTableName(int tableIndex) => string.Empty;
        }

        private static ITimeseriesRegistry CreateMockRegistry()
        {
            var dates = new[] { new DateTime(2025, 1, 1), new DateTime(2025, 1, 2) };
            var symbols = new[] { "sh.600000" };
            var meta = new DynamicMarketMetadata(symbols, dates);
            return new MockTimeseriesRegistry(meta);
        }

        [TestMethod]
        public void Constructor_NullRequiredParams_ThrowsException()
        {
            var ts = CreateMockRegistry();
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());

            Assert.ThrowsException<ArgumentNullException>(() => new EngineContext(null!, broker));
            Assert.ThrowsException<ArgumentNullException>(() => new EngineContext(ts, null!));
        }

        [TestMethod]
        public void Constructor_DefaultOptionalParameters_ShouldInitializeNonNull()
        {
            var ts = CreateMockRegistry();
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());

            var ctx = new EngineContext(ts, broker);

            Assert.IsNotNull(ctx.Timeseries);
            Assert.IsNotNull(ctx.Broker);
            Assert.IsNotNull(ctx.Market);
            Assert.IsNotNull(ctx.Events);
            Assert.IsNotNull(ctx.Fields);
            Assert.AreEqual(-1, ctx.CurrentIndex);
            Assert.AreEqual(DateTime.MinValue, ctx.CurrentTime);
        }

        [TestMethod]
        public void State_IndexAndTime_CanBeUpdated()
        {
            var ts = CreateMockRegistry();
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var ctx = new EngineContext(ts, broker);

            var now = DateTime.UtcNow;
            ctx.UpdateStep(5, now);

            Assert.AreEqual(5, ctx.CurrentIndex);
            Assert.AreEqual(now, ctx.CurrentTime);
        }

        private class SamplePlugin
        {
            public string Name { get; set; } = "Sample";
        }

        [TestMethod]
        public void Extensions_SetAndGet_WorksProperly()
        {
            var ts = CreateMockRegistry();
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var ctx = new EngineContext(ts, broker);

            Assert.IsNull(ctx.GetExtension<SamplePlugin>());

            var plugin = new SamplePlugin { Name = "TestPlugin" };
            ctx.SetExtension(plugin);

            var retrieved = ctx.GetExtension<SamplePlugin>();
            Assert.IsNotNull(retrieved);
            Assert.AreEqual("TestPlugin", retrieved.Name);
            Assert.AreSame(plugin, retrieved);
        }
    }
}
