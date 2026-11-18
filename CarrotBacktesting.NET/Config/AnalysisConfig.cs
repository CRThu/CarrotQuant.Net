using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Analysis;

namespace CarrotBacktesting.NET.Config;

/// <summary>
/// 绩效分析与报表导出综合配置根（Analysis & Exporters Configuration Root）。
/// 统筹分析算法选项、启用的分析器管线节点与报表导出器配置。
/// 支持直接内嵌于 SessionConfig 中，亦支持赋值为外部独立 YAML 文件相对路径（如 "shared/analysis.yaml"）实现跨策略复用。
/// </summary>
public class AnalysisConfig
{
    /// <summary>
    /// 全局分析算法参数设置（无风险利率、年化交易日数、基准标的等）。
    /// </summary>
    public AnalysisOptionsConfig Options { get; set; } = new();

    /// <summary>
    /// 启用的分析器节点配置列表。
    /// 缺省或为空时默认装配官方全部五大分析器（Portfolio、Capital、Trades、Cost、Signals）。
    /// </summary>
    public List<AnalyzerNodeConfig> Analyzers { get; set; } = new();

    /// <summary>
    /// 启用的报表导出器节点配置列表。
    /// 缺省或为空时默认装配官方控制台仪表盘导出器（ConsoleExporter）。
    /// </summary>
    public List<ExporterNodeConfig> Exporters { get; set; } = new();

    #region 统一门面方法 (Unified Facade APIs)

    /// <summary>
    /// 从指定物理路径加载并反序列化分析配置 YAML 文件。
    /// </summary>
    /// <param name="filePath">YAML 配置文件物理路径。</param>
    /// <returns>反序列化并解析路径后的 AnalysisConfig 实例。</returns>
    public static AnalysisConfig Load(string filePath) => AnalysisConfigLoader.Load(filePath);

    /// <summary>
    /// 从 YAML 文本字符串反序列化分析配置模型。
    /// </summary>
    /// <param name="yamlContent">YAML 文本内容。</param>
    /// <param name="baseDirectory">相对路径解析基准目录。</param>
    /// <returns>AnalysisConfig 实例。</returns>
    public static AnalysisConfig FromYaml(string yamlContent, string? baseDirectory = null) => AnalysisConfigLoader.FromYaml(yamlContent, baseDirectory);

    /// <summary>
    /// 将当前分析配置实例序列化为标准下划线命名规范的 YAML 文本。
    /// </summary>
    public string ToYaml() => YamlSerializer.Serialize(this);

    /// <summary>
    /// 将当前分析配置实例序列化并保存至指定物理文件。
    /// </summary>
    /// <param name="filePath">输出文件物理路径。</param>
    public void Save(string filePath) => YamlSerializer.Save(this, filePath);

    /// <summary>
    /// 将指定的分析报告根据当前配置的导出器管线执行全量导出（支持 ANSI 终端仪表盘、HTML 与 JSON 等多渠道导出）。
    /// </summary>
    /// <param name="report">待导出的综合分析报告聚合根。</param>
    /// <param name="baseDirectory">相对路径解析基准目录（可选）。</param>
    public void Export(AnalysisReport report, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var exporters = CreateExporters(baseDirectory);
        for (int i = 0; i < exporters.Count; i++)
        {
            exporters[i].Export(report);
        }
    }

    /// <summary>
    /// 根据当前配置装配就绪的报表导出器序列。
    /// 缺省未配置 exporters 时自动装配官方默认控制台导出器。
    /// </summary>
    /// <param name="baseDirectory">相对路径解析基准目录（可选）。</param>
    /// <returns>IReportExporter 导出器序列。</returns>
    public IReadOnlyList<IReportExporter> CreateExporters(string? baseDirectory = null)
        => AnalysisEngineFactory.CreateExporters(this, baseDirectory);

    #endregion
}

/// <summary>
/// 全局绩效分析算法参数选项配置（与 AnalysisOptions 对应，支持 YAML 蛇形命名对齐）。
/// </summary>
public class AnalysisOptionsConfig
{
    /// <summary>
    /// 无风险年化利率（用于夏普比率、索提诺比率超额计算），默认为 0.02（2%）。
    /// </summary>
    public double RiskFreeRate { get; set; } = 0.02;

    /// <summary>
    /// 全年交易日基准常数（用于年化波动率与复合收益折算），日频默认为 252。
    /// </summary>
    public int AnnualTradingDays { get; set; } = 252;

    /// <summary>
    /// 卖点时机反思的前向分析步数，默认 20。设为 0 或负数则禁用 Exit Timing 分析。
    /// </summary>
    public int ExitTimingSteps { get; set; } = 20;

    /// <summary>
    /// 策略信号前向评估的前向分析步数，默认 30。设为 0 或负数则禁用信号前向分析。
    /// </summary>
    public int SignalHorizonSteps { get; set; } = 30;

    /// <summary>
    /// 比较基准标的代码（例如 "sh.000300"）。若为 null 则跳过相对收益（Beta/Alpha/信息比率）的计算。
    /// </summary>
    public string? BenchmarkSymbol { get; set; }

    /// <summary>
    /// 用于计算前向收益与基准盯市的时序行情字段名，默认 "close"。
    /// </summary>
    public string PriceFieldName { get; set; } = "close";

    /// <summary>
    /// 转换为分析层强类型 AnalysisOptions 实例。
    /// </summary>
    public AnalysisOptions ToOptions() => new()
    {
        RiskFreeRate = RiskFreeRate,
        AnnualTradingDays = AnnualTradingDays,
        ExitTimingSteps = ExitTimingSteps,
        SignalHorizonSteps = SignalHorizonSteps,
        BenchmarkSymbol = BenchmarkSymbol,
        PriceFieldName = PriceFieldName
    };
}

/// <summary>
/// 单个分析器节点配置项。
/// </summary>
public class AnalyzerNodeConfig
{
    /// <summary>
    /// 分析器类型短名称或类名（如 "PortfolioAnalyzer", "CapitalAnalyzer", 或自定义全类名）。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用该分析器（默认为 true）。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 自定义参数键值对（若分析器具备可注入属性）。
    /// </summary>
    public Dictionary<string, object>? Params { get; set; }
}

/// <summary>
/// 单个报表导出器节点配置项。
/// </summary>
public class ExporterNodeConfig
{
    /// <summary>
    /// 导出器类型短名称或类名（如 "ConsoleExporter", "JsonReportExporter", 或自定义全类名）。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用该导出器（默认为 true）。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 导出器参数键值对（支持注入属性，如 show_portfolio, save_html, output_path 等）。
    /// </summary>
    public Dictionary<string, object>? Params { get; set; }
}
