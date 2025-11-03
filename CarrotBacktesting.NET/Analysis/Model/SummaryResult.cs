using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Model
{
    /// <summary>
    /// 存储每日的聚合性能指标。
    /// </summary>
    public class SummaryResult
    {
        public int BacktestDays { get; }
        public int ValidSignalCount { get; }
        public double[] AvgReturns { get; }
        public double[] MedianReturns { get; }
        public double[] WinRates { get; }

        public SummaryResult(int backtestDays, int validSignalCount, double[] avgReturns, double[] medianReturns, double[] winRates)
        {
            BacktestDays = backtestDays;
            ValidSignalCount = validSignalCount;
            AvgReturns = avgReturns;
            MedianReturns = medianReturns;
            WinRates = winRates;
        }
    }
}
