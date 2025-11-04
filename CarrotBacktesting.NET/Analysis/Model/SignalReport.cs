using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Model
{
    /// <summary>
    /// 封装了信号表现的核心分析结果。
    /// 包含了详细的逐笔收益率数据和聚合后的总体统计摘要。
    /// </summary>
    public class SignalReport
    {
        /// <summary>
        /// 回测统计的总天数 (N)。
        /// </summary>
        public int BacktestDays { get; }

        /// <summary>
        /// 详细的收益率矩阵。
        /// 外层List代表每个信号，内层double[]代表该信号未来N天的收益率。
        /// 这是所有统计计算和高级可视化的数据源。
        /// </summary>
        public IReadOnlyList<double[]> Returns { get; }

        /// <summary>
        /// 用于统计的有效信号数量。
        /// </summary>
        public int ValidSignalCount => Returns.Count;

        /// <summary>
        /// 每日的平均收益率数组，长度为 BacktestDays。
        /// </summary>
        public IReadOnlyList<double> AvgReturns { get; }

        /// <summary>
        /// 每日的收益率中位数数组，长度为 BacktestDays。
        /// </summary>
        public IReadOnlyList<double> MedianReturns { get; }

        /// <summary>
        /// 每日的胜率数组，长度为 BacktestDays。
        /// </summary>
        public IReadOnlyList<double> WinRates { get; }

        /// <summary>
        /// 构造函数，在内部完成所有统计计算。
        /// </summary>
        /// <param name="returns">从原始信号计算出的详细收益率矩阵。</param>
        /// <param name="backtestDays">回测天数。</param>
        public SignalReport(List<double[]> returns, int backtestDays)
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
                    // 提取 T+(day+1) 这一天的所有信号收益率
                    var returnsOnDayN = returns.Select(signalReturns => signalReturns[day]).ToList();

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
            }

            // 将计算结果赋给只读属性
            AvgReturns = avgReturns;
            MedianReturns = medianReturns;
            WinRates = winRates;
        }
    }
}
