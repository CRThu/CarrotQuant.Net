using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Analysis.Analyzers;

/// <summary>
/// 第四层：现实交易成本与摩擦损耗分析器 (Cost Analyzer)。
/// 计算累计佣金、滑点冲击损耗、综合摩擦总额、利润侵蚀比与单笔平均摩擦成本。
/// 分析完成后输出只读不可变的 CostStatistics 统计模型。
/// </summary>
public sealed class CostAnalyzer : AnalyzerBase<CostStatistics>
{
    /// <inheritdoc />
    public override string Name => "Cost";

    /// <inheritdoc />
    public override CostStatistics? Analyze(
        TradingResult facts,
        AnalysisOptions options,
        ITimeseriesRegistry? timeseries)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(options);

        return Calculate(facts.Settlements, facts.ClosedTrades);
    }

    /// <summary>
    /// 从券商交割单与已平仓交易计算并构建交易成本统计对象。
    /// </summary>
    public static CostStatistics Calculate(
        IReadOnlyList<SettlementRecord> settlements,
        IReadOnlyList<Trade> trades)
    {
        double totalCommission = 0.0;
        if (settlements != null)
        {
            for (int i = 0; i < settlements.Count; i++)
            {
                totalCommission += settlements[i].Commission;
            }
        }

        double totalSlippage = 0.0;
        double grossProfit = 0.0;
        int tradeCount = 0;

        if (trades != null)
        {
            tradeCount = trades.Count;
            for (int i = 0; i < tradeCount; i++)
            {
                totalSlippage += trades[i].SlippageCost;
                if (trades[i].RealizedPnL > 0)
                {
                    grossProfit += trades[i].RealizedPnL;
                }
            }
        }

        double totalFriction = totalCommission + totalSlippage;
        double feeToGrossProfit = grossProfit > 1e-9 ? totalFriction / grossProfit : 0.0;
        double avgFrictionPerTrade = tradeCount > 0 ? totalFriction / tradeCount : 0.0;

        return new CostStatistics(
            TotalCommission: totalCommission,
            TotalSlippage: totalSlippage,
            TotalFrictionCost: totalFriction,
            FeeToGrossProfitRatio: feeToGrossProfit,
            AverageFrictionPerTrade: avgFrictionPerTrade
        );
    }
}

/// <summary>
/// 第四层：现实交易成本与损耗统计模型 (Cost Statistics)。
/// 统筹券商佣金税费、滑点损耗冲击、总摩擦成本、摩擦侵蚀比 (Fee to Gross Profit) 与平均单笔摩擦。
/// 纯数据 Record，实现统一的 IAnalysisResult 契约，支持自描述指标流。
/// </summary>
public sealed record CostStatistics(
    double TotalCommission,
    double TotalSlippage,
    double TotalFrictionCost,
    double FeeToGrossProfitRatio,
    double AverageFrictionPerTrade
) : IAnalysisResult
{
    /// <inheritdoc />
    public string Name => "cost";

    /// <inheritdoc />
    public string Title => "现实损耗与交易成本";

    /// <inheritdoc />
    public IEnumerable<MetricItem> GetMetrics()
    {
        yield return new("累计交易佣金税费 (Commission)", $"¥{TotalCommission:N2}", "摩擦明细");
        yield return new("累计滑点冲击损耗 (Slippage)", $"¥{TotalSlippage:N2}", "摩擦明细");
        yield return new("综合摩擦损耗总额 (Total Friction)", $"¥{TotalFrictionCost:N2}", "摩擦明细");
        yield return new("摩擦利润侵蚀比 (Fee to Profit)", $"{FeeToGrossProfitRatio:P2}", "侵蚀评估");
        yield return new("平均每笔交易摩擦 (Avg Friction)", $"¥{AverageFrictionPerTrade:N2}", "侵蚀评估");
    }

    /// <summary>
    /// 从券商交割单与已平仓交易构建交易成本统计对象（转发至 CostAnalyzer.Calculate）。
    /// </summary>
    public static CostStatistics Create(
        IReadOnlyList<SettlementRecord> settlements,
        IReadOnlyList<Trade> trades)
        => CostAnalyzer.Calculate(settlements, trades);
}
