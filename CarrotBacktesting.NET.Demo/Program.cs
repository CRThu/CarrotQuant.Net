using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.DataFeed;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Strategy;
using CarrotBacktesting.NET.Utility;

namespace CarrotBacktesting.NET.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            try
            {
                // --- 1. 加载配置 ---
                Console.WriteLine("Loading config...");
                string configPath = Path.Combine(PathHelper.RuntimeRoot, ".\\CarrotQuant.Data\\v3\\yaml\\env.yaml");
                Console.WriteLine($"Config path: {configPath}");
                BacktestingSession session = new BacktestingSession(configPath);

                Console.WriteLine($"Data path: {session.Config.Data.RawPath}");
                Console.WriteLine($"Thread count: {session.Config.Runtime.ThreadCount}");
                Console.WriteLine($"Project directory: {session.Config.Runtime.ProjectDir}");

                // --- 2. 加载数据 ---
                Console.WriteLine("\nLoading data...");
                session.Load();

                if (session.Data!.TradeDates.Any())
                {
                    Console.WriteLine($"时间范围: {session.Data!.TradeDates.First():yyyy-MM-dd}  至  {session.Data!.TradeDates.Last():yyyy-MM-dd}");
                }
                Console.WriteLine($"股票数量: {session.Data!.Symbols.Count}");
                Console.WriteLine($"交易日数量: {session.Data!.TradeDates.Count}");

                if (session.Data is HistoryStorage)
                {
                    Console.WriteLine("数据存储类型: HistoryStorage (按股票时间序列存储)");
                }
                else if (session.Data is MarketStorage ms)
                {
                    Console.WriteLine($"Frame数量: {ms.GetFramesEnumerator().Sum(frame => frame.PrimaryData.Count(stock => stock.HasValue)):N0}");
                }

                // --- 3. 运行信号回测引擎 ---
                Console.WriteLine("\n--- Backtesting Start ---");

                // a. 实例化您的策略
                var strategy = new VolumeSignalStrategy();
                session.RunSignal(strategy);

                // d. 打印最终的信号计数
                Console.WriteLine("\n--- Backtesting Results ---");
                var signals = session.Result!.SignalsResult.GetSignals().ToList();
                Console.WriteLine($"Total signals (SignalInfo) generated: {signals.Count}");

                // (可选) 打印一些信号示例
                if (signals.Count != 0)
                {
                    Console.WriteLine("First 10 signals:");
                    foreach (var signal in signals.Take(10))
                    {
                        Console.WriteLine($"  - Stock: {signal.StockCode}, Date: {signal.Date:yyyy-MM-dd}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nAn unhandled error occurred: {ex}");
            }
        }
    }
}
