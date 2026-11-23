using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Analysis
{
    /// <summary>
    /// 自然月度收益率数据模型。
    /// </summary>
    /// <param name="Year">自然年份。</param>
    /// <param name="Month">自然月份 (1-12)。</param>
    /// <param name="ReturnRate">当月净值收益率。</param>
    public readonly record struct MonthlyReturn(int Year, int Month, double ReturnRate);

    /// <summary>
    /// 第一层：组合净值与宏观风险统计模型 (Portfolio Statistics)。
    /// 包含收益、波动率、最大回撤、夏普、索提诺、卡玛、月度收益矩阵及基准相对表现。
    /// 纯数据 Record，实现统一的 IAnalysisResult 契约，支持自描述指标流。
    /// </summary>
    public sealed record PortfolioStatistics(
        double TotalReturn,
        double AnnualizedReturn,
        double AnnualizedVolatility,
        double DownsideVolatility,
        double MaxDrawdown,
        MarketTime MaxDrawdownPeakTime,
        MarketTime MaxDrawdownTroughTime,
        MarketTime? MaxDrawdownRecoveryTime,
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
    ) : IAnalysisResult
    {
        /// <inheritdoc />
        public string Name => "portfolio";

        /// <inheritdoc />
        public string Title => "组合净值与风险收益统计";

        /// <inheritdoc />
        public IEnumerable<MetricItem> GetMetrics()
        {
            yield return new("累计总收益 (Total Return)", $"{TotalReturn:P2}", "收益指标");
            yield return new("年化复合收益率 (CAGR)", $"{AnnualizedReturn:P2}", "收益指标");
            yield return new("年化波动率 (Volatility)", $"{AnnualizedVolatility:P2}", "风险指标");
            yield return new("年化下行半方差 (Downside Vol)", $"{DownsideVolatility:P2}", "风险指标");
            yield return new("最大回撤 (Max Drawdown)", $"{MaxDrawdown:P2}", "风险指标");
            yield return new("最大回撤持续步数", $"{MaxDrawdownDurationSteps} 步", "风险指标");
            yield return new("夏普比率 (Sharpe Ratio)", $"{SharpeRatio:F2}", "风险收益比");
            yield return new("索提诺比率 (Sortino Ratio)", $"{SortinoRatio:F2}", "风险收益比");
            yield return new("卡玛比率 (Calmar Ratio)", $"{CalmarRatio:F2}", "风险收益比");
            yield return new("日度在险价值 (Daily VaR 95%)", $"{DailyVaR95:P2}", "极值风险");
            yield return new("日度条件在险价值 (Daily CVaR 95%)", $"{DailyCVaR95:P2}", "极值风险");

            if (BenchmarkReturn.HasValue)
                yield return new("基准总收益 (Benchmark Return)", $"{BenchmarkReturn.Value:P2}", "基准对比");
            if (Beta.HasValue)
                yield return new("贝塔系数 (Beta)", $"{Beta.Value:F2}", "基准对比");
            if (Alpha.HasValue)
                yield return new("阿尔法超额 (Alpha)", $"{Alpha.Value:P2}", "基准对比");
            if (InformationRatio.HasValue)
                yield return new("信息比率 (Information Ratio)", $"{InformationRatio.Value:F2}", "基准对比");
        }
    }

    /// <summary>
    /// 第二层：资本利用与资金敞口统计模型 (Capital Statistics)。
    /// 统筹平均/最大持仓比例、平均/最大保证金占用率、现金闲置率 (Cash Drag) 与年化换手率。
    /// 纯数据 Record，实现统一的 IAnalysisResult 契约，支持自描述指标流。
    /// </summary>
    public sealed record CapitalStatistics(
        double AveragePositionRatio,
        double MaxPositionRatio,
        double AverageMarginRatio,
        double MaxMarginRatio,
        double CashDrag,
        double AnnualizedTurnover,
        TradingStatus Status = TradingStatus.Normal
    ) : IAnalysisResult
    {
        /// <inheritdoc />
        public string Name => "capital";

        /// <inheritdoc />
        public string Title => "资本利用与敞口分析";

        /// <inheritdoc />
        public IEnumerable<MetricItem> GetMetrics()
        {
            yield return new("交易终局状态 (Trading Status)", $"{Status}", "状态监控");
            yield return new("平均持仓占比 (Avg Position)", $"{AveragePositionRatio:P2}", "仓位暴露");
            yield return new("最大持仓占比 (Max Position)", $"{MaxPositionRatio:P2}", "仓位暴露");
            yield return new("平均保证金占用 (Avg Margin)", $"{AverageMarginRatio:P2}", "杠杆安全");
            yield return new("最大保证金占用 (Max Margin)", $"{MaxMarginRatio:P2}", "杠杆安全");
            yield return new("现金闲置率 (Cash Drag)", $"{CashDrag:P2}", "资金效率");
            yield return new("年化换手率 (Turnover)", $"{AnnualizedTurnover:F2}x", "交易频率");
        }
    }

    /// <summary>
    /// 平仓后指定前向周期 (T+1..T+N) 价格走势与卖出时机评估统计模型。
    /// </summary>
    /// <param name="HorizonSteps">前向评估步数/天数。</param>
    /// <param name="ValidTradesCount">参与评估的有效平仓笔数。</param>
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
    }

    /// <summary>
    /// 前向固定步数（T+Step）信号样本在测试集上的胜率与收益统计细节模型。
    /// </summary>
    /// <param name="SignalCount">有效信号样本总数。</param>
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
    /// 第五层：独立 Alpha 信号前向质量统计模型 (Signal Statistics)。
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
    }
}
