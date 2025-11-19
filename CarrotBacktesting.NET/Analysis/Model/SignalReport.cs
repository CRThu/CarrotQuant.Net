using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Model
{
    /// <summary>
    /// 信号性能统计指标容器
    /// </summary>
    public record SignalPerf(
        int SignalCount,        // 样本数量
        double WinRate,         // 胜率
        double AvgReturn,       // 平均收益率
        double MedianReturn,    // 中位数收益率
        double AvgWin,          // 平均盈利
        double AvgLoss,         // 平均亏损
        double WinLossRatio     // 盈亏比
    );

    /// <summary>
    /// 单个时间周期 (T+X) 的信号分析报告。
    /// </summary>
    public class SignalReport
    {
        /// <summary>
        /// 该收益率对应的时间。
        /// </summary>
        public IReadOnlyList<DateTime> Dates { get; }

        /// <summary>
        /// 该持有天数下的原始收益率列表。
        /// </summary>
        public IReadOnlyList<double> Returns { get; }

        /// <summary>
        /// 该持有天数下的全局统计表现。
        /// </summary>
        public SignalPerf Global { get; }

        /// <summary>
        /// 该持有天数下的月度统计表现。
        /// </summary>
        public IReadOnlyList<(DateTime Month, SignalPerf Perf)> Monthly { get; }

        /// <summary>
        /// 用于统计的有效信号数量。
        /// </summary>
        public int ValidSignalCount => Returns.Count;

        /// <summary>
        /// 构造函数，在内部完成所有统计计算。
        /// </summary>
        /// <param name="returns">该天数对应的所有信号收益率列表</param>
        /// <param name="trades">原始交易列表 (用于获取日期进行月度分组)</param>
        public SignalReport(IEnumerable<double> returns, List<Trade> trades)
        {
            // 1. 保存原始数据
            Dates = trades.Select(d => d.EntryDate).ToList();
            var returnsList = returns.ToList();
            Returns = returnsList;

            // 2. 计算全局表现
            Global = GetPerf(returnsList);

            // 3. 计算月度表现
            var monthlyList = new List<(DateTime, SignalPerf)>();
            if (trades != null && trades.Count == returnsList.Count && returnsList.Count > 0)
            {
                // 将日期与当前 Horizon 的收益率对齐
                var signalData = Dates.Zip(returnsList, (date, ret) => new
                {
                    Date = date,
                    Return = ret
                });

                monthlyList = signalData
                    .GroupBy(x => new { x.Date.Year, x.Date.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g =>
                    {
                        var monthDate = new DateTime(g.Key.Year, g.Key.Month, 1);
                        var samples = g.Select(x => x.Return);
                        return (monthDate, GetPerf(samples));
                    })
                    .ToList();
            }
            Monthly = monthlyList;
        }

        /// <summary>
        /// 核心统计逻辑：输入一组收益率样本，返回统计对象。
        /// </summary>
        private static SignalPerf GetPerf(IEnumerable<double> samples)
        {
            var list = samples.ToList(); // 固化列表
            int count = list.Count;

            if (count == 0)
            {
                return new SignalPerf(0, 0, 0, 0, 0, 0, 0);
            }

            double avg = list.Average();
            var median = list.Median();
            var wins = list.Where(r => r > 0).ToList();
            var losses = list.Where(r => r < 0).ToList();
            double winRate = (double)wins.Count / count;
            double avgWin = wins.Count != 0 ? wins.Average() : 0;
            double avgLoss = losses.Count != 0 ? losses.Average() : 0;
            double ratio = avgWin / Math.Abs(avgLoss == 0 ? 1 : avgLoss);

            return new SignalPerf(
                SignalCount: count,
                WinRate: winRate,
                AvgReturn: avg,
                MedianReturn: median,
                AvgWin: avgWin,
                AvgLoss: avgLoss,
                WinLossRatio: ratio
            );
        }
    }
}
