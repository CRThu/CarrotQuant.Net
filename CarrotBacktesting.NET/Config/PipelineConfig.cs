using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CarrotBacktesting.NET.Config;

/// <summary>
/// 策略管线执行拓扑模式。
/// </summary>
public enum PipelineMode
{
    /// <summary>
    /// 串行拓扑（默认）：子策略严格按先后顺序依次执行。
    /// </summary>
    Sequential,

    /// <summary>
    /// 并行拓扑：各子策略在逻辑上相互独立（并联）。
    /// </summary>
    Parallel
}

/// <summary>
/// 策略管线配置模型（YAML 序列化对齐）。
/// </summary>
public sealed class PipelineConfiguration
{
    /// <summary>
    /// 管线自定义名称。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 管线执行模式（"sequential" 串行或 "parallel" 并行）。缺省为串行。
    /// </summary>
    public PipelineMode Mode { get; set; } = PipelineMode.Sequential;

    /// <summary>
    /// 是否开启多核并发驱动（仅当 Mode 为 Parallel 时生效）。缺省为 false（默认安全单核执行）。
    /// </summary>
    public bool Concurrent { get; set; } = false;

    /// <summary>
    /// 策略节点列表。
    /// </summary>
    public List<StrategyNodeConfiguration> Pipeline { get; set; } = new();

    /// <summary>
    /// 从指定物理路径加载并反序列化策略管线 YAML 配置文件。
    /// </summary>
    public static PipelineConfiguration Load(string filePath) => PipelineConfigLoader.Load(filePath);

    /// <summary>
    /// 从 YAML 格式文本字符串反序列化策略管线配置模型。
    /// </summary>
    public static PipelineConfiguration FromYaml(string yamlContent) => PipelineConfigLoader.FromYaml(yamlContent);

    /// <summary>
    /// 将当前管线配置实例序列化为 YAML 格式文本。
    /// </summary>
    public string ToYaml() => YamlSerializer.Serialize(this);

    /// <summary>
    /// 将当前管线配置实例序列化并保存至指定物理文件。
    /// </summary>
    public void Save(string filePath) => YamlSerializer.Save(this, filePath);
}

/// <summary>
/// 策略节点配置项。
/// </summary>
public sealed class StrategyNodeConfiguration
{
    /// <summary>
    /// 节点自定义名称（对嵌套子管线有效）。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 子管线执行拓扑模式（仅当包含嵌套 pipeline 时有效）。缺省为串行。
    /// </summary>
    public PipelineMode Mode { get; set; } = PipelineMode.Sequential;

    /// <summary>
    /// 是否开启多核并发驱动（仅当 Mode 为 Parallel 且包含嵌套 pipeline 时有效）。缺省为 false。
    /// </summary>
    public bool Concurrent { get; set; } = false;

    /// <summary>
    /// 嵌套子管线节点列表（存在此字段时表示该节点为复合子管线）。
    /// </summary>
    public List<StrategyNodeConfiguration>? Pipeline { get; set; }

    /// <summary>
    /// C# 源代码文件路径（Roslyn 动态热编译免构建模式）。
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// 策略类型全名或短类名（适用于程序集中已定义的策略类型）。
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// 策略参数键值对（用于属性注入）。
    /// </summary>
    public Dictionary<string, object>? Params { get; set; }
}
