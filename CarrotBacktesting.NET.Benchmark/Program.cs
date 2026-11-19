using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;

namespace CarrotBacktesting.NET.Benchmark;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                // 默认直接执行 5 标的 vs 5000 标的正交分层性能归因基准
                await BenchmarkSuite.RunLayeredBenchmark();
                return 0;
            }

            string cmd = args[0].ToLowerInvariant();
            switch (cmd)
            {
                case "macro":
                case "suite":
                case "layered":
                    await BenchmarkSuite.RunLayeredBenchmark();
                    break;

                case "bdn":
                case "micro":
                    var bdnArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();
                    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(bdnArgs);
                    break;

                case "all":
                    Console.WriteLine("\n[1/2] 正在运行 5//5000 标的分层性能归因宏观基准测试 (Macro Harness)...\n");
                    await BenchmarkSuite.RunLayeredBenchmark();
                    Console.WriteLine("\n[2/2] 正在启动 BenchmarkDotNet 底层微基准压测 (Micro BDN)...\n");
                    var allBdnArgs = args.Length > 1 ? args[1..] : new[] { "--filter", "*" };
                    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(allBdnArgs);
                    break;

                case "real":
                case "parquet":
                case "matrix3x3x3":
                case "3x3x3":
                    await BenchmarkSuite.RunMatrix3x3x3ParquetSessionBenchmark();
                    break;

                case "matrix":
                    BenchmarkSuite.RunMatrixBenchmark();
                    break;

                case "indicators":
                case "indicator":
                    BenchmarkSuite.RunIndicatorsBenchmark();
                    break;

                case "broker":
                case "match":
                    BenchmarkSuite.RunBrokerBenchmark();
                    break;

                case "matrix3x3":
                case "session":
                case "matrix-3x3":
                    BenchmarkSuite.RunMatrix3x3SessionBenchmark();
                    break;

                case "cwh":
                case "cache":
                case "cold-warm-hot":
                    BenchmarkSuite.RunColdWarmHotBenchmark();
                    break;

                case "backtest":
                    int symbols = 5000;
                    int steps = 240;
                    string mode = "full";

                    for (int i = 1; i < args.Length; i++)
                    {
                        if ((args[i] == "-s" || args[i] == "--symbols") && i + 1 < args.Length)
                        {
                            int.TryParse(args[++i], out symbols);
                        }
                        else if ((args[i] == "-b" || args[i] == "--bars" || args[i] == "--steps") && i + 1 < args.Length)
                        {
                            int.TryParse(args[++i], out steps);
                        }
                        else if ((args[i] == "-m" || args[i] == "--mode") && i + 1 < args.Length)
                        {
                            mode = args[++i];
                        }
                    }

                    BenchmarkSuite.RunBacktestBenchmark(symbols, steps, mode);
                    break;

                default:
                    Console.WriteLine($"""
                    未知的跑分命令: {args[0]}
                    用法:
                      carrot-benchmark [suite]                        执行 5 标的 vs 5000 标的分层性能归因基准 (默认)
                      carrot-benchmark bdn [--filter *]              执行 BenchmarkDotNet 底层算子微基准压测
                      carrot-benchmark all                           顺序执行分层宏观基准与全部 BDN 微基准
                      carrot-benchmark 3x3x3                         执行 3×3×3 全景 Parquet 真实冷温热矩阵压测
                      carrot-benchmark matrix                        执行二维连续矩阵内存访问吞吐基准
                      carrot-benchmark indicators                    执行增量流式指标计算基准
                      carrot-benchmark broker                        执行撮合与资金账本基准
                      carrot-benchmark backtest [-s 5000] [-b 240] [-m full|empty|read] 执行端到端回测基准
                    """);
                    return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[基准测试异常退出]: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
            return -1;
        }
    }
}
