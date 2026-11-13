using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CarrotBacktesting.NET.Config;

/// <summary>
/// 策略管线 YAML 配置文件加载器（反序列化器）。
/// 统一负责将策略管线 YAML 文件或文本反序列化为强类型的 <see cref="PipelineConfiguration"/> 模型。
/// 严格遵循单一职责原则，与具体策略对象的实例化（StrategyPipelineFactory）物理隔离。
/// </summary>
public static class PipelineConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// 从 YAML 格式文本字符串反序列化策略管线配置模型。
    /// </summary>
    /// <param name="yamlContent">YAML 文本内容。</param>
    /// <returns>反序列化后的 PipelineConfiguration 强类型配置实例。</returns>
    public static PipelineConfiguration FromYaml(string yamlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);

        var config = Deserializer.Deserialize<PipelineConfiguration>(yamlContent);
        if (config == null || config.Pipeline == null || config.Pipeline.Count == 0)
        {
            throw new InvalidOperationException("YAML 配置未包含有效的 'pipeline' 节点列表。");
        }

        return config;
    }

    /// <summary>
    /// 从指定物理路径加载并反序列化策略管线 YAML 配置文件。
    /// </summary>
    /// <param name="filePath">YAML 配置文件物理路径。</param>
    /// <returns>反序列化后的 PipelineConfiguration 强类型配置实例。</returns>
    public static PipelineConfiguration Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"找不到指定的策略管线配置文件: '{fullPath}'", fullPath);
        }

        string yamlContent = File.ReadAllText(fullPath);
        return FromYaml(yamlContent);
    }
}
