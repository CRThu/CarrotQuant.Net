using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 结构化单项指标键值对（用于自描述报表导出）。
/// </summary>
/// <param name="Label">指标显示名称（例如 "夏普比率 (Sharpe Ratio)"）。</param>
/// <param name="Value">格式化后的指标数值字符串（例如 "1.85" 或 "15.2%"）。</param>
/// <param name="Category">可选的指标分类归属（例如 "风险指标", "收益指标"）。</param>
public readonly record struct MetricItem(string Label, string Value, string? Category = null);

/// <summary>
/// 所有量化分析与绩效统计产物的统一定义契约。
/// 官方标准指标与外部扩展分析结果均遵循此契约实现自描述输出。
/// </summary>
public interface IAnalysisResult
{
    /// <summary>
    /// 分析结果唯一标识（例如 "portfolio", "capital", "trades", "cost", "signals"）。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 报表可读显示标题（例如 "组合净值与风险统计", "交易行为与执行归因"）。
    /// </summary>
    string Title { get; }

    /// <summary>
    /// 输出自描述指标键值流（供控制台、HTML、Excel 报表无需硬编码即可通排通显）。
    /// </summary>
    IEnumerable<MetricItem> GetMetrics();
}
