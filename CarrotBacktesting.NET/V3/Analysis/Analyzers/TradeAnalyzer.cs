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

            var finalResult = new TradeAnalysisResult();
            int exitTimingDays = _exitTimingDays;

            // 1. 生成 [Total] 分组
            finalResult.Add("Total", GenerateReport(trades, context.StockHistories, exitTimingDays));

            // 2. 生成各子分组
            var subGroups = trades
                .Where(t => !string.IsNullOrEmpty(t.EntryGroup) && t.EntryGroup.ToLower() != "default")
                .GroupBy(t => t.EntryGroup);

            foreach (var group in subGroups)
            {
                finalResult.Add(group.Key, GenerateReport(group.ToList(), context.StockHistories, exitTimingDays));
            }

            // 3. 存入 Context
            context.SetArtifact(finalResult);
        }

        /// <summary>
        /// 核心逻辑：为指定的交易列表生成完整的交易报告
        /// </summary>
        private TradeReport GenerateReport(List<Trade> tradesSubset, IReadOnlyDictionary<string, StockHistory> stockHistories, int backtestDays)
        {
            // 计算卖点时机 (Exit Timing)
            var exitTimingReturns = CalculateExitTimingReturns(tradesSubset, stockHistories, backtestDays);

            return new TradeReport(tradesSubset, exitTimingReturns, backtestDays);
        }

        /// <summary>
        /// 计算所有平仓点之后的T+N日收益率。
        /// </summary>
        private List<double[]> CalculateExitTimingReturns(List<Trade> trades, IReadOnlyDictionary<string, StockHistory> stockHistories, int backtestDays)
        {
            var allReturnsOverTime = new List<double[]>();
            var closedTrades = trades.Where(t => t.IsClosed && t.ExitDate.HasValue);

            foreach (var trade in closedTrades)
            {
                if (stockHistories.TryGetValue(trade.StockCode, out var history))
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
