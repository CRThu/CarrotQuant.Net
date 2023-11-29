using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Storage;
using CarrotBacktesting.Net.Strategy;
using CarrotBackTesting.Net.UnitTest.Common;
using MemoryPack;
using MemoryPack.Compression;
using Newtonsoft.Json.Linq;
using System;
using System.Data;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace CarrotBacktesting.Net.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            CsvReader csvReader = new(new SimulationOptions {
                Fields = new string[] { "交易日期", "开盘价", "最高价", "最低价", "收盘价", "成交量", "成交额", "交易状态" },
                Mapper = new ShareFrameMapper() {
                    MapDict = new Dictionary<string, string> {
                        ["交易日期"] = "Time",
                        ["开盘价"] = "Open",
                        ["最高价"] = "High",
                        ["最低价"] = "Low",
                        ["收盘价"] = "Close",
                        ["成交量"] = "Volume",
                        ["成交额"] = "Amount",
                        ["交易状态"] = "Status"
                    },
                    BooleanTrueString = new HashSet<string> {
                        "正常交易",
                        "是"
                    },
                    BooleanFalseString = new HashSet<string> {
                        "停牌",
                        "否"
                    }
                }
            });
            csvReader.Options.Mapper.UpdateGlobalBoolString();
            Stopwatch loadStopwatch = new();
            loadStopwatch.Start();
            var v = csvReader.Load("D:\\Projects\\CarrotQuant.Data\\testbench\\bs.sz.000422.csv", "sz.000422");
            loadStopwatch.Stop();

            Console.WriteLine($"回测已完成, 共测试 {v.Count()} Ticks, "
                + $"{v.Count()} KLines");
            double loadTicksSpeed = (double)v.Count() / loadStopwatch.ElapsedMilliseconds * 1000;
            double loadKlinesSpeed = (double)v.Count() / loadStopwatch.ElapsedMilliseconds * 1000;
            Console.WriteLine($"加载耗时: {loadStopwatch.ElapsedMilliseconds / 1000.0} Sec, "
                + $"加载速度: {loadTicksSpeed:F3} Ticks/Sec, "
                + $"{loadKlinesSpeed:F3} KLines/Sec");

            /*
            Console.WriteLine("Hello, World!");
            Stopwatch loadStopwatch = new();
            Stopwatch runStopwatch = new();


            // string dataSet = "testdata/daily.no-adjust";
            // string dataSet = "testdata/daily.post-adjust";
            string dataSet = "testdata/5min.post-adjust";
            string optionFile = "options.json";
            string dataDir = UnitTestDirectory.DataRootDirectory;
            loadStopwatch.Start();
            BackTestingEngine engine = BackTestingEngine.Create(new DebugStrategy(), dataDir, dataSet, optionFile);
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
            */
        }
    }
}