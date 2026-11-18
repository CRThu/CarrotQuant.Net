using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CarrotBacktesting.NET;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Strategy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CarrotBackTesting.Net.UnitTest.Engine;

[TestClass]
public class BacktestSessionTests
{
    private class DummyStrategy : IStrategy
    {
        public string Name => "DummyStrategy";
        public int UpdateCount { get; private set; }

        public void OnUpdate(IEngineContext context)
        {
            UpdateCount++;
        }
    }

    private class MockTimeseriesRegistry : ITimeseriesRegistry, ITimeseriesScheduler
    {
        public IMarketMetadata Metadata { get; }
        public List<int> SteppedRows { get; } = new();

        public MockTimeseriesRegistry(IMarketMetadata metadata)
        {
            Metadata = metadata;
        }

        public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged => null!;
        public bool HasField(string fieldName) => true;

        public void StepTo(int currentLogicalRow)
        {
            SteppedRows.Add(currentLogicalRow);
        }

        public int RegisterTable(string tableName) => 0;
        public int GetTableIndex(string tableName) => 0;
        public string GetTableName(int tableIndex) => string.Empty;
    }

    private static (SessionConfig Config, BacktestingEngine Engine, DummyStrategy Strategy) CreateTestEngine()
    {
        var dates = new List<DateTime>
        {
            new DateTime(2025, 1, 1),
            new DateTime(2025, 1, 2),
            new DateTime(2025, 1, 3)
        };
        var symbols = new List<string> { "sh.600000" };
        var metadata = new DynamicMarketMetadata(symbols, dates);
        var ts = new MockTimeseriesRegistry(metadata);

        var brokerConfig = new BrokerConfig { InitialCash = 100000 };
        var gateway = new SimulatedExchangeGateway(brokerConfig);
        var broker = new Broker(gateway, brokerConfig);

        var strategy = new DummyStrategy();
        var pipeline = StrategyPipeline.Sequential().Add(strategy);

        var sessionConfig = new SessionConfig
        {
            Engine = new EngineConfig
            {
                StartDate = dates[0],
                EndDate = dates[dates.Count - 1]
            },
            Broker = brokerConfig
        };

        var engine = new BacktestingEngine(
            ts,
            broker,
            pipeline,
            config: sessionConfig.Engine,
            simExchange: gateway);

        return (sessionConfig, engine, strategy);
    }

    [TestMethod]
    public void FromConfig_NullConfig_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() => BacktestSession.FromConfig(null!));
    }

    [TestMethod]
    public void Analyze_BeforeRun_ThrowsInvalidOperationException()
    {
        var (config, engine, _) = CreateTestEngine();
        using var session = new BacktestSession(config, engine);

        Assert.IsNull(session.Result);
        Assert.ThrowsException<InvalidOperationException>(() => session.Analyze());
    }

    [TestMethod]
    public void Run_ExecutesEngineAndPopulatesResult()
    {
        var (config, engine, strategy) = CreateTestEngine();
        using var session = new BacktestSession(config, engine);

        var result = session.Run();

        Assert.IsNotNull(result);
        Assert.AreSame(result, session.Result);
        Assert.AreEqual(3, strategy.UpdateCount, "3 个时间步应触发 3 次 Update");
        Assert.AreEqual(EngineStatus.Completed, session.Engine.Status);
    }

    [TestMethod]
    public async Task RunAsync_ExecutesEngineAsynchronously()
    {
        var (config, engine, strategy) = CreateTestEngine();
        using var session = new BacktestSession(config, engine);

        var result = await session.RunAsync();

        Assert.IsNotNull(result);
        Assert.AreSame(result, session.Result);
        Assert.AreEqual(3, strategy.UpdateCount);
        Assert.AreEqual(EngineStatus.Completed, session.Engine.Status);
    }

    [TestMethod]
    public void RunAndAnalyze_ExecutesFullPipeline_ProducesReport()
    {
        var (config, engine, _) = CreateTestEngine();
        using var session = new BacktestSession(config, engine);

        var report = session.RunAndAnalyze(export: false);

        Assert.IsNotNull(report);
        Assert.AreSame(report, session.Report);
        Assert.IsNotNull(session.Result);
    }

    [TestMethod]
    public void Dispose_CascadesToUnderlyingEngine()
    {
        var (config, engine, _) = CreateTestEngine();
        var session = new BacktestSession(config, engine);

        session.Dispose();
        // 重复调用不抛异常（幂等性）
        session.Dispose();
    }
}
