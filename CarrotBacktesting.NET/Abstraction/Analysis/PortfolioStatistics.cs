using System;
using System.Collections.Generic;
using System.Linq;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 自然月度收益率统计。
/// </summary>
/// <param name="Year">自然年份。</param>
/// <param name="Month">自然月份 (1-12)。</param>
/// <param name="ReturnRate">当月净值收益率。</param>
public readonly record struct MonthlyReturn(int Year, int Month, double ReturnRate);

/// <summary>
/// 第一层：组合净值与宏观风险统计模型 (Portfolio Statistics)。
/// 包含收益、波动率、最大回撤、夏普、索提诺、卡玛、月度收益矩阵及基准相对表现。
/// 内置静态工厂方法 Create，纯函数式高吞吐构建。
/// </summary>
public sealed record PortfolioStatistics(
    double TotalReturn,
    double AnnualizedReturn,
    double AnnualizedVolatility,
    double DownsideVolatility,
    double MaxDrawdown,
    DateTime MaxDrawdownPeakTime,
    DateTime MaxDrawdownTroughTime,
    DateTime? MaxDrawdownRecoveryTime,
    int MaxDrawdownDurationSteps,
    double SharpeRatio,
    double SortinoRatio,
    double CalmarRatio,
    double DailyVaR95,
    double DailyCVaR95,
    double? BenchmarkReturn,
    double? Alpha,
    double? Beta,
    double? InformationRatio,
    IReadOnlyList<MonthlyReturn> MonthlyReturns
)
{
    /// <summary>
    /// 从净值时间序列与日终快照构建组合宏观绩效统计对象。
    /// </summary>
    public static PortfolioStatistics Create(
        IReadOnlyList<EquityPoint> equityCurve,
        IReadOnlyList<DailySnapshot> dailySnapshots,
        AnalysisOptions options,
        IReadOnlyList<double>? benchmarkReturns = null)
    {
        ArgumentNullException.ThrowIfNull(equityCurve);
        ArgumentNullException.ThrowIfNull(options);

        // 稳健支持：若 equityCurve 为空但传入了 dailySnapshots，自动合成时间步净值
        if (equityCurve.Count == 0)
        {
            if (dailySnapshots != null && dailySnapshots.Count > 0)
            {
                var synth = new List<EquityPoint>(dailySnapshots.Count);
                for (int i = 0; i < dailySnapshots.Count; i++)
                {
                    var s = dailySnapshots[i];
                    synth.Add(new EquityPoint(s.Time, s.TotalEquity, s.AvailableCash, s.FrozenCash, s.OccupiedMargin, s.TotalMarketValue, s.UnrealizedPnL));
                }
                equityCurve = synth;
            }
            else
            {
                return CreateEmpty();
            }
        }

        // 1. 基础起止资产与收益
        int annualTradingDays = options.AnnualTradingDays > 0 ? options.AnnualTradingDays : 252;
        var startPoint = equityCurve[0];
        var endPoint = equityCurve[^1];
        double startEquity = startPoint.TotalEquity;
        double endEquity = endPoint.TotalEquity;
        double totalReturn = startEquity > 0 ? (endEquity - startEquity) / startEquity : 0.0;

        double totalDays = (endPoint.Time - startPoint.Time).TotalDays;
        double years = totalDays > 0 ? totalDays / 365.25 : (double)equityCurve.Count / annualTradingDays;
        if (years <= 0) years = 1.0 / annualTradingDays;

        double annualizedReturn = (startEquity > 0 && endEquity > 0 && years > 0)
            ? Math.Pow(endEquity / startEquity, 1.0 / years) - 1.0
            : (endEquity <= 0 ? -1.0 : 0.0);

        // 2. 提取周期收益率序列（优先使用日终快照，次选时间步净值）
        var returns = ExtractPeriodicReturns(dailySnapshots, equityCurve);

        // 3. 统计波动率与下行风险
        double annualizedVol = 0.0;
        double downsideVol = 0.0;
        double dailyVaR95 = 0.0;
        double dailyCVaR95 = 0.0;

        if (returns.Length > 0)
        {
            double meanReturn = returns.Average();
            if (returns.Length > 1)
            {
                double sumSq = 0.0;
                for (int i = 0; i < returns.Length; i++)
                {
                    double diff = returns[i] - meanReturn;
                    sumSq += diff * diff;
                }
                double sampleVar = sumSq / (returns.Length - 1);
                annualizedVol = Math.Sqrt(sampleVar) * Math.Sqrt(annualTradingDays);
            }

            // 下行半方差
            double rfDaily = options.RiskFreeRate / annualTradingDays;
            double downsideSumSq = 0.0;
            for (int i = 0; i < returns.Length; i++)
            {
                double under = Math.Min(0.0, returns[i] - rfDaily);
                downsideSumSq += under * under;
            }
            double downsideVar = downsideSumSq / returns.Length;
            downsideVol = Math.Sqrt(downsideVar) * Math.Sqrt(annualTradingDays);

            // 历史分位数 VaR95 与 CVaR95
            var sorted = (double[])returns.Clone();
            Array.Sort(sorted);
            int varIndex = Math.Clamp((int)Math.Floor(0.05 * sorted.Length), 0, sorted.Length - 1);
            dailyVaR95 = -sorted[varIndex];

            double cvarSum = 0.0;
            for (int i = 0; i <= varIndex; i++)
            {
                cvarSum += sorted[i];
            }
            dailyCVaR95 = -cvarSum / (varIndex + 1);
        }

        // 4. 最大回撤及其关键时间节点 (Max Drawdown, Peak, Trough, Recovery)
        var (maxDD, peakTime, troughTime, recoveryTime, durationSteps) = CalculateDrawdown(equityCurve);

        // 5. 风险调整收益比率
        double sharpe = annualizedVol > 1e-9 ? (annualizedReturn - options.RiskFreeRate) / annualizedVol : 0.0;
        double sortino = downsideVol > 1e-9 ? (annualizedReturn - options.RiskFreeRate) / downsideVol : 0.0;
        double calmar = maxDD > 1e-9 ? annualizedReturn / maxDD : 0.0;

        // 6. 基准对比（可选）
        double? benchmarkReturn = null;
        double? alpha = null;
        double? beta = null;
        double? infoRatio = null;

        if (benchmarkReturns != null && benchmarkReturns.Count == returns.Length && returns.Length > 1)
        {
            var (bRet, a, b, ir) = CalculateBenchmarkStats(returns, benchmarkReturns, options, annualizedReturn, years, annualTradingDays);
            benchmarkReturn = bRet;
            alpha = a;
            beta = b;
            infoRatio = ir;
        }

        // 7. 自然月度收益率矩阵
        var monthlyReturns = CalculateMonthlyReturns(dailySnapshots, equityCurve);

        return new PortfolioStatistics(
            TotalReturn: totalReturn,
            AnnualizedReturn: annualizedReturn,
            AnnualizedVolatility: annualizedVol,
            DownsideVolatility: downsideVol,
            MaxDrawdown: maxDD,
            MaxDrawdownPeakTime: peakTime,
            MaxDrawdownTroughTime: troughTime,
            MaxDrawdownRecoveryTime: recoveryTime,
            MaxDrawdownDurationSteps: durationSteps,
            SharpeRatio: sharpe,
            SortinoRatio: sortino,
            CalmarRatio: calmar,
            DailyVaR95: dailyVaR95,
            DailyCVaR95: dailyCVaR95,
            BenchmarkReturn: benchmarkReturn,
            Alpha: alpha,
            Beta: beta,
            InformationRatio: infoRatio,
            MonthlyReturns: monthlyReturns
        );
    }

    private static double[] ExtractPeriodicReturns(
        IReadOnlyList<DailySnapshot>? dailySnapshots,
        IReadOnlyList<EquityPoint> equityCurve)
    {
        if (dailySnapshots != null && dailySnapshots.Count > 1)
        {
            var res = new double[dailySnapshots.Count - 1];
            for (int i = 1; i < dailySnapshots.Count; i++)
            {
                double prev = dailySnapshots[i - 1].TotalEquity;
                double curr = dailySnapshots[i].TotalEquity;
                res[i - 1] = prev > 0 ? (curr - prev) / prev : 0.0;
            }
            return res;
        }

        if (equityCurve != null && equityCurve.Count > 1)
        {
            var res = new double[equityCurve.Count - 1];
            for (int i = 1; i < equityCurve.Count; i++)
            {
                double prev = equityCurve[i - 1].TotalEquity;
                double curr = equityCurve[i].TotalEquity;
                res[i - 1] = prev > 0 ? (curr - prev) / prev : 0.0;
            }
            return res;
        }

        return Array.Empty<double>();
    }

    private static (double MaxDD, DateTime PeakTime, DateTime TroughTime, DateTime? RecoveryTime, int DurationSteps) CalculateDrawdown(
        IReadOnlyList<EquityPoint> equityCurve)
    {
        double maxDD = 0.0;
        int peakIndex = 0;
        int maxDDPeakIndex = 0;
        int maxDDTroughIndex = 0;
        double runningPeak = equityCurve[0].TotalEquity;

        for (int i = 0; i < equityCurve.Count; i++)
        {
            double eq = equityCurve[i].TotalEquity;
            if (eq > runningPeak)
            {
                runningPeak = eq;
                peakIndex = i;
            }
            else if (runningPeak > 0)
            {
                double dd = (runningPeak - eq) / runningPeak;
                if (dd > maxDD)
                {
                    maxDD = dd;
                    maxDDPeakIndex = peakIndex;
                    maxDDTroughIndex = i;
                }
            }
        }

        DateTime peakTime = equityCurve[maxDDPeakIndex].Time;
        DateTime troughTime = equityCurve[maxDDTroughIndex].Time;
        DateTime? recoveryTime = null;
        int recoveryIndex = -1;

        if (maxDD > 1e-9)
        {
            double targetPeak = equityCurve[maxDDPeakIndex].TotalEquity;
            for (int i = maxDDTroughIndex + 1; i < equityCurve.Count; i++)
            {
                if (equityCurve[i].TotalEquity >= targetPeak)
                {
                    recoveryTime = equityCurve[i].Time;
                    recoveryIndex = i;
                    break;
                }
            }
        }

        // 业务守恒：当无回撤时持续步数严格为 0；有回撤但未修复时，按从峰值到最新步计算
        int durationSteps = maxDD <= 1e-9
            ? 0
            : (recoveryIndex >= 0 ? recoveryIndex - maxDDPeakIndex : equityCurve.Count - 1 - maxDDPeakIndex);

        return (maxDD, peakTime, troughTime, recoveryTime, Math.Max(0, durationSteps));
    }

    private static (double BenchmarkReturn, double Alpha, double Beta, double InfoRatio) CalculateBenchmarkStats(
        double[] returns,
        IReadOnlyList<double> bReturns,
        AnalysisOptions options,
        double strategyCagr,
        double years,
        int annualTradingDays)
    {
        int n = returns.Length;
        double meanS = returns.Average();
        double meanB = bReturns.Average();

        double cov = 0.0;
        double varB = 0.0;
        double totalBProd = 1.0;
        var activeDiffs = new double[n];
        double sumActiveDiff = 0.0;

        for (int i = 0; i < n; i++)
        {
            double ds = returns[i] - meanS;
            double db = bReturns[i] - meanB;
            cov += ds * db;
            varB += db * db;

            double diff = returns[i] - bReturns[i];
            activeDiffs[i] = diff;
            sumActiveDiff += diff;
            totalBProd *= (1.0 + bReturns[i]);
        }

        double beta = varB > 1e-12 ? cov / varB : 0.0;
        double benchmarkTotalReturn = totalBProd - 1.0;
        double benchmarkCagr = years > 0 && benchmarkTotalReturn > -1.0 ? Math.Pow(1.0 + benchmarkTotalReturn, 1.0 / years) - 1.0 : benchmarkTotalReturn;

        double alpha = strategyCagr - (options.RiskFreeRate + beta * (benchmarkCagr - options.RiskFreeRate));

        // 跟踪误差采用主动超额收益的样本标准差 (Sample StdDev)
        double meanActive = sumActiveDiff / n;
        double sumActiveSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            double d = activeDiffs[i] - meanActive;
            sumActiveSq += d * d;
        }
        double trackingError = Math.Sqrt(sumActiveSq / (n - 1)) * Math.Sqrt(annualTradingDays);
        double infoRatio = trackingError > 1e-9 ? (strategyCagr - benchmarkCagr) / trackingError : 0.0;

        return (benchmarkTotalReturn, alpha, beta, infoRatio);
    }

    private static IReadOnlyList<MonthlyReturn> CalculateMonthlyReturns(
        IReadOnlyList<DailySnapshot>? dailySnapshots,
        IReadOnlyList<EquityPoint> equityCurve)
    {
        var list = new List<MonthlyReturn>();

        if (dailySnapshots != null && dailySnapshots.Count > 0)
        {
            var groups = dailySnapshots.GroupBy(s => new { s.Time.Year, s.Time.Month })
                                       .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);

            double prevMonthEndEquity = dailySnapshots[0].TotalEquity;

            foreach (var group in groups)
            {
                var monthPoints = group.OrderBy(p => p.Time).ToList();
                double monthStart = prevMonthEndEquity > 0 ? prevMonthEndEquity : monthPoints[0].TotalEquity;
                double monthEnd = monthPoints[^1].TotalEquity;
                double ret = monthStart > 0 ? (monthEnd - monthStart) / monthStart : 0.0;
                list.Add(new MonthlyReturn(group.Key.Year, group.Key.Month, ret));
                prevMonthEndEquity = monthEnd;
            }
            return list;
        }

        if (equityCurve != null && equityCurve.Count > 0)
        {
            var groups = equityCurve.GroupBy(p => new { p.Time.Year, p.Time.Month })
                                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);

            double prevMonthEndEquity = equityCurve[0].TotalEquity;

            foreach (var group in groups)
            {
                var monthPoints = group.OrderBy(p => p.Time).ToList();
                double monthStart = prevMonthEndEquity > 0 ? prevMonthEndEquity : monthPoints[0].TotalEquity;
                double monthEnd = monthPoints[^1].TotalEquity;
                double ret = monthStart > 0 ? (monthEnd - monthStart) / monthStart : 0.0;
                list.Add(new MonthlyReturn(group.Key.Year, group.Key.Month, ret));
                prevMonthEndEquity = monthEnd;
            }
        }

        return list;
    }

    private static PortfolioStatistics CreateEmpty()
    {
        return new PortfolioStatistics(
            TotalReturn: 0.0,
            AnnualizedReturn: 0.0,
            AnnualizedVolatility: 0.0,
            DownsideVolatility: 0.0,
            MaxDrawdown: 0.0,
            MaxDrawdownPeakTime: DateTime.MinValue,
            MaxDrawdownTroughTime: DateTime.MinValue,
            MaxDrawdownRecoveryTime: null,
            MaxDrawdownDurationSteps: 0,
            SharpeRatio: 0.0,
            SortinoRatio: 0.0,
            CalmarRatio: 0.0,
            DailyVaR95: 0.0,
            DailyCVaR95: 0.0,
            BenchmarkReturn: null,
            Alpha: null,
            Beta: null,
            InformationRatio: null,
            MonthlyReturns: Array.Empty<MonthlyReturn>()
        );
    }
}
