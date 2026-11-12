using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CarrotBacktesting.NET.Config
{
    /// <summary>
    /// YAML 配置序列化器。
    /// 负责将强类型配置对象（<see cref="EngineConfig"/> 与 <see cref="DataConfig"/>）序列化为标准下划线命名规范的 YAML 文本或落盘保存。
    /// 与 <see cref="EngineConfigLoader"/> 形成对等的序列化与反序列化闭环。
    /// </summary>
    public static class EngineConfigSerializer
    {
        private static readonly ISerializer Serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        /// <summary>
        /// 将完整的引擎配置序列化为 YAML 格式字符串。
        /// </summary>
        /// <param name="config">引擎配置实例。</param>
        /// <returns>格式化后的 YAML 文本内容。</returns>
        public static string Serialize(EngineConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return Serializer.Serialize(config);
        }

        /// <summary>
        /// 将独立的数据层配置序列化为 YAML 格式字符串（常用于生成共享 data.yaml）。
        /// </summary>
        /// <param name="dataConfig">数据层配置实例。</param>
        /// <returns>格式化后的 YAML 文本内容。</returns>
        public static string Serialize(DataConfig dataConfig)
        {
            ArgumentNullException.ThrowIfNull(dataConfig);
            return Serializer.Serialize(dataConfig);
        }

        /// <summary>
        /// 将引擎配置序列化并保存至指定物理文件。
        /// </summary>
        /// <param name="config">引擎配置实例。</param>
        /// <param name="filePath">输出文件物理路径。</param>
        public static void Save(EngineConfig config, string filePath)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string yaml = Serialize(config);
            File.WriteAllText(filePath, yaml);
        }

        /// <summary>
        /// 将独立数据层配置序列化并保存至指定物理文件。
        /// </summary>
        /// <param name="dataConfig">数据层配置实例。</param>
        /// <param name="filePath">输出文件物理路径。</param>
        public static void Save(DataConfig dataConfig, string filePath)
        {
            ArgumentNullException.ThrowIfNull(dataConfig);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string yaml = Serialize(dataConfig);
            File.WriteAllText(filePath, yaml);
        }
    }
}
