using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Strategy;
using CarrotBackTesting.Net.UnitTest.Common;
using System.Diagnostics;
using static CarrotBacktesting.Net.Common.Enums;


namespace CarrotBacktesting.Net.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            string filename = "options.daily.no-map.json";
            string optionsJsonPath = Path.Combine(UnitTestDirectory.AsBsJsonTestDataDir, filename);
            BackTestingEngine engine = BackTestingEngine.Create(new DebugStrategy(), optionsJsonPath);
            Stopwatch stopwatch = new();
            stopwatch.Start();

            engine.Run();

            stopwatch.Stop();
            Console.WriteLine($"回测已完成, 共测试{engine.Simulator.ElapsedTickCount}帧, 耗时{stopwatch.ElapsedMilliseconds / 1000.0}秒, 回测速度{(double)engine.Simulator.ElapsedTickCount / stopwatch.ElapsedMilliseconds * 1000:F3}帧/秒.");
        }
    }
}