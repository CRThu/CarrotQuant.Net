using CarrotBacktesting.NET.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Model
{
    /// <summary>
    /// 对平仓点未来T+N日表现的分析结果 (卖出时机/踏空分析)。
    /// </summary>
    public class ExitTimingReport
    {
        /// <summary>
        /// 回测统计的总天数 (N)。
        /// </summary>
        public int BacktestDays { get; }

        /// <summary>
        /// 用于统计的有效平仓点数量。
        /// </summary>
        public int ValidExitCount => Returns.Count;

        /// <summary>
        /// 详细的平仓后收益率矩阵。
        /// </summary>
        public IReadOnlyList<double[]> Returns { get; }

        /// <summary>
        /// 每日的平均后续收益率数组。
        /// </summary>
        public IReadOnlyList<double> AvgReturns { get; }

        /// <summary>
        /// 每日的后续收益率中位数数组。
        /// </summary>
        public IReadOnlyList<double> MedianReturns { get; }

        /// <summary>
        /// 每日的后续上涨概率 (胜率) 数组。
        /// </summary>
        public IReadOnlyList<double> WinRates { get; }

        /// <summary>
        /// 构造函数，在内部完成所有统计计算。
        /// </summary>
        /// <param name="returns">从平仓点计算出的详细收益率矩阵。</param>
        /// <param name="backtestDays">回测天数。</param>
        public ExitTimingReport(List<double[]> returns, int backtestDays)
        {
            // 存储基础数据
            Returns = returns;
            BacktestDays = backtestDays;

            // --- 在构造时立即进行统计聚合计算 ---
            var avgReturns = new double[backtestDays];
            var medianReturns = new double[backtestDays];
            var winRates = new double[backtestDays];

            if (returns.Count > 0)
            {
                for (int day = 0; day < backtestDays; day++)
                {
                    // 提取 T+(day+1) 这一天的所有平仓后收益率
                    var returnsOnDayN = returns.Select(signalReturns => signalReturns[day]).ToList();

                    // 计算统计指标
                    avgReturns[day] = returnsOnDayN.Average();
                    winRates[day] = (double)returnsOnDayN.Count(r => r > 0) / returnsOnDayN.Count;
                    medianReturns[day] = returnsOnDayN.Median();
                }
            }

            // 将计算结果赋给只读属性
            AvgReturns = avgReturns;
            MedianReturns = medianReturns;
            WinRates = winRates;
        }
    }
}
