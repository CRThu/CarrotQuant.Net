using CarrotBacktesting.NET.Analysis.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Presenters
{
    public class ConsoleExporter : IExporter
    {
        public string Name => "ConsoleSummary";

        public void Export(AnalysisContext context)
        {
            var summary = context.GetArtifact<SummaryResult>();
            if (summary == null) return;

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine($"策略在 T+1 至 T+{summary.BacktestDays} 期间的每日表现统计");
            Console.WriteLine($"基于 {summary.ValidSignalCount} 个有效信号");
            Console.WriteLine(new string('=', 50));

            var sb = new StringBuilder();
            sb.AppendLine($"{"持有天数",-10} | {"平均收益率",-12} | {"收益率中位数",-14} | {"胜率",-10}");
            sb.AppendLine(new string('-', 55));

            for (int i = 0; i < summary.BacktestDays; i++)
            {
                sb.AppendLine($"{"T+" + (i + 1),-10} | {summary.AvgReturns[i],-12:P2} | {summary.MedianReturns[i],-14:P2} | {summary.WinRates[i],-10:P2}");
            }
            Console.WriteLine(sb.ToString());

            // 打印最佳持有期分析
            PrintPeakAnalysis(summary);
        }

        private void PrintPeakAnalysis(SummaryResult summary)
        {
            if (summary.ValidSignalCount == 0) return;

            var maxAvgReturn = summary.AvgReturns.Max();
            int dayMaxAvg = Array.IndexOf(summary.AvgReturns, maxAvgReturn) + 1;
            var winRateAtMaxAvg = summary.WinRates[dayMaxAvg - 1];

            var maxMedianReturn = summary.MedianReturns.Max();
            int dayMaxMedian = Array.IndexOf(summary.MedianReturns, maxMedianReturn) + 1;
            var winRateAtMaxMedian = summary.WinRates[dayMaxMedian - 1];

            var maxWinRate = summary.WinRates.Max();
            int dayMaxWin = Array.IndexOf(summary.WinRates, maxWinRate) + 1;

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("策略最佳持有期分析 (各指标峰值)");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"平均收益率峰值: {maxAvgReturn:P2} (T+{dayMaxAvg}, 当日胜率 {winRateAtMaxAvg:P2})");
            Console.WriteLine($"收益率中位数峰值: {maxMedianReturn:P2} (T+{dayMaxMedian}, 当日胜率 {winRateAtMaxMedian:P2})");
            Console.WriteLine($"(参考) 胜率峰值: {maxWinRate:P2} (T+{dayMaxWin})");
            Console.WriteLine(new string('-', 50));
        }
    }
}
