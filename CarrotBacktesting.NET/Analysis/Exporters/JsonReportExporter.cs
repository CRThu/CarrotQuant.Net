using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Analysis.Analyzers;

namespace CarrotBacktesting.NET.Analysis.Exporters;

/// <summary>
/// 标准 JSON 报表持久化导出器 (JSON Report Exporter)。
/// 作为 AnalysisReport 序列化落盘与脱机还原的单一事实来源 (SSOT)：
/// 既实现 IReportExporter 契约作为回测管线导出插件，
/// 又静态暴露完整的 Save / Load / ToJson / FromJson 双向持久化能力。
/// </summary>
public sealed class JsonReportExporter : IReportExporter
{
    private static readonly JsonSerializerOptions IndentedOptions = CreateDefaultOptions(indented: true);
    private static readonly JsonSerializerOptions CompactOptions = CreateDefaultOptions(indented: false);

    private static JsonSerializerOptions CreateDefaultOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indented,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    /// <summary>
    /// 输出文件路径（默认为 "report.json"）。
    /// </summary>
    public string OutputPath { get; set; } = "report.json";

    /// <summary>
    /// 是否格式化排版缩进（默认为 true）。
    /// </summary>
    public bool Indented { get; set; } = true;

    /// <summary>
    /// 默认无参构造函数（支持通过通用 ConfigPropertyInjector 进行声明式属性注入）。
    /// </summary>
    public JsonReportExporter() { }

    /// <summary>
    /// 构造 JSON 报表持久化导出器实例。
    /// </summary>
    /// <param name="outputPath">持久化落盘的目标文件路径（默认为 "report.json"）。</param>
    /// <param name="indented">是否格式化排版缩进。</param>
    public JsonReportExporter(string outputPath, bool indented = true)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("输出路径不可为空。", nameof(outputPath));

        OutputPath = outputPath;
        Indented = indented;
    }

    /// <summary>
    /// 执行分析报告的 JSON 序列化与文件持久化导出（IReportExporter 契约入口）。
    /// </summary>
    /// <param name="report">分析报告聚合根。</param>
    public void Export(AnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Save(report, OutputPath, Indented);
    }

    #region 双向持久化静态权威 API (SSOT)

    /// <summary>
    /// 将分析报告序列化为标准 JSON 字符串。
    /// </summary>
    /// <param name="report">分析报告。</param>
    /// <param name="indented">是否格式化缩进排版（默认为 true）。</param>
    /// <returns>JSON 字符串。</returns>
    public static string ToJson(AnalysisReport report, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(report);
        var snapshot = CreateSnapshot(report);
        var options = indented ? IndentedOptions : CompactOptions;
        return JsonSerializer.Serialize(snapshot, options);
    }

    /// <summary>
    /// 从标准 JSON 字符串反序列化还原分析报告。
    /// </summary>
    /// <param name="json">JSON 字符串。</param>
    /// <returns>还原后的分析报告聚合根。</returns>
    public static AnalysisReport FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON 文本不可为空。", nameof(json));

        var snapshot = JsonSerializer.Deserialize<ReportSnapshot>(json, IndentedOptions)
            ?? throw new InvalidDataException("无法从 JSON 中反序列化有效的 ReportSnapshot。");

        return RestoreReport(snapshot);
    }

    /// <summary>
    /// 将分析报告序列化并持久化至指定磁盘文件。
    /// 自动创建缺失的父级目录。
    /// </summary>
    /// <param name="report">分析报告。</param>
    /// <param name="filePath">目标文件路径。</param>
    /// <param name="indented">是否格式化缩进排版。</param>
    public static void Save(AnalysisReport report, string filePath, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("目标文件路径不可为空。", nameof(filePath));

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = ToJson(report, indented);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// 从磁盘文件读取 JSON 并还原分析报告（脱机反序列化）。
    /// </summary>
    /// <param name="filePath">源文件路径。</param>
    /// <returns>还原后的分析报告聚合根。</returns>
    public static AnalysisReport Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("源文件路径不可为空。", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"指定的报告文件不存在: {filePath}", filePath);

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        return FromJson(json);
    }

    #endregion

    #region 内部专用快照转换模型 (Encapsulated DTO)

    private static ReportSnapshot CreateSnapshot(AnalysisReport report)
    {
        List<GenericResultSnapshot>? extensions = null;

        // 收集第三方或未知自描述扩展分析产物
        foreach (var r in report.Results)
        {
            if (r is PortfolioStatistics or CapitalStatistics or TradeStatistics or CostStatistics or SignalStatistics)
            {
                continue; // 官方五大统计已有强类型专用槽位
            }

            extensions ??= new List<GenericResultSnapshot>();
            extensions.Add(new GenericResultSnapshot(r.Name, r.Title, r.GetMetrics().ToList()));
        }

        return new ReportSnapshot
        {
            Version = "1.0",
            CreatedAtUtc = DateTime.UtcNow,
            Facts = report.Facts,
            Portfolio = report.Portfolio,
            Capital = report.Capital,
            Trades = report.Trades,
            Cost = report.Cost,
            Signals = report.Signals,
            Extensions = extensions
        };
    }

    private static AnalysisReport RestoreReport(ReportSnapshot snapshot)
    {
        var facts = snapshot.Facts ?? new TradingResult();

        return new AnalysisReport(
            facts: facts,
            portfolio: snapshot.Portfolio,
            capital: snapshot.Capital,
            trades: snapshot.Trades,
            cost: snapshot.Cost,
            signals: snapshot.Signals,
            additionalResults: snapshot.Extensions
        );
    }

    internal sealed class ReportSnapshot
    {
        public string Version { get; init; } = "1.0";
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
        public TradingResult Facts { get; init; } = new();
        public PortfolioStatistics? Portfolio { get; init; }
        public CapitalStatistics? Capital { get; init; }
        public TradeStatistics? Trades { get; init; }
        public CostStatistics? Cost { get; init; }
        public SignalStatistics? Signals { get; init; }
        public IReadOnlyList<GenericResultSnapshot>? Extensions { get; init; }
    }

    internal sealed class GenericResultSnapshot : IAnalysisResult
    {
        public string Name { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public IReadOnlyList<MetricItem> Metrics { get; init; } = Array.Empty<MetricItem>();

        public IEnumerable<MetricItem> GetMetrics() => Metrics;

        public GenericResultSnapshot() { }

        public GenericResultSnapshot(string name, string title, IReadOnlyList<MetricItem> metrics)
        {
            Name = name ?? string.Empty;
            Title = title ?? string.Empty;
            Metrics = metrics ?? Array.Empty<MetricItem>();
        }
    }

    #endregion
}
