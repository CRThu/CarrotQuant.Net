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

                Console.WriteLine($"Data path: {config.Data.RawPath}");
                Console.WriteLine($"Thread count: {config.Runtime.ThreadCount}");
                Console.WriteLine($"Project directory: {config.Runtime.ProjectDir}");

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

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load config: {ex}");
                return;
            }
        }
    }
}
