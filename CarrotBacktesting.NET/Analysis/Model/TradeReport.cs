using CarrotBacktesting.NET.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Model
{
    /// <summary>
    /// 对已完成交易列表的核心统计分析结果
    /// </summary>
    public class TradeReport
    {
        public int TotalTrades { get; }
        public double WinRate { get; }
        public double AverageReturn { get; }
        public double AverageWinReturn { get; }
        public double AverageLossReturn { get; }
        public double WinLossRatio { get; }
        public double AverageHoldingPeriod { get; }


        public TradeReport(List<Trade> trades)
        {
            if (trades == null || !trades.Any()) return;

            TotalTrades = trades.Count;
            // 只分析已平仓的交易
            var closedTrades = trades.Where(t => t.IsClosed).ToList();
            if (!closedTrades.Any()) return;

            var winningTrades = closedTrades.Where(t => t.Return > 0).ToList();
            var losingTrades = closedTrades.Where(t => t.Return < 0).ToList();

            WinRate = (double)winningTrades.Count / closedTrades.Count;
            AverageReturn = closedTrades.Average(t => t.Return) ?? 0;
            AverageWinReturn = winningTrades.Any() ? winningTrades.Average(t => t.Return) ?? 0 : 0;
            AverageLossReturn = losingTrades.Any() ? losingTrades.Average(t => t.Return) ?? 0 : 0;

            if (AverageLossReturn < 0)
                WinLossRatio = AverageWinReturn / System.Math.Abs(AverageLossReturn);

            AverageHoldingPeriod = closedTrades.Average(t => t.HoldingPeriod);
        }
    }
}
