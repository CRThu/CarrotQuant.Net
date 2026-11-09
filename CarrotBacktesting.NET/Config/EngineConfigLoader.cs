using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CarrotBacktesting.NET.Config
{
    /// <summary>
    /// YAML 配置加载器，负责将 YAML 文件反序列化为强类型 EngineConfig 并自动解析相对路径。
    /// 支持直接内嵌 data 配置，以及通过 data_config 外链独立的 data.yaml 文件实现跨策略复用。
    /// </summary>
    public static class EngineConfigLoader
    {
        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        /// <summary>
        /// 从指定 YAML 路径加载引擎与数据配置，自动解析相对路径。
        /// </summary>
        /// <param name="configFilePath">YAML 配置文件物理路径。</param>
        /// <returns>解析并完成物理路径合并的完整 EngineConfig 实例。</returns>
        public static EngineConfig Load(string configFilePath)
        {
            if (string.IsNullOrWhiteSpace(configFilePath))
            {
                throw new ArgumentException("Config file path cannot be null or empty.", nameof(configFilePath));
            }

            string fullConfigPath = Path.GetFullPath(configFilePath);
            if (!File.Exists(fullConfigPath))
            {
                throw new FileNotFoundException($"Configuration file not found: {fullConfigPath}");
            }

            string configDir = Path.GetDirectoryName(fullConfigPath) ?? string.Empty;
            string yamlContent = File.ReadAllText(fullConfigPath);

            var config = Deserializer.Deserialize<EngineConfig>(yamlContent) ?? new EngineConfig();

            // 1. 处理外部独立 data.yaml 引用 (data_config)
            if (!string.IsNullOrWhiteSpace(config.DataConfig))
            {
                string dataConfigPath = Path.IsPathRooted(config.DataConfig)
                    ? config.DataConfig
                    : Path.GetFullPath(Path.Combine(configDir, config.DataConfig));

                if (!File.Exists(dataConfigPath))
                {
                    throw new FileNotFoundException($"External data configuration file not found: {dataConfigPath}");
                }

                string dataConfigDir = Path.GetDirectoryName(dataConfigPath) ?? string.Empty;
                string dataYamlContent = File.ReadAllText(dataConfigPath);

                DataConfig? externalData = null;
                try
                {
                    externalData = Deserializer.Deserialize<DataConfig>(dataYamlContent);
                }
                catch
                {
                    // ignored
                }

                if (externalData == null || string.IsNullOrWhiteSpace(externalData.Root))
                {
                    var wrapper = Deserializer.Deserialize<DataWrapper>(dataYamlContent);
                    if (wrapper?.Data != null)
                    {
                        externalData = wrapper.Data;
                    }
                }

                if (externalData != null)
                {
                    config.Data = externalData;
                    // 以外部 data.yaml 所在目录为基准合并 root 相对路径
                    if (!string.IsNullOrWhiteSpace(config.Data.Root) && !Path.IsPathRooted(config.Data.Root))
                    {
                        config.Data.Root = Path.GetFullPath(Path.Combine(dataConfigDir, config.Data.Root));
                    }
                }
            }
            else
            {
                // 2. 内嵌 data 配置的 root 相对路径合并
                if (!string.IsNullOrWhiteSpace(config.Data.Root) && !Path.IsPathRooted(config.Data.Root))
                {
                    config.Data.Root = Path.GetFullPath(Path.Combine(configDir, config.Data.Root));
                }
            }

            return config;
        }

        private class DataWrapper
        {
            public DataConfig? Data { get; set; }
        }
    }
}
