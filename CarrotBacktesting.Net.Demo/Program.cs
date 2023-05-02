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
            Stopwatch loadStopwatch = new();
            Stopwatch runStopwatch = new();


            // string filename = "options.daily.no-adjust.json";
            // string filename = "options.daily.post-adjust.json";
            string filename = "options.5min.post-adjust.json";
            string optionsJsonPath = Path.Combine(UnitTestDirectory.AsBsJsonTestDataDir, filename);
            loadStopwatch.Start();
            BackTestingEngine engine = BackTestingEngine.Create(new DebugStrategy(), optionsJsonPath);
            loadStopwatch.Stop();
            runStopwatch.Start();
            engine.Run();
            runStopwatch.Stop();

            Console.WriteLine($"回测已完成, 共测试 {engine.Simulator.ElapsedTickCount} 帧");
            Console.WriteLine($"加载耗时: {loadStopwatch.ElapsedMilliseconds / 1000.0} 秒, 加载速度: {(double)engine.Simulator.ElapsedTickCount / loadStopwatch.ElapsedMilliseconds * 1000:F3} 帧/秒.");
            Console.WriteLine($"回测耗时: {runStopwatch.ElapsedMilliseconds / 1000.0} 秒, 回测速度: {(double)engine.Simulator.ElapsedTickCount / runStopwatch.ElapsedMilliseconds * 1000:F3} 帧/秒.");
        }
    }
}