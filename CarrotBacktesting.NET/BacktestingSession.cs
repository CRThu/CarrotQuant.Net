using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.DataFeed;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Strategy;
using CarrotBacktesting.NET.Utility;
using CarrotBacktesting.NET.Utility.Serialization;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET
{
    /// <summary>
    /// 管理一次完整的的回测会话，封装了从配置加载到结果产出的整个流程。
    /// </summary>
    public class BacktestingSession
    {
        /// <summary>
        /// 会话使用的环境配置
        /// </summary>
        public EnvConfig Config { get; private set; }

        /// <summary>
        /// 加载的市场数据
        /// </summary>
        public IDataStorage? Data { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public BacktestingEngine? Engine { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public BacktestingResult? Result { get; private set; }

        /// <summary>
        /// 构造一个新的回测会话
        /// </summary>
        /// <param name="configPath">env.yaml 配置文件路径</param>
        private BacktestingSession(string configPath)
        {
            string? foundPath = PathHelper.FindPathUpwards(configPath);

            if (string.IsNullOrWhiteSpace(foundPath))
            {
                throw new FileNotFoundException($"无法在当前目录或上级目录中找到配置文件: {configPath}");
            }

            Console.WriteLine($"成功定位配置文件: {foundPath}");

            Config = EnvConfigLoader.Load(foundPath);

            if (Config == null)
                throw new InvalidOperationException("Config is null");

            PrintConfigSummary();
        }

        /// <summary>
        /// 使用静态工厂方法来创建会话实例
        /// </summary>
        public static BacktestingSession Create(string configPath = "env.yaml")
        {
            return new BacktestingSession(configPath);
        }

        /// <summary>
        /// 加载数据。数据加载完成后会存储在 Data 属性中。
        /// </summary>
        /// <returns>返回会话实例本身。</returns>
        public BacktestingSession LoadData()
        {
            var dataLoader = new DataLoader();
            Data = dataLoader.LoadData(Config);
            if (Data == null)
                throw new InvalidOperationException("Data loading failed.");

            PrintDataSummary();

            return this;
        }

        /// <summary>
        /// 使用指定的策略运行回测。
        /// </summary>
        /// <param name="strategy">要运行的信号策略</param>
        /// <returns>返回会话实例本身</returns>
        public BacktestingSession Run(IStrategy strategy)
        {
            if (Data == null)
                throw new InvalidOperationException("Data is null");

            Engine = new BacktestingEngine(Data!, Config);
            Result = Engine.Run(strategy);

            PrintResultSummary();

            return this;
        }

        /// <summary>
        /// 对上一次运行的结果进行分析。
        /// </summary>
        /// <returns>返回会话实例本身</returns>
        public BacktestingSession Analyze()
        {
            if (Result is null || Data is null)
            {
                Console.WriteLine("没有可供分析的结果或数据。");
                return this;
            }

            var runner = new AnalysisRunner(Config, Result, Data);
            runner.Run();

            PrintAnalysisSummary();

            return this;
        }

        /// <summary>
        /// 将上一次运行的结果保存到文件。
        /// </summary>
        /// <returns>返回会话实例本身，以支持链式调用。</returns>
        public BacktestingSession SaveResult()
        {
            if (Result == null)
            {
                Console.WriteLine("没有可供保存的回测结果。");
                return this;
            }
            if (Result.Trades.Count == 0)
            {
                Console.WriteLine("没有产生任何交易。");
                return this;
            }

            if (!string.IsNullOrWhiteSpace(Config.Out.Signal))
            {
                try
                {
                    string fileName = Config.ResolvePath(Config.Out.Signal);
                    Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
                    JsonSerializationHelper.SerializeToFile(Result.Trades, fileName);
                    Console.WriteLine($"交易列表已自动保存到: {fileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 保存交易文件失败: {ex.Message}");
                }
            }

            return this;
        }


        #region Summary Printers (摘要打印方法)

        /// <summary>
        /// 打印配置文件的核心信息摘要。
        /// </summary>
        private void PrintConfigSummary()
        {
            AnsiConsole.MarkupLine("[bold underline yellow]配置摘要:[/]");
            AnsiConsole.MarkupLine($"  - 项目目录: [cyan]{Config.Runtime.ProjectDir}[/]");
            AnsiConsole.MarkupLine($"  - 数据路径: [cyan]{Config.ResolvePath(Config.Data.RawPath)}[/]");
            AnsiConsole.MarkupLine($"  - 线程数量: [cyan]{Config.Runtime.ThreadCount}[/]");
            AnsiConsole.WriteLine();
        }

        /// <summary>
        /// 打印加载完成的数据的核心信息摘要。
        /// </summary>
        private void PrintDataSummary()
        {
            if (Data is null) return;

            AnsiConsole.MarkupLine("[bold underline yellow]数据摘要:[/]");

            AnsiConsole.MarkupLine($"  - 股票数量: [cyan]{Data.Symbols.Count:N0}[/]");
            AnsiConsole.MarkupLine($"  - 全局交易日: [cyan]{Data.TradeDates.Count:N0}[/]");

            if (Data.TradeDates.Any())
            {
                AnsiConsole.MarkupLine($"  - 时间范围: [cyan]{Data.TradeDates.First():yyyy-MM-dd}[/] 至 [cyan]{Data.TradeDates.Last():yyyy-MM-dd}[/]");
            }

            if (Data is HistoryStorage hs)
            {
                AnsiConsole.MarkupLine("  - 存储类型: [green]HistoryStorage (纵向)[/]");
                long totalFrames = hs.StockHistories.Values.Sum(h => (long)h.Data.Count);
                AnsiConsole.MarkupLine($"  - 总数据点 (Frames): [cyan]{totalFrames:N0}[/]");
            }
            else if (Data is MarketStorage ms)
            {
                AnsiConsole.MarkupLine("  - 存储类型: [magenta]MarketStorage (横向)[/]");
                long totalFrames = ms.MarketFrames.Values.Sum(frame => (long)frame.PrimaryData.Count(stock => stock.HasValue));
                AnsiConsole.MarkupLine($"  - 总数据点 (Frames): [cyan]{totalFrames:N0}[/]");
            }
            AnsiConsole.WriteLine();
        }
        /// <summary>
        /// 打印回测运行结果的摘要。
        /// </summary>
        private void PrintResultSummary()
        {
            if (Result is null) return;
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold underline yellow]回测结果摘要[/]");

            int closedCount = Result.Trades.Count(t => t.IsClosed);
            int openCount = Result.Trades.Count - closedCount;

            AnsiConsole.MarkupLine($"  - [bold]总计产生 {Result.Trades.Count:N0} 笔交易记录[/]");
            AnsiConsole.MarkupLine($"    - 已平仓交易: [green]{closedCount:N0}[/]");
            AnsiConsole.MarkupLine($"    - 未平仓交易/信号: [yellow]{openCount:N0}[/]");
        }

        /// <summary>
        /// 打印分析完成的摘要信息。
        /// </summary>
        private void PrintAnalysisSummary()
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold underline yellow]分析摘要[/]");
            AnsiConsole.MarkupLine("  - [green]分析流程已执行完成。[/]");
            // 未来这里可以扩展，打印更详细的分析结果摘要，
            // 例如从 AnalysisContext 中提取关键指标。
        }

        #endregion
    }
}
