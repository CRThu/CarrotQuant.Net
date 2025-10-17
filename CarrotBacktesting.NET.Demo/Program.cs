using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.DataFeed;
using CarrotBacktesting.NET.Utility;

namespace CarrotBacktesting.NET.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            // 加载配置文件
            EnvConfig config;
            try
            {
                string configPath = Path.Combine(PathHelper.RuntimeRoot, ".\\CarrotQuant.Data\\v3\\yaml\\env.yaml");
                Console.WriteLine($"Config path: {configPath}");
                config = EnvConfigLoader.Load(configPath);

                Console.WriteLine($"Data path: {config.Data.FullPath}");
                Console.WriteLine($"Thread count: {config.Runtime.ThreadCount}");
                Console.WriteLine($"Project directory: {config.Runtime.ProjectDir}");

                DataLoader dataLoader = new DataLoader();
                MarketStorage? ms = dataLoader.LoadData(config);

                if (ms.TradeDates.Any())
                {
                    Console.WriteLine($"时间范围: {ms.TradeDates.First():yyyy-MM-dd}  至  {ms.TradeDates.Last():yyyy-MM-dd}");
                }
                Console.WriteLine($"股票数量: {ms.Symbols.Length}");
                Console.WriteLine($"交易日数量: {ms.TradeDates.Count}");
                Console.WriteLine($"Frame数量: {ms.GetFramesEnumerator().Sum(frame => frame.PrimaryData.Count(stock => stock.HasValue)):N0}");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load config: {ex}");
                return;
            }
        }
    }
}
