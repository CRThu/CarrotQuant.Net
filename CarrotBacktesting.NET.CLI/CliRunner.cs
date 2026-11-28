using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using CarrotBacktesting.NET.Analysis.Exporters;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.CLI;

/// <summary>
/// CLI 命令行执行器内核，收敛参数解析、配置覆盖与回测会话调度。
/// </summary>
public static class CliRunner
{
    public const int ExitSuccess = 0;
    public const int ExitInvalidArgs = 1;
    public const int ExitExecutionError = 2;

    /// <summary>
    /// 执行命令行入口逻辑。
    /// </summary>
    /// <param name="args">终端输入参数列表。</param>
    /// <param name="outWriter">标准输出写入器（为 null 时使用 Console.Out）。</param>
    /// <param name="errWriter">错误输出写入器（为 null 时使用 Console.Error）。</param>
    /// <returns>进程退出码：0 成功，1 参数无效/文件缺失，2 运行时异常。</returns>
    public static int Run(string[] args, TextWriter? outWriter = null, TextWriter? errWriter = null)
    {
        var stdout = outWriter ?? Console.Out;
        var stderr = errWriter ?? Console.Error;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage(stdout);
            return ExitSuccess;
        }

        // 兼容 "view" 子命令启动轻量交互式看板
        if (string.Equals(args[0], "view", StringComparison.OrdinalIgnoreCase))
        {
            return RunView(args, stdout, stderr);
        }

        int index = 0;

        // 兼容显式 "run" 子命令或直接输入配置文件路径
        if (string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
        {
            index++;
            if (index >= args.Length || args[index].StartsWith('-'))
            {
                stderr.WriteLine("[错误] 缺少回测配置文件路径。");
                PrintUsage(stderr);
                return ExitInvalidArgs;
            }
        }

        string configPath = args[index++];
        if (!File.Exists(configPath))
        {
            stderr.WriteLine($"[错误] 指定的配置文件不存在: {configPath}");
            return ExitInvalidArgs;
        }

        // 解析可选的覆盖参数
        MarketTime? overrideStartDate = null;
        MarketTime? overrideEndDate = null;
        bool noExport = false;

        while (index < args.Length)
        {
            string current = args[index++];
            switch (current)
            {
                case "--start-date":
                case "-s":
                    if (index >= args.Length || !MarketTime.TryParse(args[index++], null, out var sDate))
                    {
                        stderr.WriteLine("[错误] --start-date 参数缺少有效日期值 (格式如: 2024-01-01)。");
                        return ExitInvalidArgs;
                    }
                    overrideStartDate = sDate;
                    break;

                case "--end-date":
                case "-e":
                    if (index >= args.Length || !MarketTime.TryParse(args[index++], null, out var eDate))
                    {
                        stderr.WriteLine("[错误] --end-date 参数缺少有效日期值 (格式如: 2024-12-31)。");
                        return ExitInvalidArgs;
                    }
                    overrideEndDate = eDate;
                    break;

                case "--no-export":
                    noExport = true;
                    break;

                default:
                    stderr.WriteLine($"[错误] 未知参数: {current}");
                    PrintUsage(stderr);
                    return ExitInvalidArgs;
            }
        }

        try
        {
            stdout.WriteLine($"[CarrotQuant] 正在装配回测会话: {Path.GetFullPath(configPath)}");

            // 加载配置
            var config = SessionConfig.Load(configPath);

            // 应用命令行动态覆盖参数
            if (overrideStartDate.HasValue)
            {
                config.Engine.StartDate = overrideStartDate.Value;
                stdout.WriteLine($"[CarrotQuant] 覆盖起始日期: {overrideStartDate.Value:yyyy-MM-dd}");
            }
            if (overrideEndDate.HasValue)
            {
                config.Engine.EndDate = overrideEndDate.Value;
                stdout.WriteLine($"[CarrotQuant] 覆盖结束日期: {overrideEndDate.Value:yyyy-MM-dd}");
            }

            // 装配并执行回测与绩效分析
            using var session = BacktestSession.FromConfig(config);
            var report = session.RunAndAnalyze(export: !noExport);

            stdout.WriteLine("[CarrotQuant] 回测执行与分析完成。");
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"[CarrotQuant 异常中止] {ex.Message}");
            return ExitExecutionError;
        }
    }

    private static int RunView(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < 2 || args[1].StartsWith('-'))
        {
            stderr.WriteLine("[错误] 缺少报告文件路径 (report.json)。");
            PrintUsage(stderr);
            return ExitInvalidArgs;
        }

        string reportPath = args[1];
        if (!File.Exists(reportPath))
        {
            stderr.WriteLine($"[错误] 指定的报告文件不存在: {reportPath}");
            return ExitInvalidArgs;
        }

        string? dataRoot = null;
        string tableId = "daily";
        int port = 0;
        bool openBrowser = true;

        int i = 2;
        while (i < args.Length)
        {
            string arg = args[i++];
            switch (arg)
            {
                case "--data":
                case "-d":
                    if (i >= args.Length)
                    {
                        stderr.WriteLine("[错误] --data 参数缺少目录路径。");
                        return ExitInvalidArgs;
                    }
                    dataRoot = args[i++];
                    break;
                case "--table":
                case "-t":
                    if (i >= args.Length)
                    {
                        stderr.WriteLine("[错误] --table 参数缺少表名。");
                        return ExitInvalidArgs;
                    }
                    tableId = args[i++];
                    break;
                case "--port":
                case "-p":
                    if (i >= args.Length || !int.TryParse(args[i++], out port) || port < 0 || port > 65535)
                    {
                        stderr.WriteLine("[错误] --port 参数无效。");
                        return ExitInvalidArgs;
                    }
                    break;
                case "--no-open":
                    openBrowser = false;
                    break;
                default:
                    stderr.WriteLine($"[错误] 未知参数: {arg}");
                    PrintUsage(stderr);
                    return ExitInvalidArgs;
            }
        }

        try
        {
            var report = JsonReportExporter.Load(reportPath);
            using var cts = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                var server = new ReportViewerServer(report, dataRoot, tableId, $"CarrotQuant - {Path.GetFileName(reportPath)}", port: port);
                server.StartAsync(cts.Token).GetAwaiter().GetResult();

                stdout.WriteLine($"[CarrotQuant] 交互式分析看板已启动: {server.BaseUrl}");
                if (dataRoot != null)
                {
                    stdout.WriteLine($"[CarrotQuant] 已挂载行情目录: {Path.GetFullPath(dataRoot)} (table={tableId})");
                }
                stdout.WriteLine("按 Ctrl+C 停止服务...");

                if (openBrowser)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(server.BaseUrl) { UseShellExecute = true });
                    }
                    catch
                    {
                        // 无桌面环境时忽略拉起浏览器失败
                    }
                }

                try
                {
                    Task.Delay(Timeout.Infinite, cts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    // 正常 Ctrl+C 退出
                }

                server.StopAsync().GetAwaiter().GetResult();
                server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }

            return ExitSuccess;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"[CarrotQuant 异常中止] {ex.Message}");
            return ExitExecutionError;
        }
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("""
        CarrotQuant .NET 回测命令行工具

        用法:
          carrot-backtest [run] <config.yaml> [选项]
          carrot-backtest view <report.json> [选项]

        run 选项:
          -s, --start-date <YYYY-MM-DD>  动态覆盖回测起始日期
          -e, --end-date <YYYY-MM-DD>    动态覆盖回测结束日期
          --no-export                    禁用报表文件导出（仅保留控制台打印）

        view 选项:
          -d, --data <dir>               可选行情数据根目录（用于按需加载 K 线买卖点）
          -t, --table <table_id>         行情时序表名（默认: daily）
          -p, --port <port>              指定监听端口（默认自动分配空闲端口）
          --no-open                      启动后不自动打开浏览器

        通用选项:
          -h, --help                     显示此帮助信息
        """);
    }
}
