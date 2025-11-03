using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Analyzers
{
    /// <summary>
    /// 计算信号未来N期收益率的分析器
    /// </summary>
    public class ForwardReturnsAnalyzer : IAnalyzer
    {
        public string Name => "ForwardReturnsAnalyzer";
        private readonly int _backtestDays;

        public ForwardReturnsAnalyzer(ForwardReturnsConfig config)
        {
            _backtestDays = config.BacktestDays;
        }

        public void Analyze(AnalysisContext context)
        {
            var signals = context.BacktestResult.SignalsResult.Signals.ToList();
            if (signals.Count == 0)
            {
                Console.WriteLine("没有信号可供分析。");
                return;
            }

            Console.WriteLine($"开始计算 {signals.Count} 个信号的未来 {_backtestDays} 日收益率...");
            var stopwatch = Stopwatch.StartNew();

            var allReturnsOverTime = new List<double[]>(signals.Count);

            if (context.Data is HistoryStorage hs)
            {
                // 纵向数据模式，效率最高
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
                // 横向数据模式，需要为每个信号构建临时时间序列
                // (此处省略实现，推荐使用TimeSeries模式以获得最佳分析性能)
                Console.WriteLine("警告: 在MarketSnapshot模式下进行收益率分析效率较低。");
            }

            stopwatch.Stop();
            Console.WriteLine($"收益率计算完成，耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒。");

            // 将结果存入上下文，供后续分析器使用
            var result = new ForwardReturnsResult(allReturnsOverTime, _backtestDays);
            context.SetArtifact(result);
        }

        private double[]? CalculateReturnsForSignal(Result.SignalInfo signal, StockHistory history)
        {
            // 使用二分查找定位信号日，效率远高于线性扫描
            int signalIndex = history.Dates.ToList().BinarySearch(signal.Date);
            if (signalIndex < 0) return null; // 信号日当天数据不存在

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
