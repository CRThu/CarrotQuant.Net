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
            var dates = new[] { new MarketTime(2025, 1, 1), new MarketTime(2025, 1, 2) };
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

        [TestMethod]
        public void Facades_AccountAndPosition_ForwardToBrokerAccurately()
        {
            var ts = CreateMockRegistry();
            var config = new BrokerConfig { InitialCash = 50_000.0, SettleDays = 0, FeeRate = 0.0, Slippage = 0.0 };
            var broker = new Broker(new SimulatedExchangeGateway(config), config);
            var ctx = new EngineContext(ts, broker);

            Assert.AreEqual(50_000.0, ctx.AvailableCash);
            Assert.AreEqual(50_000.0, ctx.TotalEquity);
            Assert.AreEqual(0m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(0m, ctx.GetAvailablePosition("sh.600000"));
            Assert.AreEqual(0.0, ctx.GetPositionCost("sh.600000"), "无持仓时成本价必须为 0.0");

            // 1. 第一批以 10.0 买入 100 股
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 10.0, 100m));
            Assert.AreEqual(100m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(10.0, ctx.GetPositionCost("sh.600000"), 1e-6, "第一批买入后持仓成本必须准确为 10.0");

            // 2. 第二批以 20.0 加仓 100 股 -> 加权成本 (100*10 + 100*20) / 200 = 15.0
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Buy, OrderType.Market, 20.0, 100m));
            Assert.AreEqual(200m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(15.0, ctx.GetPositionCost("sh.600000"), 1e-6, "加仓后加权平均持仓成本必须精确为 15.0");

            // 3. 部分平仓 50 股 -> 成本保持 15.0 不变
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 15.0, 50m));
            Assert.AreEqual(150m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(15.0, ctx.GetPositionCost("sh.600000"), 1e-6, "部分平仓后剩余持仓成本守恒保持 15.0");

            // 4. 全部清仓平仓 150 股 -> 成本归零
            broker.SubmitOrder(new OrderRequest("sh.600000", OrderDirection.Sell, OrderType.Market, 15.0, 150m));
            Assert.AreEqual(0m, ctx.GetPosition("sh.600000"));
            Assert.AreEqual(0.0, ctx.GetPositionCost("sh.600000"), 1e-6, "完全平仓后持仓成本必须准确归零");
        }

        [TestMethod]
        public void Facades_Metadata_SymbolsAndIndex_ForwardAccurately()
        {
            var dates = new[] { new MarketTime(2025, 1, 1) };
            var symbols = new[] { "sh.600000", "sz.000001", "bj.920001" };
            var meta = new DynamicMarketMetadata(symbols, dates);
            var ts = new MockTimeseriesRegistry(meta);
            var broker = new Broker(new SimulatedExchangeGateway(new BrokerConfig()), new BrokerConfig());
            var ctx = new EngineContext(ts, broker);

            Assert.AreEqual(3, ctx.SymbolCount);
            Assert.AreEqual(3, ctx.Symbols.Count);
            Assert.AreEqual("sh.600000", ctx.Symbols[0]);
            Assert.AreEqual("sz.000001", ctx.Symbols[1]);
            Assert.AreEqual("bj.920001", ctx.Symbols[2]);

            Assert.AreEqual(0, ctx.GetSymbolIndex("sh.600000"));
            Assert.AreEqual(1, ctx.GetSymbolIndex("sz.000001"));
            Assert.AreEqual(2, ctx.GetSymbolIndex("bj.920001"));
            Assert.AreEqual(-1, ctx.GetSymbolIndex("invalid_symbol"));
        }

        [TestMethod]
        public void Facades_TradeVerbs_BuySellClosePosition_ForwardToBrokerAccurately()
        {
            var ts = CreateMockRegistry();
            var config = new BrokerConfig { InitialCash = 100_000.0, SettleDays = 0, FeeRate = 0.0, Slippage = 0.0 };
            var broker = new Broker(new SimulatedExchangeGateway(config), config);
            var ctx = new EngineContext(ts, broker);

            // 1. context.Buy 动词（默认市价单）
            string buyId = ctx.Buy("sh.600000", 200m, 10.0, clientOrderId: "ctx_buy_01");
            Assert.AreEqual("ctx_buy_01", buyId);
            Assert.AreEqual(200m, ctx.GetPosition("sh.600000"));

            // 2. context.Sell 动词卖出 50 股
            string sellId = ctx.Sell("sh.600000", 50m, 10.0, clientOrderId: "ctx_sell_01");
            Assert.AreEqual("ctx_sell_01", sellId);
            Assert.AreEqual(150m, ctx.GetPosition("sh.600000"));

            // 3. context.ClosePosition 动词全额平仓剩余 150 股
            string closeId = ctx.ClosePosition("sh.600000", 10.0, clientOrderId: "ctx_close_01");
            Assert.AreEqual("ctx_close_01", closeId);
            Assert.AreEqual(0m, ctx.GetPosition("sh.600000"));

            // 4. 空仓调用 ClosePosition 返回 string.Empty
            Assert.AreEqual(string.Empty, ctx.ClosePosition("sh.600000"));
        }
    }
}
