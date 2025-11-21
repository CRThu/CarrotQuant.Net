using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Result;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Analyzers
{
    /// <summary>
    /// 统一的性能分析器。
    /// 负责计算信号的未来N期收益率，并生成按持有天数(T+N)分组的性能报告列表。
    /// </summary>
    public class SignalAnalyzer : IAnalyzer
    {
        public string Name => nameof(SignalAnalyzer);
        private int _backtestDays;

        public void Analyze(AnalysisContext context)
        {
            _backtestDays = context.Config.Analysis.SignalAnalysisDays;
            var trades = context.BacktestResult.Trades;

            if (trades.Count == 0)
            {
                Console.WriteLine("没有信号可供分析。");
                return;
            }

            // 检查数据模式
            if (context.Data is not HistoryStorage hs)
            {
                throw new NotImplementedException("性能分析器当前仅在 TimeSeries 存储模式下高效运行。");
            }

            Console.WriteLine($"开始执行信号分组分析 (未来 {_backtestDays} 日)...");
            var stopwatch = Stopwatch.StartNew();

            // 1. 预计算所有交易的收益率 (缓存层)
            // 这样后续分组时不需要重复查询 HistoryStorage
            var allReturnsCache = new List<(Trade trade, double[] returns)>(trades.Count);

            foreach (var trade in trades)
            {
                if (hs.StockHistories.TryGetValue(trade.StockCode, out var history))
                {
                    var returns = CalculateReturnsForSignal(trade, history);
                    if (returns != null)
                        allReturnsCache.Add((trade, returns));
                }
            }

            // 2. 准备结果容器
            var finalResult = new SignalAnalysisResult();

            // 3. 生成 [Total] 分组
            if (allReturnsCache.Count > 1)
            {
                finalResult.Add("Total", GenerateReportsFromCache(allReturnsCache));
            }

            // 4. 生成各子分组
            var subGroups = allReturnsCache
                .Where(x => !string.IsNullOrEmpty(x.trade.EntryGroup) && x.trade.EntryGroup.ToLower() != "default")
                .GroupBy(x => x.trade.EntryGroup);

            foreach (var group in subGroups)
            {
                // group 是 IGrouping<string, (Trade, double[])>
                // ToList() 后变成 List<(Trade, double[])>，正好可以直接传入生成方法
                finalResult.Add(group.Key, GenerateReportsFromCache(group.ToList()));
            }

            stopwatch.Stop();
            Console.WriteLine($"信号分析完成，共生成 {finalResult.Groups.Count} 个分组报告，耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒。");

            // 5. 存入 Context (使用强类型)
            context.SetArtifact(finalResult);
        }

        /// <summary>
        /// 核心逻辑：根据缓存的 (Trade, Returns) 列表生成 T+N 报告数组
        /// </summary>
        private SignalReport[] GenerateReportsFromCache(List<(Trade trade, double[] returns)> cacheSubset)
        {
            var reports = new SignalReport[_backtestDays];

            for (int i = 0; i < _backtestDays; i++)
            {
                var info = cacheSubset.Select(x => (x.trade, x.returns[i]));
                reports[i] = new SignalReport(info);
            }

            return reports;
        }

        /// <summary>
        /// 为单个信号计算其未来N天的收益率序列。
        /// </summary>
        private double[]? CalculateReturnsForSignal(Trade trade, StockHistory history)
        {
            // 使用二分查找定位信号日
            int tradeIndex = history.Dates.ToList().BinarySearch(trade.EntryDate);
            if (tradeIndex < 0) return null;

            // 检查是否有足够的数据
            if (tradeIndex + _backtestDays >= history.Data.Count) return null;

            double closeT0 = history.Data[tradeIndex].Close;
            if (closeT0 <= 0) return null;

            var returns = new double[_backtestDays];
            for (int i = 0; i < _backtestDays; i++)
            {
                double closeTn = history.Data[tradeIndex + 1 + i].Close;
                returns[i] = (closeTn - closeT0) / closeT0;
            }
            return returns;
        }
    }
}
