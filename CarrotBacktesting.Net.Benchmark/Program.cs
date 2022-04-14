// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Strategy;

Console.WriteLine("Hello, World!");

BenchmarkRunner.Run<BackTestingEngineBenchmark>();

[MemoryDiagnoser]
public class BackTestingEngineBenchmark
{
    [Benchmark(Baseline = true)]
    public void StandardEngine()
    {
        var options = new BackTestingSimulationOptions()
        {
            IsSqliteDataFeed = true,
            SqliteDatabasePath = @"D:\Projects\CarrotQuant\Stock\Data\StockData_1d_baostock.db",
            SimulationStartDateTime = new DateTime(2021, 6, 1),
            SimulationEndDateTime = new DateTime(2021, 11, 1),
            SimulationDuration = new TimeSpan(0, 0, 1),
            ShareName = "sz.000422",
        };

        IEngine engine = new BackTestingEngine(new BasicStrategy(), options);
        engine.Run();
    }

    [Benchmark]
    public void StandardEngineWithImportingStringData()
    {
        var options = new BackTestingSimulationOptions()
        {
            IsSqliteDataFeed = true,
            SqliteDatabasePath = @"D:\Projects\CarrotQuant\Stock\Data\StockData_1d_baostock.db",
            SimulationStartDateTime = new DateTime(2021, 6, 1),
            SimulationEndDateTime = new DateTime(2021, 11, 1),
            SimulationDuration = new TimeSpan(0, 0, 1),
            ShareName = "sz.000422",
            AdditionalStringColumnNames = new string[] { "是否ST" },
        };

        IEngine engine = new BackTestingEngine(new BasicStrategy(), options);
        engine.Run();
    }

    [Benchmark]
    public void StandardEngineWithImportingFloatData()
    {
        var options = new BackTestingSimulationOptions()
        {
            IsSqliteDataFeed = true,
            SqliteDatabasePath = @"D:\Projects\CarrotQuant\Stock\Data\StockData_1d_baostock.db",
            SimulationStartDateTime = new DateTime(2021, 6, 1),
            SimulationEndDateTime = new DateTime(2021, 11, 1),
            SimulationDuration = new TimeSpan(0, 0, 1),
            IsEnableShareStatusFlag = true,
            AdditionalDataColumnNames = new string[] { "滚动市盈率" }
        };

        IEngine engine = new BackTestingEngine(new BasicStrategy(), options);
        engine.Run();
    }
}