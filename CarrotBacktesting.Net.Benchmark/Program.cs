// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Strategy;

Console.WriteLine("Hello, World!");

BenchmarkRunner.Run<BackTestingEngineBenchmark>();

[MemoryDiagnoser]
public class BackTestingEngineBenchmark
{

    public BackTestingEngineBenchmark()
    {
    }

    [Benchmark(Baseline = true)]
    public void a1()
    {
    }

    [Benchmark]
    public void a2()
    {
    }

    [Benchmark]
    public void a3()
    {
    }
}