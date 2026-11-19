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
        var dates = new List<MarketTime>
        {
            new MarketTime(2025, 1, 1),
            new MarketTime(2025, 1, 2),
            new MarketTime(2025, 1, 3)
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
    public async Task RunAndAnalyzeAsync_ExecutesAsynchronously_ProducesReport()
    {
        var (config, engine, _) = CreateTestEngine();
        using var session = new BacktestSession(config, engine);

        var report = await session.RunAndAnalyzeAsync(export: false);

        Assert.IsNotNull(report);
        Assert.AreSame(report, session.Report);
        Assert.IsNotNull(session.Result);
        Assert.AreEqual(EngineStatus.Completed, session.Engine.Status);
    }

    [TestMethod]
    public void Analyze_WithExportTrue_InvokesExporterPipeline()
    {
        var (config, engine, _) = CreateTestEngine();
        using var session = new BacktestSession(config, engine);

        session.Run();
        // export = true 会根据配置中的 Exporters 驱动导出（测试配置默认含 ConsoleExporter）
        var report = session.Analyze(export: true);

        Assert.IsNotNull(report);
        Assert.AreSame(report, session.Report);
    }

    [TestMethod]
    public void FromConfig_WithStrategy_CreatesValidSession()
    {
        var csvRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv");
        var sessionConfig = new SessionConfig
        {
            Engine = new EngineConfig
            {
                StartDate = new DateTime(2025, 1, 1),
                EndDate = new DateTime(2025, 1, 3)
            },
            Broker = new BrokerConfig { InitialCash = 100000 },
            Data = new DataConfig
            {
                Root = csvRoot,
                Timeseries = new List<TimeseriesConfig>
                {
                    new()
                    {
                        TableId = "ashare.kline.1d.raw.baostock",
                        Fields = new List<string> { "close", "volume" },
                        Cache = false
                    }
                }
            }
        };

        // 1. 传入单个 IStrategy
        var dummyStrategy = new DummyStrategy();
        using var session1 = BacktestSession.FromConfig(sessionConfig, dummyStrategy);
        Assert.IsNotNull(session1);
        Assert.IsNotNull(session1.Engine);

        // 2. 传入已有的 IStrategyPipeline
        var pipeline = StrategyPipeline.Sequential().Add(new DummyStrategy());
        using var session2 = BacktestSession.FromConfig(sessionConfig, pipeline);
        Assert.IsNotNull(session2);
        Assert.IsNotNull(session2.Engine);
    }

    [TestMethod]
    public void LoadAndRun_FromYamlConfigFile_EndToEnd()
    {
        var csvRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "test_data_root", "csv").Replace("\\", "/");
        string tempYaml = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "test_session_" + Guid.NewGuid().ToString("N") + ".yaml");

        try
        {
            System.IO.File.WriteAllText(tempYaml, $"""
            engine:
              mode: backtest
              start_date: "2025-01-02 00:00:00"
              end_date: "2025-01-06 00:00:00"
            broker:
              initial_cash: 200000.0
            data:
              root: "{csvRoot}"
              timeseries:
                - table_id: ashare.kline.1d.raw.baostock
                  fields:
                    - close
                    - volume
                  cache:
                    enabled: false
            analysis:
              analyzers:
                - type: TradeAnalyzer
              exporters:
                - type: ConsoleExporter
            """);

            var dummyStrategy = new DummyStrategy();
            using var session = BacktestSession.Load(tempYaml, dummyStrategy);
            Assert.IsNotNull(session);
            Assert.AreEqual(200000.0, session.Config.Broker.InitialCash);

            var report = session.RunAndAnalyze(export: false);
            Assert.IsNotNull(report);
            Assert.IsNotNull(session.Result);
            Assert.IsTrue(dummyStrategy.UpdateCount > 0);

            // 测试静态直接运行入口 BacktestSession.Run(configPath, export: false)
            var staticReport = BacktestSession.Run(tempYaml, export: false);
            Assert.IsNotNull(staticReport);
        }
        finally
        {
            if (System.IO.File.Exists(tempYaml))
            {
                System.IO.File.Delete(tempYaml);
            }
        }
    }

    [TestMethod]
    public void Dispose_CascadesToUnderlyingEngine()
    {
        var (config, engine, _) = CreateTestEngine();
        var session = new BacktestSession(config, engine);

        session.Dispose();
        // 验证重复释放幂等性
        session.Dispose();

        // 验证底层引擎已被释放：再次尝试运行必须抛出 ObjectDisposedException
        Assert.ThrowsException<ObjectDisposedException>(() => session.Run());
    }
}
