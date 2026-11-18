using System;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 绩效分析与评估执行配置项。
/// </summary>
public sealed class AnalysisOptions
{
    /// <summary>
    /// 无风险年化利率（用于夏普比率、索提诺比率超额计算），默认为 0.02（2%）。
    /// </summary>
    public double RiskFreeRate { get; init; } = 0.02;

    /// <summary>
    /// 全年交易日基准常数（用于年化波动率与复合收益折算），日频默认为 252。
    /// </summary>
    public int AnnualTradingDays { get; init; } = 252;

    /// <summary>
    /// 卖点时机反思的前向分析步数，默认 20。
    /// 设为 0 或负数则禁用 Exit Timing 分析。
    /// </summary>
    public int ExitTimingSteps { get; init; } = 20;

    /// <summary>
    /// 策略信号前向评估的前向分析步数，默认 30。
    /// 设为 0 或负数则禁用信号前向分析。
    /// </summary>
    public int SignalHorizonSteps { get; init; } = 30;

    /// <summary>
    /// 比较基准标的代码（例如 "sh.000300"）。若为 null 则跳过基准相关相对收益（Beta/Alpha/信息比率）的计算。
    /// </summary>
    public string? BenchmarkSymbol { get; init; }

    /// <summary>
    /// 用于计算前向收益与基准盯市的时序行情字段名，默认 "close"。
    /// </summary>
    public string PriceFieldName { get; init; } = "close";
}

/// <summary>
/// 绩效分析与统计引擎核心契约（统一分析管线调度器）。
/// 纯粹通过一路径驱动注册的分析器管线（IAnalyzer），支持任意分析器的动态注册、替换与移除。
/// </summary>
public interface IAnalysisEngine
{
    /// <summary>
    /// 获取当前管线中已注册的分析器只读列表。
    /// </summary>
    IReadOnlyList<IAnalyzer> Analyzers { get; }

    /// <summary>
    /// 向管线中添加分析器（支持链式调用）。
    /// </summary>
    /// <param name="analyzer">待挂载的分析器实例。</param>
    /// <returns>返回分析引擎自身以支持流式调用。</returns>
    IAnalysisEngine Add(IAnalyzer analyzer);

    /// <summary>
    /// 注册分析器（Add 的语义别名，兼容历史契约）。
    /// </summary>
    IAnalysisEngine RegisterAnalyzer(IAnalyzer analyzer) => Add(analyzer);

    /// <summary>
    /// 从管线中移除指定类型的分析器（例如移除默认的 SignalAnalyzer）。
    /// </summary>
    /// <typeparam name="T">待移除的分析器具体类型。</typeparam>
    /// <returns>返回分析引擎自身以支持流式调用。</returns>
    IAnalysisEngine Remove<T>() where T : IAnalyzer;

    /// <summary>
    /// 传入不可变的交易事实账本与可选行情矩阵，单路径调度管线执行全景量化分析并产出聚合报告。
    /// </summary>
    /// <param name="result">回测或实盘沉淀的事实账本数据集。</param>
    /// <param name="options">分析配置参数（为 null 时采用默认配置）。</param>
    /// <param name="timeseries">只读时序行情注册表（可选，用于计算卖点反思、信号前向收益与基准对比）。</param>
    /// <returns>全景综合分析报告。</returns>
    AnalysisReport Analyze(TradingResult result, AnalysisOptions? options = null, ITimeseriesRegistry? timeseries = null);
}
