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
    public static string[] a = new string[] { "a", "bb", "ccc", "dddd" };
    public static string[] b = new string[] { "1", "22", "333", "4444" };
    public static string[] c = new string[] { "!", "@@", "###", "$$$$" };

    [Benchmark(Baseline = true)]
    public void a1()
    {
        ArrayMisc.ArrayCombine(a, b, c, a, b, c);
    }

    [Benchmark]
    public void a2()
    {
        ArrayMisc.ArrayCombine2(a, b, c, a, b, c);
    }
}