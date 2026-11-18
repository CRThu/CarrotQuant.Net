using System;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 纯粹的统一分析器契约 (Unified Analyzer Contract)。
/// 系统所有分析器（包含官方五大标准指标与任何外部扩展）均统一实现此契约，地位完全均等。
/// </summary>
public interface IAnalyzer
{
    /// <summary>
    /// 分析器唯一命名标识（用于结果索引与配置映射，不区分大小写）。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 对回测或实盘事实流水及可选行情矩阵执行分析并产出强类型自描述结果。
    /// </summary>
    /// <param name="facts">不可变交易事实账本 (SSOT)。</param>
    /// <param name="options">全局分析配置选项。</param>
    /// <param name="timeseries">只读时序行情矩阵注册表（可选）。</param>
    /// <returns>分析产物实体（实现 IAnalysisResult）；若无有效结果可返回 null。</returns>
    IAnalysisResult? Analyze(TradingResult facts, AnalysisOptions options, ITimeseriesRegistry? timeseries);
}

/// <summary>
/// 强类型分析器泛型模板基类。
/// 简化强类型分析产物的编写与类型安全绑定。
/// </summary>
/// <typeparam name="TResult">分析产物强类型实体，必须实现 IAnalysisResult。</typeparam>
public abstract class AnalyzerBase<TResult> : IAnalyzer where TResult : class, IAnalysisResult
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <summary>
    /// 执行强类型分析计算。
    /// </summary>
    public abstract TResult? Analyze(TradingResult facts, AnalysisOptions options, ITimeseriesRegistry? timeseries);

    IAnalysisResult? IAnalyzer.Analyze(TradingResult facts, AnalysisOptions options, ITimeseriesRegistry? timeseries)
        => Analyze(facts, options, timeseries);
}
