using CarrotBacktesting.NET.DataFeed;
using CarrotBacktesting.NET.DataFeed.Model;
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
            EnvConfig config;
            try
            {
                string configPath = Path.Combine(PathHelper.RuntimeRoot, ".\\CarrotQuant.Data\\v3\\xml\\env.xml");
                Console.WriteLine($"Config path: {configPath}");
                config = EnvConfigLoader.Load(configPath);

                Console.WriteLine($"Data path: {config.Data.FullPath}");
                Console.WriteLine($"Thread count: {config.Runtime.ThreadCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load config: {ex.Message}");
                return;
            }

            // 扫描目录
            var info = FileScanner.GetFiles(config.Data.FullPath);
            Console.WriteLine($"Found {info.Count} files.");
            //foreach (var kvp in info)
            //{
            //    Console.WriteLine($"Key: {kvp.Key}, Path: {kvp.Value}");
            //}
        }
    }
}
