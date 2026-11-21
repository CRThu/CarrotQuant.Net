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
        double medianReturn = AnalysisHelper.CalculateMedian(returnRates);

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

            int row = AnalysisHelper.ResolveDateIndex(metadata, trade.ExitTime);
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
                medianReturns[k] = AnalysisHelper.CalculateMedian(arr);
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

            int entryRow = AnalysisHelper.ResolveDateIndex(metadata, trade.EntryTime);
            int exitRow = AnalysisHelper.ResolveDateIndex(metadata, trade.ExitTime);
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
        double med = AnalysisHelper.CalculateMedian(effArr);

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
