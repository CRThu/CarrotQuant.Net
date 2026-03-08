using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.DataFeed;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Strategy;
using CarrotBacktesting.NET.Examples;
using CarrotBacktesting.NET.Utility;
using CarrotBacktesting.NET.Utility.ScottPlot;
using ScottPlot;
using Spectre.Console;

namespace CarrotBacktesting.NET.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            try
            {
                string configPath = ".\\CarrotQuant.Data\\v3\\yaml\\env.yaml";
                BacktestingSession.Create(configPath)
                    .LoadData()
                    .Run(new MarketTrendStrategy())
                    //.Run(new MarketHeatStrategy())
                    //.Run(new VolumeSignalStrategy())
                    //.Run(new PriceStrategy())
                    .Analyze();
            }
            catch (Exception ex)
            {
                // AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
                Console.WriteLine($"\nAn unhandled error occurred: {ex}");
            }
        }
    }
}
