﻿using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Strategy;
using CarrotBackTesting.Net.UnitTest.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Demo
{
    public static class EngineTest
    {
        public static void Run()
        {
            
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
            
        }
    }
}
