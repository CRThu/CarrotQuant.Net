using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Storage;
using CarrotBacktesting.Net.Strategy;
using CarrotBackTesting.Net.UnitTest.Common;
using System.Data;
using System.Diagnostics;


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

            int klinesCount = engine.Simulator.KLineCount;
            int ticksCount = engine.Simulator.TickCount;
            double loadTicksSpeed = (double)ticksCount / loadStopwatch.ElapsedMilliseconds * 1000;
            double loadKlinesSpeed = (double)klinesCount / loadStopwatch.ElapsedMilliseconds * 1000;
            double runTicksSpeed = (double)ticksCount / runStopwatch.ElapsedMilliseconds * 1000;
            double runKlinesSpeed = (double)klinesCount / runStopwatch.ElapsedMilliseconds * 1000;

            Console.WriteLine($"回测已完成, 共测试 {ticksCount} Ticks, "
                + $"{klinesCount} KLines");
            Console.WriteLine($"加载耗时: {loadStopwatch.ElapsedMilliseconds / 1000.0} Sec, "
                + $"加载速度: {loadTicksSpeed:F3} Ticks/Sec, "
                + $"{loadKlinesSpeed:F3} KLines/Sec");
            Console.WriteLine($"回测耗时: {runStopwatch.ElapsedMilliseconds / 1000.0} Sec, "
                + $"回测速度: {runTicksSpeed:F3} Ticks/Sec, "
                + $"{runKlinesSpeed:F3} KLines/Sec");

        }
    }
}