// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;
using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Storage;
using CarrotBacktesting.Net.Strategy;
using CarrotBackTesting.Net.UnitTest.Common;
using Microsoft.Extensions.Options;
using System.Data;

Console.WriteLine("Hello, World!");

BenchmarkRunner.Run<BackTestingEngineBenchmark>();

[MemoryDiagnoser]
public class BackTestingEngineBenchmark
{
    SimulationOptions options;
    BackTestingEngine engine;

    public BackTestingEngineBenchmark()
    {
        string filename = "simulationoptions.baostock.sqlite.daily.json";
        string optionJsonPath = Path.Combine(UnitTestDirectory.BenchmarkJsonDirectory, filename);
        Console.WriteLine($"optionJsonPath:{optionJsonPath}");
        options = SimulationOptions.CreateFromJson(optionJsonPath);
        engine = BackTestingEngineFactory.Create(new EmptyStrategy(), options);
    }

    [Benchmark]
    public void RunTick()
    {
        engine.RunTickForBenchmark();
    }
}