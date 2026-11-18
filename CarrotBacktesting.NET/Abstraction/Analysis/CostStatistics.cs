using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 第四层：现实交易成本与损耗统计模型 (Cost Statistics)。
/// 统筹券商佣金税费、滑点损耗冲击、总摩擦成本、摩擦侵蚀比 (Fee to Gross Profit) 与平均单笔摩擦。
/// 内置静态工厂方法 Create，纯函数式高吞吐构建。
/// </summary>
public sealed record CostStatistics(
    double TotalCommission,
    double TotalSlippage,
    double TotalFrictionCost,
    double FeeToGrossProfitRatio,
    double AverageFrictionPerTrade
)
{
    /// <summary>
    /// 从券商交割单与已平仓交易构建交易成本统计对象。
    /// </summary>
    public static CostStatistics Create(
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
