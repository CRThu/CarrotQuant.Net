using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Analysis.Analyzers;
using CarrotBacktesting.NET.Analysis.Exporters;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Analysis;

/// <summary>
/// 绩效分析与报表导出装配工厂 (Analysis & Exporter Factory)。
/// 作为绩效分析子系统的统一装配门面，根据强类型 AnalysisConfig 模型动态装配分析器管线（IAnalysisEngine）与报表导出器列表。
/// 遵循约定优于配置原则：未配置时自动装配官方五大标准分析器与默认控制台导出器。
/// </summary>
public static class AnalysisEngineFactory
{
    private static readonly Dictionary<string, Type> KnownAnalyzers = new(StringComparer.OrdinalIgnoreCase)
    {
        { "portfolio", typeof(PortfolioAnalyzer) },
        { "capital", typeof(CapitalAnalyzer) },
        { "trades", typeof(TradeAnalyzer) },
        { "cost", typeof(CostAnalyzer) },
        { "signals", typeof(SignalAnalyzer) }
    };

    private static readonly Dictionary<string, Type> KnownExporters = new(StringComparer.OrdinalIgnoreCase)
    {
        { "console", typeof(ConsoleExporter) },
        { "json", typeof(JsonReportExporter) },
        { "html", typeof(HtmlReportExporter) },
        { "excel", typeof(ExcelReportExporter) }
    };

    /// <summary>
    /// 根据分析配置装配分析引擎管线。
    /// </summary>
    /// <param name="config">分析配置模型（为 null 时采用官方全套五大分析器）。</param>
    /// <returns>已装配就绪的 IAnalysisEngine 实例。</returns>
    public static IAnalysisEngine Create(AnalysisConfig? config = null)
    {
        if (config == null || config.Analyzers == null || config.Analyzers.Count == 0)
        {
            // 缺省时默认装配官方全部五大分析器
            return new AnalysisEngine();
        }

        var engine = new AnalysisEngine(Array.Empty<IAnalyzer>());

        foreach (var node in config.Analyzers)
        {
            if (!node.Enabled) continue;
            if (string.IsNullOrWhiteSpace(node.Type))
            {
                throw new InvalidOperationException("分析器节点配置缺少必需的 'type' 字段。");
            }

            var analyzer = CreateAnalyzerInstance(node);
            engine.Add(analyzer);
        }

        return engine;
    }

    /// <summary>
    /// 根据分析配置装配报表导出器序列。
    /// 遵循约定优于配置原则：未配置 exporters 时自动装配官方默认控制台仪表盘导出器。
    /// </summary>
    /// <param name="config">分析配置模型（为 null 时采用 ConsoleExporter.Default）。</param>
    /// <param name="baseDirectory">相对文件路径解析基准目录（可选，缺省为当前工作目录）。</param>
    /// <returns>已装配就绪的 IReportExporter 序列。</returns>
    public static IReadOnlyList<IReportExporter> CreateExporters(AnalysisConfig? config = null, string? baseDirectory = null)
    {
        if (config == null || config.Exporters == null || config.Exporters.Count == 0)
        {
            return new IReportExporter[] { ConsoleExporter.Default };
        }

        string effectiveBaseDir = string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(baseDirectory);

        var list = new List<IReportExporter>();

        foreach (var node in config.Exporters)
        {
            if (!node.Enabled) continue;
            if (string.IsNullOrWhiteSpace(node.Type))
            {
                throw new InvalidOperationException("导出器节点配置缺少必需的 'type' 字段。");
            }

            var exporter = CreateExporterInstance(node, effectiveBaseDir);
            list.Add(exporter);
        }

        return list;
    }

    /// <summary>
    /// 便捷扩展方法：使用强类型 AnalysisConfig 直接驱动分析引擎执行全量分析。
    /// 自动将 AnalysisOptionsConfig 转换为底层的 AnalysisOptions。
    /// </summary>
    /// <param name="engine">分析引擎实例。</param>
    /// <param name="facts">回测或实盘沉淀的事实账本数据集。</param>
    /// <param name="config">分析配置模型（若为 null 则采用默认参数）。</param>
    /// <param name="timeseries">只读时序行情注册表（可选）。</param>
    /// <returns>全景综合分析报告。</returns>
    public static AnalysisReport Analyze(
        this IAnalysisEngine engine,
        TradingResult facts,
        AnalysisConfig? config,
        ITimeseriesRegistry? timeseries = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Analyze(facts, config?.Options?.ToOptions(), timeseries);
    }

    /// <summary>
    /// 执行量化分析并依据配置执行报表导出。
    /// </summary>
    /// <param name="engine">分析引擎实例。</param>
    /// <param name="facts">不可变交易事实账本。</param>
    /// <param name="config">分析综合配置模型（若为 null 则使用默认五大分析器与控制台导出）。</param>
    /// <param name="timeseries">只读时序行情注册表（可选）。</param>
    /// <param name="baseDirectory">相对路径解析基准目录（可选）。</param>
    /// <returns>全景综合分析报告。</returns>
    public static AnalysisReport AnalyzeAndExport(
        this IAnalysisEngine engine,
        TradingResult facts,
        AnalysisConfig? config,
        ITimeseriesRegistry? timeseries = null,
        string? baseDirectory = null)
    {
        var report = engine.Analyze(facts, config, timeseries);
        config ??= new AnalysisConfig();
        config.Export(report, baseDirectory);
        return report;
    }

    private static IAnalyzer CreateAnalyzerInstance(AnalyzerNodeConfig node)
    {
        Type targetType = ResolveAnalyzerType(node.Type);

        if (!typeof(IAnalyzer).IsAssignableFrom(targetType))
        {
            throw new InvalidOperationException($"指定的类型 '{targetType.FullName}' 未实现 IAnalyzer 契约。");
        }

        var instance = (IAnalyzer)Activator.CreateInstance(targetType)!;

        if (node.Params != null && node.Params.Count > 0)
        {
            ConfigPropertyInjector.Inject(instance, node.Params, instance.Name);
        }

        return instance;
    }

    private static IReportExporter CreateExporterInstance(ExporterNodeConfig node, string baseDir)
    {
        Type targetType = ResolveExporterType(node.Type);

        if (!typeof(IReportExporter).IsAssignableFrom(targetType))
        {
            throw new InvalidOperationException($"指定的类型 '{targetType.FullName}' 未实现 IReportExporter 契约。");
        }

        // 1. ConsoleExporter 官方特化：注入 ConsoleExporterOptions
        if (targetType == typeof(ConsoleExporter))
        {
            var options = new ConsoleExporterOptions();
            if (node.Params != null && node.Params.Count > 0)
            {
                ConfigPropertyInjector.Inject(options, node.Params, nameof(ConsoleExporterOptions));

                if (!string.IsNullOrWhiteSpace(options.HtmlOutputPath) && !Path.IsPathRooted(options.HtmlOutputPath))
                {
                    options = options with { HtmlOutputPath = Path.GetFullPath(Path.Combine(baseDir, options.HtmlOutputPath)) };
                }
            }
            return new ConsoleExporter(options);
        }

        // 2. 其余所有实现（JsonReportExporter 及第三方扩展）：无参构造 + 通用属性注入
        var instance = (IReportExporter)Activator.CreateInstance(targetType)!;

        if (node.Params != null && node.Params.Count > 0)
        {
            ConfigPropertyInjector.Inject(instance, node.Params, targetType.Name);
        }

        switch (instance)
        {
            case JsonReportExporter jsonExp when !Path.IsPathRooted(jsonExp.OutputPath):
                jsonExp.OutputPath = Path.GetFullPath(Path.Combine(baseDir, jsonExp.OutputPath));
                break;
            case HtmlReportExporter htmlExp when !Path.IsPathRooted(htmlExp.OutputPath):
                htmlExp.OutputPath = Path.GetFullPath(Path.Combine(baseDir, htmlExp.OutputPath));
                break;
            case ExcelReportExporter excelExp when !Path.IsPathRooted(excelExp.OutputPath):
                excelExp.OutputPath = Path.GetFullPath(Path.Combine(baseDir, excelExp.OutputPath));
                break;
        }

        return instance;
    }

    private static Type ResolveAnalyzerType(string typeName)
    {
        if (KnownAnalyzers.TryGetValue(typeName, out var knownType))
        {
            return knownType;
        }

        var targetType = Type.GetType(typeName, throwOnError: false, ignoreCase: true);
        if (targetType != null) return targetType;

        targetType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetExportedTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => typeof(IAnalyzer).IsAssignableFrom(t)
                              && !t.IsAbstract
                              && (string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase)));

        if (targetType != null) return targetType;

        throw new TypeLoadException($"无法解析名称为 '{typeName}' 的分析器类型。请检查类名拼写或确保其所在程序集已载入。");
    }

    private static Type ResolveExporterType(string typeName)
    {
        if (KnownExporters.TryGetValue(typeName, out var knownType))
        {
            return knownType;
        }

        var targetType = Type.GetType(typeName, throwOnError: false, ignoreCase: true);
        if (targetType != null) return targetType;

        targetType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetExportedTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => typeof(IReportExporter).IsAssignableFrom(t)
                              && !t.IsAbstract
                              && (string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase)));

        if (targetType != null) return targetType;

        throw new TypeLoadException($"无法解析名称为 '{typeName}' 的报表导出器类型。请检查类名拼写或确保其所在程序集已载入。");
    }
}
