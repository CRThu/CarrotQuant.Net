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
        // string dataSet = "testdata/daily.no-adjust";
        // string dataSet = "testdata/daily.post-adjust";
        string dataSet = "testdata/5min.post-adjust";
        string optionFile = "options.json";
        string dataDir = UnitTestDirectory.BenchmarkDataRootDirectory;
        engine = BackTestingEngine.Create(new DebugStrategy(), dataDir, dataSet, optionFile);
    }

    [Benchmark]
    public void RunTick()
    {
        engine.RunTickForBenchmark();
    }
}