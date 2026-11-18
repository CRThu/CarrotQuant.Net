namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 绩效分析报表导出器契约 (Report Exporter)。
/// 负责将生成的结构化 AnalysisReport 导出呈现至终端、磁盘文件或其他宿主介质。
/// </summary>
public interface IReportExporter
{
    /// <summary>
    /// 执行报表导出呈现。
    /// </summary>
    /// <param name="report">分析引擎生成的综合报告聚合根。</param>
    void Export(AnalysisReport report);
}
