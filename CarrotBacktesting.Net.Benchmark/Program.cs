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
    public void a1()
    {
        (double x, double v) x = (0, 0);
        for (int i = 0; i < 10000; i++)
        {
            x.x += i;
            x.v += i * i;
        }
    }

    [Benchmark]
    public void a2()
    {
        xv x = new() { x = 0, v = 0 };
        for (int i = 0; i < 10000; i++)
        {
            x.x += i;
            x.v += i * i;
        }
    }
    struct xv
    {
        public double x;
        public double v;
    }
}