using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Result;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CarrotBacktesting.NET.Analysis.Analyzers
{
    public class TradeAnalyzer : IAnalyzer
    {
        public string Name => nameof(TradeAnalyzer);
        private int _exitTimingDays = 30;

        public void Init(AnalyzerConfig config)
        {
            _exitTimingDays = config.ExitDays;
        }

        public void Analyze(AnalysisContext context)
        {
            var trades = context.BacktestResult.Trades;
            if (trades == null || trades.Count == 0)
            {
                Console.WriteLine("[TradeAnalyzer] 没有交易可供分析。");
                return;
            }

            // 检查数据模式
            if (context.Data is not HistoryStorage hs)
            {
                // 如果不是 HistoryStorage，目前暂不进行深度分析，或者仅做基础统计
                // 这里为了简化，假设必须是 HS
                return;
            }

            var finalResult = new TradeAnalysisResult();
            int exitTimingDays = _exitTimingDays;

            // 1. 生成 [Total] 分组
            finalResult.Add("Total", GenerateReport(trades, hs, exitTimingDays));

            // 2. 生成各子分组
            var subGroups = trades
                .Where(t => !string.IsNullOrEmpty(t.EntryGroup) && t.EntryGroup.ToLower() != "default")
                .GroupBy(t => t.EntryGroup);

            foreach (var group in subGroups)
            {
                finalResult.Add(group.Key, GenerateReport(group.ToList(), hs, exitTimingDays));
            }

            // 3. 存入 Context
            context.SetArtifact(finalResult);
        }

        /// <summary>
        /// 核心逻辑：为指定的交易列表生成完整的交易报告
        /// </summary>
        private TradeReport GenerateReport(List<Trade> tradesSubset, HistoryStorage historyStorage, int backtestDays)
        {
            // 计算卖点时机 (Exit Timing)
            // 注意：这里对每个子集都重新计算了一次 ExitTiming。
            // 虽然可以通过缓存优化，但考虑到已平仓交易数量通常远小于信号数量，
            // 且 ExitTiming 计算量不大，直接重算代码更简洁。
            var exitTimingReturns = CalculateExitTimingReturns(tradesSubset, historyStorage, backtestDays);

            return new TradeReport(tradesSubset, exitTimingReturns, backtestDays);
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
