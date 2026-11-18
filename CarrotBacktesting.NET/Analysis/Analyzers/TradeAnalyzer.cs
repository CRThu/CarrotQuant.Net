using System;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Analysis.Analyzers;

/// <summary>
/// 第三层：交易行为与执行归因分析器 (Trade Analyzer)。
/// 计算交易笔数、胜率、多空收益分布、盈亏比、盈利因子、连胜连败、持仓周期、MFE 交易捕获效率与 Exit Timing 卖点反思。
/// 分析完成后输出只读不可变的 TradeStatistics 统计模型。
/// </summary>
public sealed class TradeAnalyzer : AnalyzerBase<TradeStatistics>
{
    /// <inheritdoc />
    public override string Name => "Trades";

    /// <inheritdoc />
    public override TradeStatistics? Analyze(
        TradingResult facts,
        AnalysisOptions options,
        ITimeseriesRegistry? timeseries)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(options);

        return Calculate(facts.ClosedTrades, options, timeseries);
    }

    /// <summary>
    /// 从已平仓交易集合计算并构建交易行为与执行统计对象。
    /// </summary>
    public static TradeStatistics Calculate(
        IReadOnlyList<Trade> trades,
        AnalysisOptions options,
        ITimeseriesRegistry? timeseries = null)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(options);

        if (trades.Count == 0)
        {
            return CreateEmpty();
        }

        return CalculateCore(trades, options, timeseries, isRoot: true);
    }

    private static TradeStatistics CalculateCore(
        IReadOnlyList<Trade> trades,
        AnalysisOptions options,
        ITimeseriesRegistry? timeseries,
        bool isRoot)
    {
        int totalTrades = trades.Count;
        int winCount = 0;
        int lossCount = 0;
        int tieCount = 0;

        int longCount = 0;
        int longWinCount = 0;
        int shortCount = 0;
        int shortWinCount = 0;

        double sumReturn = 0.0;
        double sumWinReturn = 0.0;
        double sumLossReturn = 0.0;

        double grossProfit = 0.0;
        double grossLoss = 0.0;

        double sumHolding = 0.0;
        double sumWinHolding = 0.0;
        double sumLossHolding = 0.0;

        var returnRates = new double[totalTrades];

        for (int i = 0; i < totalTrades; i++)
        {
            var t = trades[i];
            double pnl = t.RealizedPnL;
            double ret = t.ReturnRate;
            returnRates[i] = ret;
            sumReturn += ret;
            sumHolding += t.HoldingSteps;

            if (t.Side == TradeSide.Long)
            {
                longCount++;
                if (pnl > 0) longWinCount++;
            }
            else
            {
                shortCount++;
                if (pnl > 0) shortWinCount++;
            }

            if (pnl > 0)
            {
                winCount++;
                sumWinReturn += ret;
                grossProfit += pnl;
                sumWinHolding += t.HoldingSteps;
            }
            else if (pnl < 0)
            {
                lossCount++;
                sumLossReturn += ret;
                grossLoss += Math.Abs(pnl);
                sumLossHolding += t.HoldingSteps;
            }
            else
            {
                tieCount++;
            }
        }

        double winRate = (double)winCount / totalTrades;
        double longWinRate = longCount > 0 ? (double)longWinCount / longCount : 0.0;
        double shortWinRate = shortCount > 0 ? (double)shortWinCount / shortCount : 0.0;

        double avgReturn = sumReturn / totalTrades;
        double avgWinReturn = winCount > 0 ? sumWinReturn / winCount : 0.0;
        double avgLossReturn = lossCount > 0 ? sumLossReturn / lossCount : 0.0;

        // 全胜场景下盈亏比与盈利因子安全封顶 999.0
        double winLossRatio = Math.Abs(avgLossReturn) > 1e-9
            ? avgWinReturn / Math.Abs(avgLossReturn)
            : (winCount > 0 ? 999.0 : 0.0);

        double profitFactor = grossLoss > 1e-9
            ? grossProfit / grossLoss
            : (grossProfit > 0 ? 999.0 : 0.0);

        // 中位数收益率
        Array.Sort(returnRates);
        double medianReturn = CalculateMedian(returnRates);

        // 连续盈亏
        var (maxConsWins, maxConsLosses) = CalculateConsecutiveStats(trades);

        // 持仓时长
        double avgHolding = sumHolding / totalTrades;
        double avgWinHolding = winCount > 0 ? sumWinHolding / winCount : 0.0;
        double avgLossHolding = lossCount > 0 ? sumLossHolding / lossCount : 0.0;

        // 卖点反思 (Exit Timing) 与交易效率 (MFE)
        ExitTimingStatistics? exitTiming = null;
        double? avgEfficiency = null;
        double? medianEfficiency = null;

        if (timeseries != null && isRoot)
        {
            if (options.ExitTimingSteps > 0)
            {
                exitTiming = CalculateExitTiming(trades, timeseries, options.ExitTimingSteps, options.PriceFieldName);
            }
            var (avgEff, medEff) = CalculateTradeEfficiency(trades, timeseries, options.PriceFieldName);
            avgEfficiency = avgEff;
            medianEfficiency = medEff;
        }

        // 分组标签下钻分析 (Tag Breakdown)
        var tagBreakdowns = new Dictionary<string, TradeStatistics>(StringComparer.OrdinalIgnoreCase);
        if (isRoot)
        {
            var groups = new Dictionary<string, List<Trade>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < trades.Count; i++)
            {
                var tag = trades[i].EntryTag;
                if (string.IsNullOrWhiteSpace(tag) || string.Equals(tag, "default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!groups.TryGetValue(tag, out var groupList))
                {
                    groupList = new List<Trade>();
                    groups[tag] = groupList;
                }
                groupList.Add(trades[i]);
            }

            foreach (var kvp in groups)
            {
                tagBreakdowns[kvp.Key] = CalculateCore(kvp.Value, options, timeseries, isRoot: false);
            }
        }

        return new TradeStatistics(
            TotalTrades: totalTrades,
            WinningTrades: winCount,
            LosingTrades: lossCount,
            BreakevenTrades: tieCount,
            WinRate: winRate,
            LongTrades: longCount,
            LongWinningTrades: longWinCount,
            LongWinRate: longWinRate,
            ShortTrades: shortCount,
            ShortWinningTrades: shortWinCount,
            ShortWinRate: shortWinRate,
            AverageReturn: avgReturn,
            MedianReturn: medianReturn,
            AverageWinReturn: avgWinReturn,
            AverageLossReturn: avgLossReturn,
            WinLossRatio: winLossRatio,
            ProfitFactor: profitFactor,
            MaxConsecutiveWins: maxConsWins,
            MaxConsecutiveLosses: maxConsLosses,
            AverageHoldingSteps: avgHolding,
            AverageWinHoldingSteps: avgWinHolding,
            AverageLossHoldingSteps: avgLossHolding,
            AverageTradeEfficiency: avgEfficiency,
            MedianTradeEfficiency: medianEfficiency,
            ExitTiming: exitTiming,
            TagBreakdowns: tagBreakdowns
        );
    }

    private static (int MaxWins, int MaxLosses) CalculateConsecutiveStats(IReadOnlyList<Trade> trades)
    {
        int maxWins = 0;
        int maxLosses = 0;
        int currentWins = 0;
        int currentLosses = 0;

        for (int i = 0; i < trades.Count; i++)
        {
            double pnl = trades[i].RealizedPnL;
            if (pnl > 0)
            {
                currentWins++;
                currentLosses = 0;
                if (currentWins > maxWins) maxWins = currentWins;
            }
            else if (pnl < 0)
            {
                currentLosses++;
                currentWins = 0;
                if (currentLosses > maxLosses) maxLosses = currentLosses;
            }
            else
            {
                currentWins = 0;
                currentLosses = 0;
            }
        }

        return (maxWins, maxLosses);
    }

    private static double CalculateMedian(double[] sortedArray)
    {
        if (sortedArray.Length == 0) return 0.0;
        int mid = sortedArray.Length / 2;
        if (sortedArray.Length % 2 != 0)
        {
            return sortedArray[mid];
        }
        return (sortedArray[mid - 1] + sortedArray[mid]) / 2.0;
    }

    private static int ResolveDateIndex(IMarketMetadata metadata, DateTime time)
    {
        int idx = metadata.GetDateIndex(time);
        if (idx >= 0) return idx;
        return metadata.GetDateIndex(time.Date);
    }

    private static ExitTimingStatistics? CalculateExitTiming(
        IReadOnlyList<Trade> trades,
        ITimeseriesRegistry timeseries,
        int steps,
        string priceField)
    {
        if (!timeseries.HasField(priceField)) return null;

        var matrix = timeseries.GetMatrix<double>(priceField);
        var metadata = timeseries.Metadata;
        int totalRows = Math.Min(metadata.TradeDates.Count, matrix.RowCount);

        var allStepReturns = new List<double>[steps];
        for (int k = 0; k < steps; k++)
        {
            allStepReturns[k] = new List<double>(trades.Count);
        }

        int validCount = 0;

        for (int i = 0; i < trades.Count; i++)
        {
            var trade = trades[i];
            int col = metadata.GetSymbolIndex(trade.Symbol);
            if (col < 0) continue;

            int row = ResolveDateIndex(metadata, trade.ExitTime);
            if (row < 0 || row + steps >= totalRows) continue;

            double p0 = matrix[row, col];
            if (double.IsNaN(p0) || p0 <= 0) continue;

            validCount++;
            for (int k = 0; k < steps; k++)
            {
                double pk = matrix[row + 1 + k, col];
                if (double.IsNaN(pk) || pk <= 0) continue;

                double ret = trade.Side == TradeSide.Long
                    ? (pk - p0) / p0
                    : (p0 - pk) / p0;

                allStepReturns[k].Add(ret);
            }
        }

        if (validCount == 0) return null;

        var avgReturns = new double[steps];
        var medianReturns = new double[steps];
        var winRates = new double[steps];

        for (int k = 0; k < steps; k++)
        {
            var list = allStepReturns[k];
            int listCount = list.Count;
            if (listCount > 0)
            {
                double sumRet = 0.0;
                int posCount = 0;
                var arr = new double[listCount];
                for (int m = 0; m < listCount; m++)
                {
                    double r = list[m];
                    arr[m] = r;
                    sumRet += r;
                    if (r > 0) posCount++;
                }
                avgReturns[k] = sumRet / listCount;
                Array.Sort(arr);
                medianReturns[k] = CalculateMedian(arr);
                winRates[k] = (double)posCount / listCount;
            }
        }

        return new ExitTimingStatistics(
            HorizonSteps: steps,
            ValidTradesCount: validCount,
            AverageReturns: avgReturns,
            MedianReturns: medianReturns,
            WinRates: winRates
        );
    }

    private static (double? Avg, double? Median) CalculateTradeEfficiency(
        IReadOnlyList<Trade> trades,
        ITimeseriesRegistry timeseries,
        string priceField)
    {
        if (!timeseries.HasField(priceField)) return (null, null);

        var matrix = timeseries.GetMatrix<double>(priceField);
        var metadata = timeseries.Metadata;
        int totalRows = Math.Min(metadata.TradeDates.Count, matrix.RowCount);
        var efficiencies = new List<double>();

        for (int i = 0; i < trades.Count; i++)
        {
            var trade = trades[i];
            if (trade.RealizedPnL <= 0) continue; // 仅对盈利交易评估卖点捕获效率

            int col = metadata.GetSymbolIndex(trade.Symbol);
            if (col < 0) continue;

            int entryRow = ResolveDateIndex(metadata, trade.EntryTime);
            int exitRow = ResolveDateIndex(metadata, trade.ExitTime);
            if (entryRow < 0 || exitRow < 0 || exitRow < entryRow || exitRow >= totalRows) continue;

            double maxPotentialProfit;
            if (trade.Side == TradeSide.Long)
            {
                double maxPrice = trade.EntryPrice;
                for (int r = entryRow; r <= exitRow; r++)
                {
                    double p = matrix[r, col];
                    if (!double.IsNaN(p) && p > maxPrice) maxPrice = p;
                }
                maxPotentialProfit = (maxPrice - trade.EntryPrice) * (double)trade.Quantity * trade.Multiplier;
            }
            else
            {
                // 空头交易：盈利来自于下跌，潜在最大收益基于区间最低价 minPrice
                double minPrice = trade.EntryPrice;
                for (int r = entryRow; r <= exitRow; r++)
                {
                    double p = matrix[r, col];
                    if (!double.IsNaN(p) && p > 0 && p < minPrice) minPrice = p;
                }
                maxPotentialProfit = (trade.EntryPrice - minPrice) * (double)trade.Quantity * trade.Multiplier;
            }

            if (maxPotentialProfit > 1e-4)
            {
                double eff = Math.Clamp(trade.RealizedPnL / maxPotentialProfit, 0.0, 1.0);
                efficiencies.Add(eff);
            }
        }

        int effCount = efficiencies.Count;
        if (effCount == 0) return (null, null);

        double effSum = 0.0;
        var effArr = new double[effCount];
        for (int i = 0; i < effCount; i++)
        {
            double v = efficiencies[i];
            effArr[i] = v;
            effSum += v;
        }
        double avg = effSum / effCount;
        Array.Sort(effArr);
        double med = CalculateMedian(effArr);

        return (avg, med);
    }

    private static TradeStatistics CreateEmpty()
    {
        return new TradeStatistics(
            TotalTrades: 0,
            WinningTrades: 0,
            LosingTrades: 0,
            BreakevenTrades: 0,
            WinRate: 0.0,
            LongTrades: 0,
            LongWinningTrades: 0,
            LongWinRate: 0.0,
            ShortTrades: 0,
            ShortWinningTrades: 0,
            ShortWinRate: 0.0,
            AverageReturn: 0.0,
            MedianReturn: 0.0,
            AverageWinReturn: 0.0,
            AverageLossReturn: 0.0,
            WinLossRatio: 0.0,
            ProfitFactor: 0.0,
            MaxConsecutiveWins: 0,
            MaxConsecutiveLosses: 0,
            AverageHoldingSteps: 0.0,
            AverageWinHoldingSteps: 0.0,
            AverageLossHoldingSteps: 0.0,
            AverageTradeEfficiency: null,
            MedianTradeEfficiency: null,
            ExitTiming: null,
            TagBreakdowns: new Dictionary<string, TradeStatistics>(StringComparer.OrdinalIgnoreCase)
        );
    }
}

/// <summary>
/// 卖点时机反思统计（Exit Timing Stats）。
/// 评估已平仓标的在平仓后未来 T+1..T+N 步的后续涨跌幅分布，反思卖点是否过早或准确逃顶。
/// </summary>
/// <param name="HorizonSteps">分析的前向步数 N。</param>
/// <param name="ValidTradesCount">具备足够后续行情支撑的有效平仓笔数。</param>
/// <param name="AverageReturns">各步平均后续收益率。</param>
/// <param name="MedianReturns">各步中位数后续收益率。</param>
/// <param name="WinRates">各步后续上涨（收益率 > 0）概率。</param>
public sealed record ExitTimingStatistics(
    int HorizonSteps,
    int ValidTradesCount,
    IReadOnlyList<double> AverageReturns,
    IReadOnlyList<double> MedianReturns,
    IReadOnlyList<double> WinRates
);

/// <summary>
/// 第三层：交易行为与执行归因统计模型 (Trade Statistics)。
/// 统筹总交易笔数、多空胜率、盈亏比、盈利因子、连续盈亏、持仓步数、交易效率 (MFE) 与卖点反思 (Exit Timing)。
/// 纯数据 Record，实现统一的 IAnalysisResult 契约，支持自描述指标流。
/// </summary>
public sealed record TradeStatistics(
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    int BreakevenTrades,
    double WinRate,
    int LongTrades,
    int LongWinningTrades,
    double LongWinRate,
    int ShortTrades,
    int ShortWinningTrades,
    double ShortWinRate,
    double AverageReturn,
    double MedianReturn,
    double AverageWinReturn,
    double AverageLossReturn,
    double WinLossRatio,
    double ProfitFactor,
    int MaxConsecutiveWins,
    int MaxConsecutiveLosses,
    double AverageHoldingSteps,
    double AverageWinHoldingSteps,
    double AverageLossHoldingSteps,
    double? AverageTradeEfficiency,
    double? MedianTradeEfficiency,
    ExitTimingStatistics? ExitTiming,
    IReadOnlyDictionary<string, TradeStatistics> TagBreakdowns
) : IAnalysisResult
{
    /// <inheritdoc />
    public string Name => "trades";

    /// <inheritdoc />
    public string Title => "交易行为与执行归因";

    /// <inheritdoc />
    public IEnumerable<MetricItem> GetMetrics()
    {
        yield return new("总交易笔数 (Total Trades)", $"{TotalTrades}", "胜率分析");
        yield return new("盈利 / 亏损 / 保本笔数", $"{WinningTrades} / {LosingTrades} / {BreakevenTrades}", "胜率分析");
        yield return new("总体胜率 (Win Rate)", $"{WinRate:P2}", "胜率分析");
        yield return new("多头笔数 / 胜率", $"{LongTrades} / {LongWinRate:P2}", "多空拆分");
        yield return new("空头笔数 / 胜率", $"{ShortTrades} / {ShortWinRate:P2}", "多空拆分");
        yield return new("盈亏比 (Win/Loss Ratio)", $"{WinLossRatio:F2}", "盈亏质量");
        yield return new("盈利因子 (Profit Factor)", $"{ProfitFactor:F2}", "盈亏质量");
        yield return new("平均收益率 (Avg Return)", $"{AverageReturn:P2}", "收益分布");
        yield return new("中位数收益率 (Median Return)", $"{MedianReturn:P2}", "收益分布");
        yield return new("平均盈利幅度 (Avg Win)", $"{AverageWinReturn:P2}", "收益分布");
        yield return new("平均亏损幅度 (Avg Loss)", $"{AverageLossReturn:P2}", "收益分布");
        yield return new("最大连胜 / 连败", $"{MaxConsecutiveWins} / {MaxConsecutiveLosses}", "连胜连败");
        yield return new("平均持仓步数 (Avg Holding)", $"{AverageHoldingSteps:F1} 步", "持仓周期");
        yield return new("盈利/亏损持仓步数", $"{AverageWinHoldingSteps:F1} / {AverageLossHoldingSteps:F1} 步", "持仓周期");

        if (AverageTradeEfficiency.HasValue)
            yield return new("平均最大有利偏离捕获效率 (Avg Efficiency)", $"{AverageTradeEfficiency.Value:P2}", "执行效率");
        if (MedianTradeEfficiency.HasValue)
            yield return new("中位数最大有利偏离捕获效率 (Median Efficiency)", $"{MedianTradeEfficiency.Value:P2}", "执行效率");
    }

    /// <summary>
    /// 从已平仓交易集合构建交易行为与执行统计对象（转发至 TradeAnalyzer.Calculate）。
    /// </summary>
    public static TradeStatistics Create(
        IReadOnlyList<Trade> trades,
        AnalysisOptions options,
        ITimeseriesRegistry? timeseries = null)
        => TradeAnalyzer.Calculate(trades, options, timeseries);
}
