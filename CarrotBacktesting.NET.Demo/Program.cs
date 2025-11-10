using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.DataFeed;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Strategy;
using CarrotBacktesting.NET.Strategy.Examples;
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
                // 加载配置
                Console.WriteLine("Loading config...");
                string configPath = ".\\CarrotQuant.Data\\v3\\yaml\\env.yaml";
                //Console.WriteLine($"Config path: {configPath}");
                BacktestingSession session = new BacktestingSession(configPath);

                Console.WriteLine($"Project directory: {session.Config.Runtime.ProjectDir}");
                Console.WriteLine($"Data path: {session.Config.ResolvePath(session.Config.Data.RawPath)}");
                Console.WriteLine($"Thread count: {session.Config.Runtime.ThreadCount}");

                // 加载数据
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

                Console.WriteLine("\n--- Backtesting Start ---");

                // 实例化您的策略
                var strategy = new VolumeSignalStrategy();
                //var strategy = new PriceStrategy();

                // 运行信号回测引擎
                session.Run(strategy);

                // 打印最终的信号计数
                Console.WriteLine("\n--- Backtesting Results ---");
                var trades = session.Result!.Trades;
                Console.WriteLine($"Total signals generated: {trades.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nAn unhandled error occurred: {ex}");
            }
        }
    }
}
