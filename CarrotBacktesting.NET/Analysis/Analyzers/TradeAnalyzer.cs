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
    public class TradeAnalyzer : IAnalyzer
    {
        public string Name => nameof(TradeAnalyzer);

        public void Analyze(AnalysisContext context)
        {
            var trades = context.BacktestResult.Trades;
            if (trades == null || trades.Count == 0)
            {
                Console.WriteLine("[TradeAnalyzer] 没有交易可供分析。");
                return;
            }

            // 执行卖点时机分析的计算
            var exitTimingBacktestDays = context.Config.Analysis.SignalAnalysisDays; // 复用配置
            List<double[]>? exitTimingReturns = null;
            if (context.Data is HistoryStorage hs)
            {
                exitTimingReturns = CalculateExitTimingReturns(trades, hs, exitTimingBacktestDays);
            }

            // 创建一个包含了所有分析结果的统一报告
            var report = new TradeReport(trades, exitTimingReturns, exitTimingBacktestDays);

            // 将最终的统一报告存入上下文
            context.SetArtifact(report);
        }

        /// <summary>
        /// 计算所有平仓点之后的T+N日收益率。
        /// </summary>
        private List<double[]> CalculateExitTimingReturns(List<Trade> trades, HistoryStorage historyStorage, int backtestDays)
        {
            var allReturnsOverTime = new List<double[]>();
            var closedTrades = trades.Where(t => t.IsClosed && t.ExitDate.HasValue);

            foreach (var trade in closedTrades)
            {
                if (historyStorage.StockHistories.TryGetValue(trade.StockCode, out var history))
                {
                    int exitIndex = history.Dates.ToList().BinarySearch(trade.ExitDate!.Value);
                    if (exitIndex < 0) continue;
                    if (exitIndex + backtestDays >= history.Data.Count) continue;
                    double closeT0 = history.Data[exitIndex].Close;
                    if (closeT0 <= 0) continue;

                    var returns = new double[backtestDays];
                    for (int i = 0; i < backtestDays; i++)
                    {
                        double closeTn = history.Data[exitIndex + 1 + i].Close;
                        returns[i] = (closeTn - closeT0) / closeT0;
                    }
                    allReturnsOverTime.Add(returns);
                }
            }
            return allReturnsOverTime;
        }
    }
}
