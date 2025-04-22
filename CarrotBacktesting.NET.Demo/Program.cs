using CarrotBacktesting.NET.DataFeed;
using CarrotBacktesting.NET.Utility;
using System.Xml.Serialization;

namespace CarrotBacktesting.NET.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            // 加载配置文件
            try
            {
                string configPath = Path.Combine(PathHelper.RuntimeRoot, ".\\CarrotQuant.Data\\v3\\xml\\env.xml");
                Console.WriteLine($"Config path: {configPath}");
                var config = EnvConfigLoader.Load(configPath);

                Console.WriteLine($"Data path: {config.Data.FullPath}");
                Console.WriteLine($"Thread count: {config.Runtime.ThreadCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load config: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}
