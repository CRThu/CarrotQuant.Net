using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Exporters
{
    public class ConsoleExporter : IExporter
    {
        public string Name => nameof(ConsoleExporter);
        
        /// <summary>
        /// 导出器的入口方法
        /// </summary>
        public void Export(AnalysisContext context)
        {
            // 从上下文中获取核心分析报告
            var report = context.GetArtifact<SignalReport>();
            if (report == null || report.ValidSignalCount == 0)
            {
                Console.WriteLine("[ConsoleExporter] 没有有效的信号报告可供打印。");
                return;
            }

            Console.WriteLine();

            PrintDailySummary(report);
            PrintPeakAnalysis(report);

            // 月度分析需要原始信号的时间信息
            var signals = context.BacktestResult.SignalsResult.GetSignals();
            PrintMonthlyReturns(report, signals.ToList());
        }

        /// <summary>
        /// 打印每日表现统计表格
        /// </summary>
        private void PrintDailySummary(SignalReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 55));
            sb.AppendLine($"策略在 T+1 至 T+{report.BacktestDays} 期间的每日表现统计");
            sb.AppendLine($"基于 {report.ValidSignalCount} 个有效信号");
            sb.AppendLine(new string('=', 55));

            // 打印表头
            sb.AppendLine($"{"持有天数",-10} | {"平均收益率",-12} | {"收益率中位数",-14} | {"胜率",-10}");
            sb.AppendLine(new string('-', 55));

            // 循环打印每一行数据
            for (int i = 0; i < report.BacktestDays; i++)
            {
                sb.AppendLine($"{"T+" + (i + 1),-10} | {report.AvgReturns[i],-12:P2} | {report.MedianReturns[i],-14:P2} | {report.WinRates[i],-10:P2}");
            }
            Console.WriteLine(sb.ToString());
        }

        /// <summary>
        /// 打印最佳持有期分析
        /// </summary>
        private void PrintPeakAnalysis(SignalReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 50));
            sb.AppendLine("策略最佳持有期分析 (各指标峰值)");
            sb.AppendLine(new string('=', 50));

            // 计算平均收益率峰值
            var maxAvgReturn = report.AvgReturns.Max();
            int dayMaxAvg = Array.IndexOf(report.AvgReturns.ToArray(), maxAvgReturn) + 1;
            var winRateAtMaxAvg = report.WinRates[dayMaxAvg - 1];

            // 计算收益率中位数峰值
            var maxMedianReturn = report.MedianReturns.Max();
            int dayMaxMedian = Array.IndexOf(report.MedianReturns.ToArray(), maxMedianReturn) + 1;
            var winRateAtMaxMedian = report.WinRates[dayMaxMedian - 1];

            // 计算胜率峰值
            var maxWinRate = report.WinRates.Max();
            int dayMaxWin = Array.IndexOf(report.WinRates.ToArray(), maxWinRate) + 1;

            sb.AppendLine($"平均收益率峰值: {maxAvgReturn:P2} (T+{dayMaxAvg}, 当日胜率 {winRateAtMaxAvg:P2})");
            sb.AppendLine($"收益率中位数峰值: {maxMedianReturn:P2} (T+{dayMaxMedian}, 当日胜率 {winRateAtMaxMedian:P2})");
            sb.AppendLine($"(参考) 胜率峰值: {maxWinRate:P2} (T+{dayMaxWin})");
            sb.AppendLine(new string('-', 50));
            Console.WriteLine(sb.ToString());
        }

        /// <summary>
        /// 打印 T+X 月度收益统计
        /// </summary>
        private void PrintMonthlyReturns(SignalReport report, List<Result.SignalInfo> signals)
        {
            // 找到最佳平均收益率的持有天数
            var maxAvgReturn = report.AvgReturns.Max();
            int bestDay = Array.IndexOf(report.AvgReturns.ToArray(), maxAvgReturn); // 索引 (0-based)

            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 50));
            sb.AppendLine($"策略信号在 T+{bestDay + 1} 的月度收益统计");
            sb.AppendLine(new string('=', 50));

            // 将信号的日期和其在最佳持有期的收益率配对
            var returnsWithDate = signals.Zip(report.Returns, (signal, returns) => new
            {
                Date = signal.Date,
                Return = returns[bestDay]
            });

            // 按年和月进行分组
            var monthlyStats = returnsWithDate
                .GroupBy(x => new { x.Date.Year, x.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new
                {
                    Month = new DateTime(g.Key.Year, g.Key.Month, 1),
                    SignalCount = g.Count(),
                    AvgReturn = g.Average(x => x.Return),
                    MedianReturn = g.Select(x => x.Return).Median() // 需要一个Median扩展方法
                });

            // 打印表头
            sb.AppendLine($"{"月份",-10} | {"信号数",-8} | {"月度平均收益",-15} | {"月度中位数收益",-18}");
            sb.AppendLine(new string('-', 60));

            // 循环打印每个月的数据
            foreach (var stat in monthlyStats)
            {
                sb.AppendLine($"{stat.Month:yyyy-MM} | {stat.SignalCount,-8} | {stat.AvgReturn,-15:P2} | {stat.MedianReturn,-18:P2}");
            }
            Console.WriteLine(sb.ToString());
        }
    }
}
