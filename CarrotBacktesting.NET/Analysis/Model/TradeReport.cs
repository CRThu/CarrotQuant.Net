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
    /// 存储单个自然月的交易性能统计数据。
    /// </summary>
    public record MonthlyPerformanceStat(
        DateTime Month,
        int TradeCount,
        double WinRate,
        double AverageReturn,
        double MedianReturn,
        double AverageWin,
        double AverageLoss,
        double WinLossRatio);

    /// <summary>
    /// 对已完成交易列表的核心统计分析结果
    /// </summary>
    public class TradeReport
    {
        #region 总体统计指标
        public int TotalTrades { get; }
        public double WinRate { get; }
        public double AverageReturn { get; }
        /// <summary>
        /// 所有已平仓交易的收益率中位数。
        /// </summary>
        public double MedianReturn { get; }
        public double AverageWinReturn { get; }
        public double AverageLossReturn { get; }
        public double WinLossRatio { get; }
        public double AverageHoldingPeriod { get; }
        /// <summary>
        /// 所有盈利交易的平均交易效率。
        /// </summary>
        public double AverageTradeEfficiency { get; }

        /// <summary>
        /// 所有盈利交易的交易效率中位数。
        /// </summary>
        public double MedianTradeEfficiency { get; }
        #endregion

        #region 月度统计指标
        /// <summary>
        /// 按月分组的详细性能统计列表。
        /// </summary>
        public IReadOnlyList<MonthlyPerformanceStat> MonthlyStats { get; }
        #endregion


        #region 卖点时机分析指标

        /// <summary>
        /// 用于统计的有效平仓点数量。
        /// </summary>
        public int ExitValidCount { get; }

        /// <summary>
        /// 用于卖点时机分析的回测天数 (N)。
        /// </summary>
        public int ExitTimingBacktestDays { get; }

        /// <summary>
        /// 平仓后每日的平均后续收益率数组。
        /// </summary>
        public IReadOnlyList<double> ExitTimingAvgReturns { get; }

        /// <summary>
        /// 平仓后每日的中位数后续收益率数组。
        /// </summary>
        public IReadOnlyList<double> ExitTimingMedianReturns { get; }

        /// <summary>
        /// 平仓后每日的后续上涨概率数组。
        /// </summary>
        public IReadOnlyList<double> ExitTimingWinRates { get; }

        #endregion


        public TradeReport(List<Trade> trades, List<double[]>? exitTimingReturns, int exitTimingBacktestDays)
        {
            MonthlyStats = new List<MonthlyPerformanceStat>();
            ExitTimingAvgReturns = Array.Empty<double>();
            ExitTimingMedianReturns = Array.Empty<double>();
            ExitTimingWinRates = Array.Empty<double>();

            if (trades == null || !trades.Any()) return;

            // 只分析已平仓的交易
            var closedTrades = trades.Where(t => t.IsClosed).ToList();
            if (!closedTrades.Any()) return;

            // 计算总体统计
            TotalTrades = closedTrades.Count;
            var winningTrades = closedTrades.Where(t => t.Return > 0).ToList();
            var losingTrades = closedTrades.Where(t => t.Return < 0).ToList();
            var allReturns = closedTrades.Select(t => t.Return!.Value).ToList();
            var efficiencies = closedTrades
                .Select(t => t.TradeEfficiency)
                .Where(d => d.HasValue) // 过滤掉可能为null的情况
                .Select(d => d!.Value)
                .ToList();

            WinRate = (double)winningTrades.Count / TotalTrades;
            AverageReturn = allReturns.Average();
            MedianReturn = allReturns.Median();
            AverageWinReturn = winningTrades.Any() ? winningTrades.Average(t => t.Return) ?? 0 : 0;
            AverageLossReturn = losingTrades.Any() ? losingTrades.Average(t => t.Return) ?? 0 : 0;
            if (AverageLossReturn < 0)
                WinLossRatio = AverageWinReturn / Math.Abs(AverageLossReturn);
            AverageHoldingPeriod = closedTrades.Average(t => t.HoldingPeriod);
            if (efficiencies.Count != 0)
            {
                AverageTradeEfficiency = efficiencies.Average();
                MedianTradeEfficiency = efficiencies.Median();
            }

            // 月度统计
            MonthlyStats = closedTrades
                .GroupBy(t => new { t.EntryDate.Year, t.EntryDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g =>
                {
                    var monthlyReturns = g.Select(t => t.Return!.Value).ToList();
                    var monthlyWinningReturns = monthlyReturns.Where(r => r > 0).ToList();
                    var monthlyLosingReturns = monthlyReturns.Where(r => r < 0).ToList();
                    var avgWin = monthlyWinningReturns.Any() ? monthlyWinningReturns.Average() : 0;
                    var avgLoss = monthlyLosingReturns.Any() ? monthlyLosingReturns.Average() : 0;
                    var winLossRatio = (avgLoss < 0) ? avgWin / Math.Abs(avgLoss) : 0;

                    return new MonthlyPerformanceStat(
                        new DateTime(g.Key.Year, g.Key.Month, 1),
                        g.Count(),
                        (double)monthlyWinningReturns.Count / g.Count(),
                        monthlyReturns.Average(),
                        monthlyReturns.Median(),
                        avgWin,
                        avgLoss,
                        winLossRatio
                    );
                }).ToList();

            // 处理和存储卖点时机分析的结果
            ExitTimingBacktestDays = exitTimingBacktestDays;

            var avgReturns = new double[exitTimingBacktestDays];
            var medianReturns = new double[exitTimingBacktestDays];
            var winRates = new double[exitTimingBacktestDays];

            if (exitTimingReturns != null && exitTimingReturns.Count > 0)
            {
                for (int day = 0; day < exitTimingBacktestDays; day++)
                {
                    var returnsOnDayN = exitTimingReturns.Select(r => r[day]).ToList();
                    avgReturns[day] = returnsOnDayN.Average();
                    medianReturns[day] = returnsOnDayN.Median();
                    winRates[day] = (double)returnsOnDayN.Count(r => r > 0) / returnsOnDayN.Count;
                }
            }

            ExitValidCount = exitTimingReturns?.Count ?? 0;
            ExitTimingAvgReturns = avgReturns;
            ExitTimingMedianReturns = medianReturns;
            ExitTimingWinRates = winRates;
        }
    }
}
