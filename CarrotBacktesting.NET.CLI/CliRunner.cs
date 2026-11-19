using System;
using System.Globalization;
using System.IO;
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
        DateTime? overrideStartDate = null;
        DateTime? overrideEndDate = null;
        bool noExport = false;

        while (index < args.Length)
        {
            string current = args[index++];
            switch (current)
            {
                case "--start-date":
                case "-s":
                    if (index >= args.Length || !DateTime.TryParse(args[index++], CultureInfo.InvariantCulture, DateTimeStyles.None, out var sDate))
                    {
                        stderr.WriteLine("[错误] --start-date 参数缺少有效日期值 (格式如: 2024-01-01)。");
                        return ExitInvalidArgs;
                    }
                    overrideStartDate = sDate;
                    break;

                case "--end-date":
                case "-e":
                    if (index >= args.Length || !DateTime.TryParse(args[index++], CultureInfo.InvariantCulture, DateTimeStyles.None, out var eDate))
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

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("""
        CarrotQuant .NET 回测命令行工具

        用法:
          carrot-backtest [run] <config.yaml> [选项]

        参数:
          <config.yaml>              回测 YAML 配置文件物理路径

        选项:
          -s, --start-date <YYYY-MM-DD>  动态覆盖回测起始日期
          -e, --end-date <YYYY-MM-DD>    动态覆盖回测结束日期
          --no-export                    禁用报表文件导出（仅保留控制台打印）
          -h, --help                     显示此帮助信息
        """);
    }
}
