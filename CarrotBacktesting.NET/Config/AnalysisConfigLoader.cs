using System;
using System.IO;

namespace CarrotBacktesting.NET.Config;

/// <summary>
/// YAML 绩效分析配置加载器。
/// 负责将 YAML 文件或文本反序列化为强类型 AnalysisConfig 并规范化文件路径。
/// 底层反序列化由系统统一的 YamlSerializer 驱动。
/// </summary>
public static class AnalysisConfigLoader
{
    /// <summary>
    /// 从指定物理路径加载分析配置。
    /// </summary>
    /// <param name="filePath">YAML 配置文件物理路径。</param>
    /// <returns>解析后的 AnalysisConfig 实例。</returns>
    public static AnalysisConfig Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("配置文件路径不可为空。", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"分析配置文件不存在: {fullPath}");

        string configDir = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string yaml = File.ReadAllText(fullPath);

        return FromYaml(yaml, configDir);
    }

    /// <summary>
    /// 从 YAML 文本字符串反序列化分析配置模型。
    /// 自动兼容直接为 AnalysisConfig 或包裹在根节点 analysis 下的两种结构。
    /// </summary>
    /// <param name="yamlContent">YAML 文本内容。</param>
    /// <param name="baseDirectory">相对路径解析基准目录。</param>
    /// <returns>解析并完成路径归一化的 AnalysisConfig 实例。</returns>
    public static AnalysisConfig FromYaml(string yamlContent, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
            throw new ArgumentException("YAML 内容不可为空。", nameof(yamlContent));

        string effectiveBaseDir = string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(baseDirectory);

        AnalysisConfig? config = null;

        // 1. 尝试直接反序列化为 AnalysisConfig
        try
        {
            config = YamlSerializer.Deserialize<AnalysisConfig>(yamlContent);
        }
        catch
        {
            // ignored
        }

        // 2. 若直接反序列化为空或未解析到核心属性，尝试作为根节点 analysis 解包
        if (config == null || (config.Analyzers.Count == 0 && config.Exporters.Count == 0 && config.Options.BenchmarkSymbol == null))
        {
            try
            {
                var wrapper = YamlSerializer.Deserialize<AnalysisWrapper>(yamlContent);
                if (wrapper?.Analysis != null)
                {
                    config = wrapper.Analysis;
                }
            }
            catch
            {
                // ignored
            }
        }

        config ??= new AnalysisConfig();
        config.Options ??= new AnalysisOptionsConfig();
        config.Analyzers ??= new List<AnalyzerNodeConfig>();
        config.Exporters ??= new List<ExporterNodeConfig>();

        // 3. 对导出器中的相对路径做基准路径归一化
        NormalizeExporterPaths(config, effectiveBaseDir);

        return config;
    }

    private static void NormalizeExporterPaths(AnalysisConfig config, string baseDir)
    {
        if (config.Exporters == null) return;

        var targetPathKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "outputpath", "htmloutputpath"
        };

        foreach (var exporterNode in config.Exporters)
        {
            if (exporterNode.Params == null) continue;

            var updates = new List<(string Key, string FullPath)>();
            foreach (var (key, rawVal) in exporterNode.Params)
            {
                string normKey = key.Replace("_", "");
                if (targetPathKeys.Contains(normKey) && rawVal is string pathStr && !string.IsNullOrWhiteSpace(pathStr))
                {
                    if (!Path.IsPathRooted(pathStr))
                    {
                        updates.Add((key, Path.GetFullPath(Path.Combine(baseDir, pathStr))));
                    }
                }
            }

            foreach (var (key, fullPath) in updates)
            {
                exporterNode.Params[key] = fullPath;
            }
        }
    }

    private class AnalysisWrapper
    {
        public AnalysisConfig? Analysis { get; set; }
    }
}
