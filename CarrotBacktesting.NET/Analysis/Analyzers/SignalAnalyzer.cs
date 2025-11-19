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

        /// <summary>
        /// 执行分析的核心方法。
        /// </summary>
        public void Analyze(AnalysisContext context)
        {
            _backtestDays = context.Config.Analysis.SignalAnalysisDays;
            var trades = context.BacktestResult.Trades;
            if (trades.Count == 0)
            {
                Console.WriteLine("没有信号可供分析。");
                return;
            }

            // --- 第一步：计算详细的收益率矩阵 ---
            Console.WriteLine($"开始为 {trades.Count} 个信号计算未来 {_backtestDays} 日的性能表现...");
            var stopwatch = Stopwatch.StartNew();

            // 存储结构：List<double[]>，外层是信号，内层是天数 [T+1, T+2, ..., T+N]
            var allReturnsOverTime = new List<double[]>(trades.Count);

            // 目前只支持最高效的纵向数据模式
            if (context.Data is HistoryStorage hs)
            {
                foreach (var trade in trades)
                {
                    if (hs.StockHistories.TryGetValue(trade.StockCode, out var history))
                    {
                        var returns = CalculateReturnsForSignal(trade, history);
                        if (returns != null)
                            allReturnsOverTime.Add(returns);
                    }
                }
            }
            else
            {
                throw new NotImplementedException("性能分析器当前仅在 TimeSeries 存储模式下高效运行。");
            }

            // --- 第二步：数据透视 (Pivot) 与 报告生成 ---
            // 将数据从 "按信号分组" 转换为 "按持有天数分组"
            // 最终生成 SignalReport[]，索引 0 对应 T+1，索引 4 对应 T+5
            var reports = new SignalReport[_backtestDays];

            for (int i = 0; i < _backtestDays; i++)
            {
                // 提取所有信号在 T+(i+1) 这一天的收益率
                // 使用 Select 投影出第 i 列
                var returnsForDay = allReturnsOverTime.Select(r => r[i]);

                // 创建该持有天数的独立报告
                reports[i] = new SignalReport(returnsForDay, trades);
            }

            stopwatch.Stop();
            Console.WriteLine($"性能分析完成，有效信号数: {allReturnsOverTime.Count}，耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒。");

            // --- 第三步：将报告列表存入上下文 ---
            // 注意：现在存入的是 SignalReport[]
            context.SetArtifact(reports);
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
