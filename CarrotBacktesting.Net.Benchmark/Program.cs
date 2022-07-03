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
    public bool a = false;
    public double b = 0;
    public (bool a, double b) ab;
    public (bool a, double b) test;

    public (bool a, double b) TestMethodUseTuple()
    {
        ab.a = !ab.a;
        ab.b++;
        return ab;
    }

    public void TestMethodUseOut(out bool x, out double y)
    {
        a = !a;
        b++;
        x = a;
        y = b;
    }

    public BackTestingEngineBenchmark()
    {
    }

    [Benchmark(Baseline = true)]
    public void UseTuple()
    {
        test = TestMethodUseTuple();
    }

    [Benchmark]
    public void UseOut()
    {
        TestMethodUseOut(out bool a, out double b);
    }
}