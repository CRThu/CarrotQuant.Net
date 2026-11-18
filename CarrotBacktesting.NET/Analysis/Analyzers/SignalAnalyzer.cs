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

    private static int ResolveDateIndex(IMarketMetadata metadata, DateTime time)
    {
        int idx = metadata.GetDateIndex(time);
        if (idx >= 0) return idx;
        return metadata.GetDateIndex(time.Date);
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

            int row = ResolveDateIndex(metadata, sig.Time);
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
/// 基于行情矩阵提取前向 T+1..T+N 收益分布、双口径加权与最优持有期。
/// 纯数据 Record，实现统一的 IAnalysisResult 契约，支持自描述指标流。
/// </summary>
public sealed record SignalStatistics(
    int TotalSignals,
    int HorizonSteps,
    IReadOnlyList<SignalHorizonStatistics> Horizons,
    int PeakHorizonStep,
    IReadOnlyDictionary<string, SignalStatistics> GroupBreakdowns
) : IAnalysisResult
{
    /// <inheritdoc />
    public string Name => "signals";

    /// <inheritdoc />
    public string Title => "独立 Alpha 信号前向收益评估";

    /// <inheritdoc />
    public IEnumerable<MetricItem> GetMetrics()
    {
        yield return new("总信号样本数 (Total Signals)", $"{TotalSignals}", "信号概况");
        yield return new("评估前向周期步数 (Horizon Steps)", $"{HorizonSteps} 步", "周期设置");
        yield return new("最优持有周期推荐 (Peak Horizon)", $"T+{PeakHorizonStep}", "最优决策");

        if (PeakHorizonStep > 0 && PeakHorizonStep <= Horizons.Count)
        {
            var best = Horizons[PeakHorizonStep - 1];
            yield return new($"T+{PeakHorizonStep} 样本等权胜率", $"{best.SampleWeighted.WinRate:P2}", "最优周期表现");
            yield return new($"T+{PeakHorizonStep} 样本等权平均收益", $"{best.SampleWeighted.AvgReturn:P2}", "最优周期表现");
            yield return new($"T+{PeakHorizonStep} 时间等权胜率", $"{best.TimeWeighted.WinRate:P2}", "最优周期表现");
            yield return new($"T+{PeakHorizonStep} 时间等权平均收益", $"{best.TimeWeighted.AvgReturn:P2}", "最优周期表现");
        }
    }

    /// <summary>
    /// 从策略信号记录集合与只读时序行情矩阵构建信号前向评估统计对象（转发至 SignalAnalyzer.Calculate）。
    /// </summary>
    public static SignalStatistics? Create(
        IReadOnlyList<SignalRecord> signals,
        ITimeseriesRegistry? timeseries,
        AnalysisOptions options)
        => SignalAnalyzer.Calculate(signals, timeseries, options);
}
