using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Analyzers
{
    /// <summary>
    /// 【新】卖出时机分析器，负责计算平仓点之后的T+N日收益率。
    /// </summary>
    public class ExitTimingAnalyzer : IAnalyzer
    {
        public string Name => nameof(ExitTimingAnalyzer);
        private int _backtestDays;

        public void Analyze(AnalysisContext context)
        {
            _backtestDays = context.Config.Analysis.SignalAnalysisDays; // 复用同一个配置

            // 1. 筛选出所有【已平仓】的交易
            var closedTrades = context.BacktestResult.Trades
                .Where(t => t.IsClosed && t.ExitDate.HasValue)
                .ToList();

            if (closedTrades.Count == 0)
            {
                // Console.WriteLine("[ExitTimingAnalyzer] 没有已平仓的交易可供进行卖点分析。");
                return;
            }

            var allReturnsOverTime = new List<double[]>();

            if (context.Data is HistoryStorage hs)
            {
                foreach (var trade in closedTrades)
                {
                    if (hs.StockHistories.TryGetValue(trade.StockCode, out var history))
                    {
                        // 2. 对每个平仓点，计算其未来N日收益
                        var returns = CalculateReturnsAfterExit(trade, history);
                        if (returns != null)
                            allReturnsOverTime.Add(returns);
                    }
                }
            }

            // 3. 创建并存储报告
            var report = new ExitTimingReport(allReturnsOverTime, _backtestDays);
            context.SetArtifact(report);
        }

        /// <summary>
        /// 为单个平仓点计算其未来N天的收益率序列。
        /// </summary>
        private double[]? CalculateReturnsAfterExit(Trade trade, StockHistory history)
        {
            // 定位【平仓日】的索引
            int exitIndex = history.Dates.ToList().BinarySearch(trade.ExitDate!.Value);
            if (exitIndex < 0) return null;

            // 检查是否有足够的未来数据
            if (exitIndex + _backtestDays >= history.Data.Count) return null;

            // 以【平仓日】的收盘价为基准
            double closeT0 = history.Data[exitIndex].Close;
            if (closeT0 <= 0) return null;

            var returns = new double[_backtestDays];
            for (int i = 0; i < _backtestDays; i++)
            {
                double closeTn = history.Data[exitIndex + 1 + i].Close;
                returns[i] = (closeTn - closeT0) / closeT0;
            }
            return returns;
        }
    }
}
