using CarrotBacktesting.NET.Analysis.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Analyzers
{
    public class SummaryAnalyzer : IAnalyzer
    {
        public string Name => "SummaryAnalyzer";

        public void Analyze(AnalysisContext context)
        {
            // 从上下文中获取前一个分析器的产出
            var returnsResult = context.GetArtifact<ForwardReturnsResult>();
            if (returnsResult == null || returnsResult.Returns.Count == 0)
            {
                return;
            }

            int backtestDays = returnsResult.BacktestDays;
            var returnsMatrix = returnsResult.Returns;

            var avgReturns = new double[backtestDays];
            var medianReturns = new double[backtestDays];
            var winRates = new double[backtestDays];

            for (int day = 0; day < backtestDays; day++)
            {
                // 提取T+N天的所有收益率
                var returnsOnDayN = new List<double>(returnsMatrix.Count);
                for (int signalIdx = 0; signalIdx < returnsMatrix.Count; signalIdx++)
                {
                    returnsOnDayN.Add(returnsMatrix[signalIdx][day]);
                }

                // 计算统计指标
                avgReturns[day] = returnsOnDayN.Average();
                winRates[day] = (double)returnsOnDayN.Count(r => r > 0) / returnsOnDayN.Count;

                // 计算中位数
                var sortedReturns = returnsOnDayN.OrderBy(r => r).ToList();
                int mid = sortedReturns.Count / 2;
                medianReturns[day] = sortedReturns.Count % 2 == 0 ?
                    (sortedReturns[mid - 1] + sortedReturns[mid]) / 2.0 :
                    sortedReturns[mid];
            }

            var summary = new SummaryResult(backtestDays, returnsMatrix.Count, avgReturns, medianReturns, winRates);
            context.SetArtifact(summary);
        }
    }
}
