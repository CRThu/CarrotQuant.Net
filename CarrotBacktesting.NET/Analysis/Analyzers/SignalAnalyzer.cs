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
    /// 负责计算信号的未来N期收益率，并生成一份包含详细数据和统计摘要的性能报告。
    /// </summary>
    public class SignalAnalyzer : IAnalyzer
    {
        public string Name => nameof(SignalAnalyzer);
        private readonly int _backtestDays;

        /// <summary>
        /// 构造函数，从配置中读取回测天数。
        /// </summary>
        public SignalAnalyzer(AnalysisConfig config)
        {
            _backtestDays = config.SignalAnalysisDays;
        }

        /// <summary>
        /// 执行分析的核心方法。
        /// </summary>
        public void Analyze(AnalysisContext context)
        {
            var signals = context.BacktestResult.SignalsResult.GetSignals().ToList();
            if (signals.Count == 0)
            {
                Console.WriteLine("没有信号可供分析。");
                return;
            }

            // --- 第一步：计算详细的收益率矩阵 (原 ForwardReturnsAnalyzer 的工作) ---
            Console.WriteLine($"开始为 {signals.Count} 个信号计算未来 {_backtestDays} 日的性能表现...");
            var stopwatch = Stopwatch.StartNew();

            var allReturnsOverTime = new List<double[]>(signals.Count);

            // 目前只支持最高效的纵向数据模式
            if (context.Data is HistoryStorage hs)
            {
                foreach (var signal in signals)
                {
                    if (hs.StockHistories.TryGetValue(signal.StockCode, out var history))
                    {
                        var returns = CalculateReturnsForSignal(signal, history);
                        if (returns != null)
                            allReturnsOverTime.Add(returns);
                    }
                }
            }
            else
            {
                throw new NotImplementedException("性能分析器当前仅在 TimeSeries 存储模式下高效运行。");
            }

            // --- 第二步：创建统一的性能报告 (原 SummaryAnalyzer 的工作被移入其构造函数) ---
            var report = new SignalReport(allReturnsOverTime, _backtestDays);

            stopwatch.Stop();
            Console.WriteLine($"性能分析完成，耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒。");

            // --- 第三步：将最终的统一报告存入上下文 ---
            context.SetArtifact(report);
        }

        /// <summary>
        /// 为单个信号计算其未来N天的收益率序列。
        /// </summary>
        private double[]? CalculateReturnsForSignal(SignalInfo signal, StockHistory history)
        {
            // 使用二分查找定位信号日
            int signalIndex = history.Dates.ToList().BinarySearch(signal.Date);
            if (signalIndex < 0) return null;

            // 检查是否有足够的数据
            if (signalIndex + _backtestDays >= history.Data.Count) return null;

            double closeT0 = history.Data[signalIndex].Close;
            if (closeT0 <= 0) return null;

            var returns = new double[_backtestDays];
            for (int i = 0; i < _backtestDays; i++)
            {
                double closeTn = history.Data[signalIndex + 1 + i].Close;
                returns[i] = (closeTn - closeT0) / closeT0;
            }
            return returns;
        }
    }
}
