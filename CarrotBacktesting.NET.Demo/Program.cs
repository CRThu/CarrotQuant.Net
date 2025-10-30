using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.DataFeed;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Engine.Strategy;
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
                EnvConfig config = EnvConfigLoader.Load(configPath);

                Console.WriteLine($"Data path: {config.Data.RawPath}");
                Console.WriteLine($"Thread count: {config.Runtime.ThreadCount}");
                Console.WriteLine($"Project directory: {config.Runtime.ProjectDir}");

                // --- 2. 加载数据 ---
                Console.WriteLine("\nLoading data...");
                DataLoader dataLoader = new DataLoader();
                IDataStorage? ds = dataLoader.LoadData(config);

                if (ds.TradeDates.Any())
                {
                    Console.WriteLine($"时间范围: {ds.TradeDates.First():yyyy-MM-dd}  至  {ds.TradeDates.Last():yyyy-MM-dd}");
                }
                Console.WriteLine($"股票数量: {ds.Symbols.Count}");
                Console.WriteLine($"交易日数量: {ds.TradeDates.Count}");

                if (ds is HistoryStorage)
                {
                    Console.WriteLine("数据存储类型: HistoryStorage (按股票时间序列存储)");
                }
                else if (ds is MarketStorage ms)
                {
                    Console.WriteLine($"Frame数量: {ms.GetFramesEnumerator().Sum(frame => frame.PrimaryData.Count(stock => stock.HasValue)):N0}");
                }

                // --- 3. 运行信号回测引擎 ---
                Console.WriteLine("\n--- Backtesting Start ---");

                // a. 实例化您的策略
                var strategy = new VolumeSignalStrategy();

                // b. 用加载的数据和策略来实例化引擎
                //    引擎内部会自动适配 TimeSeries 和 MarketSnapshot 两种数据模式
                var engine = new BacktestingEngine(ds, strategy, config);

                // c. 运行引擎并获取信号结果
                //    这个 Signal 结构就是您提到的 SignalInfo
                var signals = engine.Run();

                // d. 打印最终的信号计数
                Console.WriteLine("\n--- Backtesting Results ---");
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
