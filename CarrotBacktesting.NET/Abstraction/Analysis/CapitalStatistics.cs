using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 第二层：资本利用与资金敞口统计模型 (Capital Statistics)。
/// 统筹平均/最大持仓比例、平均/最大保证金占用率、现金闲置率 (Cash Drag) 与年化换手率。
/// 内置静态工厂方法 Create，纯函数式高吞吐构建。
/// </summary>
public sealed record CapitalStatistics(
    double AveragePositionRatio,
    double MaxPositionRatio,
    double AverageMarginRatio,
    double MaxMarginRatio,
    double CashDrag,
    double AnnualizedTurnover
)
{
    /// <summary>
    /// 从日终快照、净值曲线及交割单流水构建资本利用与敞口统计对象。
    /// </summary>
    public static CapitalStatistics Create(
        IReadOnlyList<DailySnapshot> dailySnapshots,
        IReadOnlyList<EquityPoint> equityCurve,
        IReadOnlyList<SettlementRecord> settlements,
        AnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        int annualTradingDays = options.AnnualTradingDays > 0 ? options.AnnualTradingDays : 252;
        double totalDays = 0.0;
        if (equityCurve != null && equityCurve.Count > 1)
        {
            totalDays = (equityCurve[^1].Time - equityCurve[0].Time).TotalDays;
        }
        else if (dailySnapshots != null && dailySnapshots.Count > 1)
        {
            totalDays = (dailySnapshots[^1].Time - dailySnapshots[0].Time).TotalDays;
        }

        double years = totalDays > 0 ? totalDays / 365.25 : 1.0 / annualTradingDays;
        if (years <= 0) years = 1.0 / annualTradingDays;

        double sumPosRatio = 0.0;
        double maxPosRatio = 0.0;
        double sumMarginRatio = 0.0;
        double maxMarginRatio = 0.0;
        double sumCashDrag = 0.0;
        double sumEquity = 0.0;
        int validCount = 0;

        if (dailySnapshots != null && dailySnapshots.Count > 0)
        {
            for (int i = 0; i < dailySnapshots.Count; i++)
            {
                var snap = dailySnapshots[i];
                double eq = snap.TotalEquity;
                if (eq > 0)
                {
                    validCount++;
                    double posRatio = snap.TotalMarketValue / eq;
                    double marginRatio = snap.MarginRatio;
                    double cashDrag = snap.AvailableCash / eq;

                    sumPosRatio += posRatio;
                    if (posRatio > maxPosRatio) maxPosRatio = posRatio;

                    sumMarginRatio += marginRatio;
                    if (marginRatio > maxMarginRatio) maxMarginRatio = marginRatio;

                    sumCashDrag += cashDrag;
                    sumEquity += eq;
                }
            }
        }
        else if (equityCurve != null && equityCurve.Count > 0)
        {
            for (int i = 0; i < equityCurve.Count; i++)
            {
                var pt = equityCurve[i];
                double eq = pt.TotalEquity;
                if (eq > 0)
                {
                    validCount++;
                    double posRatio = pt.TotalMarketValue / eq;
                    double marginRatio = pt.MarginRatio;
                    double cashDrag = pt.AvailableCash / eq;

                    sumPosRatio += posRatio;
                    if (posRatio > maxPosRatio) maxPosRatio = posRatio;

                    sumMarginRatio += marginRatio;
                    if (marginRatio > maxMarginRatio) maxMarginRatio = marginRatio;

                    sumCashDrag += cashDrag;
                    sumEquity += eq;
                }
            }
        }

        double avgPosRatio = validCount > 0 ? sumPosRatio / validCount : 0.0;
        double avgMarginRatio = validCount > 0 ? sumMarginRatio / validCount : 0.0;
        double avgCashDrag = validCount > 0 ? sumCashDrag / validCount : 0.0;
        double avgEquity = validCount > 0 ? sumEquity / validCount : 0.0;

        // 年化换手率 = (总成交金额 / 2) / 平均资产 / 年数
        double totalTurnover = 0.0;
        if (settlements != null)
        {
            for (int i = 0; i < settlements.Count; i++)
            {
                totalTurnover += settlements[i].Turnover;
            }
        }

        double annualizedTurnover = (avgEquity > 0 && years > 0)
            ? (totalTurnover / 2.0) / avgEquity / years
            : 0.0;

        return new CapitalStatistics(
            AveragePositionRatio: avgPosRatio,
            MaxPositionRatio: maxPosRatio,
            AverageMarginRatio: avgMarginRatio,
            MaxMarginRatio: maxMarginRatio,
            CashDrag: avgCashDrag,
            AnnualizedTurnover: annualizedTurnover
        );
    }
}
