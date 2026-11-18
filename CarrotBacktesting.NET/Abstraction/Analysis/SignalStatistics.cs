using System;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 信号样本聚合统计模型 (Signal Sample Statistics)。
/// 统筹指定样本集合（全局样本或月度样本）的有效样本量、胜率、均值、中位数及盈亏比。
/// </summary>
/// <param name="SignalCount">有效样本数量。</param>
/// <param name="WinRate">胜率 (收益率 > 0 的占比)。</param>
/// <param name="AvgReturn">平均收益率。</param>
/// <param name="MedianReturn">中位数收益率。</param>
/// <param name="AvgWin">平均盈利。</param>
/// <param name="AvgLoss">平均亏损。</param>
/// <param name="WinLossRatio">盈亏比 (AvgWin / |AvgLoss|)。</param>
public readonly record struct SignalSampleStatistics(
    int SignalCount,
    double WinRate,
    double AvgReturn,
    double MedianReturn,
    double AvgWin,
    double AvgLoss,
    double WinLossRatio
);

/// <summary>
/// 单个前向周期（T+Step）的信号多维度评估统计模型 (Signal Horizon Statistics)。
/// </summary>
/// <param name="Step">前向步数/天数 (1..N)。</param>
/// <param name="SampleWeighted">全量样本等权表现 (Global Sample-Weighted)。</param>
/// <param name="TimeWeighted">时间等权表现 (月度均化，消除单月信号聚集偏差)。</param>
public sealed record SignalHorizonStatistics(
    int Step,
    SignalSampleStatistics SampleWeighted,
    SignalSampleStatistics TimeWeighted
);

/// <summary>
/// 独立 Alpha 信号前向质量统计模型 (Signal Statistics)。
/// 继承并升级 V3 SignalAnalyzer 核心算法，基于行情矩阵纳秒级提取前向 T+1..T+N 收益分布、双口径加权与最优持有期。
/// 内置静态工厂方法 Create，纯函数式高吞吐构建。
/// </summary>
public sealed record SignalStatistics(
    int TotalSignals,
    int HorizonSteps,
    IReadOnlyList<SignalHorizonStatistics> Horizons,
    int PeakHorizonStep,
    IReadOnlyDictionary<string, SignalStatistics> GroupBreakdowns
)
{
    private sealed record SignalSample(
        SignalRecord Signal,
        double[] ForwardReturns
    );

    /// <summary>
    /// 从策略信号记录集合与只读时序行情矩阵构建信号前向评估统计对象。
    /// </summary>
    public static SignalStatistics? Create(
        IReadOnlyList<SignalRecord> signals,
        ITimeseriesRegistry? timeseries,
        AnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(options);

        if (signals.Count == 0 || timeseries == null || options.SignalHorizonSteps <= 0)
        {
            return null;
        }

        if (!timeseries.HasField(options.PriceFieldName))
        {
            return null;
        }

        return CalculateCore(signals, timeseries, options, isRoot: true);
    }

    private static SignalStatistics? CalculateCore(
        IReadOnlyList<SignalRecord> signals,
        ITimeseriesRegistry timeseries,
        AnalysisOptions options,
        bool isRoot)
    {
        int horizonSteps = options.SignalHorizonSteps;
        var matrix = timeseries.GetMatrix<double>(options.PriceFieldName);
        var metadata = timeseries.Metadata;
        int rowCount = Math.Min(metadata.TradeDates.Count, matrix.RowCount);

        var validSamples = new List<SignalSample>(signals.Count);

        for (int i = 0; i < signals.Count; i++)
        {
            var sig = signals[i];
            int col = metadata.GetSymbolIndex(sig.Symbol);
            if (col < 0) continue;

            int row = metadata.GetDateIndex(sig.Time.Date);
            if (row < 0 || row >= rowCount) continue;

            double p0 = matrix[row, col];
            if (double.IsNaN(p0) || p0 <= 0) continue;

            var fwdReturns = new double[horizonSteps];
            bool hasValid = false;

            for (int k = 0; k < horizonSteps; k++)
            {
                int targetRow = row + 1 + k;
                if (targetRow < rowCount)
                {
                    double pk = matrix[targetRow, col];
                    if (!double.IsNaN(pk) && pk > 0)
                    {
                        fwdReturns[k] = (pk - p0) / p0;
                        hasValid = true;
                    }
                    else
                    {
                        fwdReturns[k] = double.NaN;
                    }
                }
                else
                {
                    fwdReturns[k] = double.NaN;
                }
            }

            if (hasValid)
            {
                validSamples.Add(new SignalSample(sig, fwdReturns));
            }
        }

        if (validSamples.Count == 0)
        {
            return null;
        }

        var horizons = new SignalHorizonStatistics[horizonSteps];
        int peakStep = 1;
        double maxAvgReturn = double.MinValue;

        for (int k = 0; k < horizonSteps; k++)
        {
            int stepIndex = k;
            int stepNumber = k + 1;

            // 单趟遍历，消除 LINQ 临时堆分配
            var stepReturns = new List<double>(validSamples.Count);
            var monthlyBuckets = new SortedDictionary<(int Year, int Month), List<double>>();

            for (int s = 0; s < validSamples.Count; s++)
            {
                var sample = validSamples[s];
                double r = sample.ForwardReturns[stepIndex];
                if (!double.IsNaN(r))
                {
                    stepReturns.Add(r);
                    var key = (sample.Signal.Time.Year, sample.Signal.Time.Month);
                    if (!monthlyBuckets.TryGetValue(key, out var list))
                    {
                        list = new List<double>();
                        monthlyBuckets[key] = list;
                    }
                    list.Add(r);
                }
            }

            var globalPerf = ComputePerf(stepReturns);

            var monthlyPerfList = new List<SignalSampleStatistics>(monthlyBuckets.Count);
            foreach (var kvp in monthlyBuckets)
            {
                monthlyPerfList.Add(ComputePerf(kvp.Value));
            }

            var timeWeightedPerf = ComputeTimeWeightedPerf(monthlyPerfList, globalPerf.SignalCount);

            horizons[k] = new SignalHorizonStatistics(
                Step: stepNumber,
                SampleWeighted: globalPerf,
                TimeWeighted: timeWeightedPerf
            );

            if (globalPerf.AvgReturn > maxAvgReturn && globalPerf.SignalCount > 0)
            {
                maxAvgReturn = globalPerf.AvgReturn;
                peakStep = stepNumber;
            }
        }

        // 分组下钻
        var groupBreakdowns = new Dictionary<string, SignalStatistics>(StringComparer.OrdinalIgnoreCase);
        if (isRoot)
        {
            var groups = signals.Where(s => !string.IsNullOrWhiteSpace(s.SignalGroup) && !string.Equals(s.SignalGroup, "default", StringComparison.OrdinalIgnoreCase))
                                .GroupBy(s => s.SignalGroup, StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups)
            {
                var subMetric = CalculateCore(g.ToList(), timeseries, options, isRoot: false);
                if (subMetric != null)
                {
                    groupBreakdowns[g.Key] = subMetric;
                }
            }
        }

        return new SignalStatistics(
            TotalSignals: validSamples.Count,
            HorizonSteps: horizonSteps,
            Horizons: horizons,
            PeakHorizonStep: peakStep,
            GroupBreakdowns: groupBreakdowns
        );
    }

    private static SignalSampleStatistics ComputePerf(List<double> returns)
    {
        int count = returns.Count;
        if (count == 0)
        {
            return new SignalSampleStatistics(0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
        }

        double sum = 0.0;
        int winCount = 0;
        double sumWin = 0.0;
        int lossCount = 0;
        double sumLoss = 0.0;

        for (int i = 0; i < count; i++)
        {
            double r = returns[i];
            sum += r;
            if (r > 0)
            {
                winCount++;
                sumWin += r;
            }
            else if (r < 0)
            {
                lossCount++;
                sumLoss += r;
            }
        }

        double winRate = (double)winCount / count;
        double avgReturn = sum / count;
        double avgWin = winCount > 0 ? sumWin / winCount : 0.0;
        double avgLoss = lossCount > 0 ? sumLoss / lossCount : 0.0;
        double winLossRatio = Math.Abs(avgLoss) > 1e-9
            ? avgWin / Math.Abs(avgLoss)
            : (winCount > 0 ? 999.0 : 0.0);

        returns.Sort();
        double medianReturn = CalculateMedian(returns);

        return new SignalSampleStatistics(
            SignalCount: count,
            WinRate: winRate,
            AvgReturn: avgReturn,
            MedianReturn: medianReturn,
            AvgWin: avgWin,
            AvgLoss: avgLoss,
            WinLossRatio: winLossRatio
        );
    }

    private static SignalSampleStatistics ComputeTimeWeightedPerf(List<SignalSampleStatistics> monthlyPerfs, int totalSignalCount)
    {
        if (monthlyPerfs.Count == 0)
        {
            return new SignalSampleStatistics(0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
        }

        double avgWinRate = monthlyPerfs.Average(p => p.WinRate);
        double avgReturn = monthlyPerfs.Average(p => p.AvgReturn);
        double avgMedian = monthlyPerfs.Average(p => p.MedianReturn);

        // 仅对存在盈利/亏损样本的月份均化，防止无胜/无负月份稀释平均幅度
        var winMonths = monthlyPerfs.Where(p => p.AvgWin > 0).ToList();
        double avgWin = winMonths.Count > 0 ? winMonths.Average(p => p.AvgWin) : 0.0;

        var lossMonths = monthlyPerfs.Where(p => p.AvgLoss < 0).ToList();
        double avgLoss = lossMonths.Count > 0 ? lossMonths.Average(p => p.AvgLoss) : 0.0;

        double winLossRatio = Math.Abs(avgLoss) > 1e-9
            ? avgWin / Math.Abs(avgLoss)
            : (avgWin > 0 ? 999.0 : 0.0);

        return new SignalSampleStatistics(
            SignalCount: totalSignalCount,
            WinRate: avgWinRate,
            AvgReturn: avgReturn,
            MedianReturn: avgMedian,
            AvgWin: avgWin,
            AvgLoss: avgLoss,
            WinLossRatio: winLossRatio
        );
    }

    private static double CalculateMedian(List<double> sorted)
    {
        if (sorted.Count == 0) return 0.0;
        int mid = sorted.Count / 2;
        if (sorted.Count % 2 != 0)
        {
            return sorted[mid];
        }
        return (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
