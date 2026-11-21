using System;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Analysis.Analyzers;

/// <summary>
/// 第五层：独立 Alpha 策略信号前向收益分析器 (Signal Analyzer)。
/// 计算 T+1..T+N 前向收益分布、最优持有周期识别、双口径加权（样本等权 vs 月度时间等权）与分组切片归因。
/// 分析完成后输出只读不可变的 SignalStatistics 统计模型。
/// </summary>
public sealed class SignalAnalyzer : AnalyzerBase<SignalStatistics>
{
    /// <inheritdoc />
    public override string Name => "Signals";

    /// <inheritdoc />
    public override SignalStatistics? Analyze(
        TradingResult facts,
        AnalysisOptions options,
        ITimeseriesRegistry? timeseries)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(options);

        return Calculate(facts.Signals, timeseries, options);
    }

    private sealed record SignalSample(
        SignalRecord Signal,
        double[] ForwardReturns
    );

    /// <summary>
    /// 从策略信号记录集合与只读时序行情矩阵计算并构建信号前向评估统计对象。
    /// </summary>
    public static SignalStatistics? Calculate(
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

            int row = AnalysisHelper.ResolveDateIndex(metadata, sig.Time);
            if (row < 0 || row >= rowCount) continue;

            double p0 = matrix[row, col];
            if (double.IsNaN(p0) || p0 <= 0) continue;

            double direction = sig.SignalValue < 0 ? -1.0 : 1.0;
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
                        fwdReturns[k] = direction * ((pk - p0) / p0);
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
            var groups = new Dictionary<string, List<SignalRecord>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < signals.Count; i++)
            {
                var grp = signals[i].SignalGroup;
                if (string.IsNullOrWhiteSpace(grp) || string.Equals(grp, "default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!groups.TryGetValue(grp, out var list))
                {
                    list = new List<SignalRecord>();
                    groups[grp] = list;
                }
                list.Add(signals[i]);
            }

            foreach (var kvp in groups)
            {
                var subMetric = CalculateCore(kvp.Value, timeseries, options, isRoot: false);
                if (subMetric != null)
                {
                    groupBreakdowns[kvp.Key] = subMetric;
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
        double medianReturn = AnalysisHelper.CalculateMedian(returns);

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
        int mCount = monthlyPerfs.Count;
        if (mCount == 0)
        {
            return new SignalSampleStatistics(0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
        }

        double sumWinRate = 0.0;
        double sumReturn = 0.0;
        double sumMedian = 0.0;

        int winMonthCount = 0;
        double sumWin = 0.0;

        int lossMonthCount = 0;
        double sumLoss = 0.0;

        for (int i = 0; i < mCount; i++)
        {
            var p = monthlyPerfs[i];
            sumWinRate += p.WinRate;
            sumReturn += p.AvgReturn;
            sumMedian += p.MedianReturn;

            if (p.AvgWin > 0)
            {
                winMonthCount++;
                sumWin += p.AvgWin;
            }

            if (p.AvgLoss < 0)
            {
                lossMonthCount++;
                sumLoss += p.AvgLoss;
            }
        }

        double avgWinRate = sumWinRate / mCount;
        double avgReturn = sumReturn / mCount;
        double avgMedian = sumMedian / mCount;

        double avgWin = winMonthCount > 0 ? sumWin / winMonthCount : 0.0;
        double avgLoss = lossMonthCount > 0 ? sumLoss / lossMonthCount : 0.0;

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
}
