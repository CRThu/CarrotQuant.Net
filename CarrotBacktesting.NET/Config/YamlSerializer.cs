using System;
using System.Collections.Concurrent;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CarrotBacktesting.NET.Config;

/// <summary>
/// 全系统唯一的权威 YAML 序列化与反序列化基础设施。
/// 遵循单一事实来源（SSOT），集中统一管理 YamlDotNet 的命名规范、空值忽略与类型转换规则。
/// 消除项目中散落的重复构建实例，为所有配置实体提供标准化的泛型解析与持久化能力。
/// </summary>
public static class YamlSerializer
{
    private static readonly ConcurrentDictionary<Type, ISerializer> Serializers = new();
    private static readonly ConcurrentDictionary<Type, IDeserializer> Deserializers = new();

    private static readonly INamingConvention SnakeCase = UnderscoredNamingConvention.Instance;
    private static readonly INamingConvention CamelCase = CamelCaseNamingConvention.Instance;

    private static INamingConvention ResolveNamingConvention(Type type, INamingConvention? custom)
    {
        if (custom != null) return custom;
        // 策略管线配置默认使用驼峰命名，其余配置默认遵循蛇形下划线标准
        return (type == typeof(PipelineConfiguration) || type == typeof(StrategyNodeConfiguration))
            ? CamelCase
            : SnakeCase;
    }

    private static ISerializer GetSerializer(INamingConvention convention)
    {
        return Serializers.GetOrAdd(convention.GetType(), _ =>
            new SerializerBuilder()
                .WithNamingConvention(convention)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build());
    }

    private static IDeserializer GetDeserializer(INamingConvention convention)
    {
        return Deserializers.GetOrAdd(convention.GetType(), _ =>
            new DeserializerBuilder()
                .WithNamingConvention(convention)
                .IgnoreUnmatchedProperties()
                .Build());
    }

    /// <summary>
    /// 将对象序列化为 YAML 文本字符串。
    /// </summary>
    /// <param name="obj">需要序列化的对象实例。</param>
    /// <param name="namingConvention">可选的自定义命名规范，缺省时按类型智能派发。</param>
    /// <returns>格式化后的 YAML 文本内容。</returns>
    public static string Serialize(object obj, INamingConvention? namingConvention = null)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var convention = ResolveNamingConvention(obj.GetType(), namingConvention);
        return GetSerializer(convention).Serialize(obj);
    }

    /// <summary>
    /// 将对象序列化并保存至指定物理文件。
    /// </summary>
    /// <param name="obj">需要序列化的对象实例。</param>
    /// <param name="filePath">输出文件物理路径。</param>
    /// <param name="namingConvention">可选的自定义命名规范，缺省时按类型智能派发。</param>
    public static void Save(object obj, string filePath, INamingConvention? namingConvention = null)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string yaml = Serialize(obj, namingConvention);
        File.WriteAllText(filePath, yaml);
    }

    /// <summary>
    /// 从 YAML 文本字符串反序列化强类型对象（可能为 null）。
    /// </summary>
    /// <typeparam name="T">目标强类型。</typeparam>
    /// <param name="yamlContent">YAML 文本内容。</param>
    /// <param name="namingConvention">可选的自定义命名规范，缺省时按类型智能派发。</param>
    /// <returns>反序列化后的强类型对象实例或 null。</returns>
    public static T? Deserialize<T>(string yamlContent, INamingConvention? namingConvention = null)
    {
        if (string.IsNullOrWhiteSpace(yamlContent)) return default;
        var convention = ResolveNamingConvention(typeof(T), namingConvention);
        return GetDeserializer(convention).Deserialize<T>(yamlContent);
    }

    /// <summary>
    /// 从物理文件读取并反序列化强类型对象。
    /// </summary>
    /// <typeparam name="T">目标强类型。</typeparam>
    /// <param name="filePath">YAML 配置文件物理路径。</param>
    /// <param name="namingConvention">可选的自定义命名规范，缺省时按类型智能派发。</param>
    /// <returns>反序列化后的强类型对象实例。</returns>
    public static T? Load<T>(string filePath, INamingConvention? namingConvention = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"找不到指定的 YAML 配置文件: '{fullPath}'", fullPath);
        }

        string yamlContent = File.ReadAllText(fullPath);
        return Deserialize<T>(yamlContent, namingConvention);
    }
}
