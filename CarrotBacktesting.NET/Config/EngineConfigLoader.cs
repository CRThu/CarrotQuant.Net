using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CarrotBacktesting.NET.Config
{
    /// <summary>
    /// YAML 配置加载器，负责将 YAML 文件反序列化为强类型 EngineConfig 并自动解析相对路径。
    /// 支持直接内嵌 data 配置（映射对象），以及通过 data 多态指定外部独立 data.yaml 路径实现跨策略复用。
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

            // 首先探测 data 节点是外部文件路径（标量字符串）还是内嵌配置（映射对象）
            var rawConfig = Deserializer.Deserialize<RawEngineConfig>(yamlContent) ?? new RawEngineConfig();

            EngineConfig config;

            if (rawConfig.Data is string externalDataPath && !string.IsNullOrWhiteSpace(externalDataPath))
            {
                // 1. 外部独立数据配置引用：data: "path/to/data.yaml"
                config = new EngineConfig
                {
                    Engine = rawConfig.Engine ?? new EngineSettings(),
                    Broker = rawConfig.Broker ?? new BrokerConfig()
                };

                string fullDataPath = Path.IsPathRooted(externalDataPath)
                    ? externalDataPath
                    : Path.GetFullPath(Path.Combine(configDir, externalDataPath));

                if (!File.Exists(fullDataPath))
                {
                    throw new FileNotFoundException($"External data configuration file not found: {fullDataPath}");
                }

                string dataConfigDir = Path.GetDirectoryName(fullDataPath) ?? string.Empty;
                string dataYamlContent = File.ReadAllText(fullDataPath);

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
                    // 规范化 root 路径：若是相对路径则以外部 data.yaml 所在目录为基准合并
                    if (!string.IsNullOrWhiteSpace(config.Data.Root))
                    {
                        config.Data.Root = Path.IsPathRooted(config.Data.Root)
                            ? Path.GetFullPath(config.Data.Root)
                            : Path.GetFullPath(Path.Combine(dataConfigDir, config.Data.Root));
                    }
                }
            }
            else
            {
                // 2. 内嵌 data 配置映射对象
                config = Deserializer.Deserialize<EngineConfig>(yamlContent) ?? new EngineConfig();

                if (!string.IsNullOrWhiteSpace(config.Data.Root))
                {
                    config.Data.Root = Path.IsPathRooted(config.Data.Root)
                        ? Path.GetFullPath(config.Data.Root)
                        : Path.GetFullPath(Path.Combine(configDir, config.Data.Root));
                }
            }

            return config;
        }

        private class RawEngineConfig
        {
            public EngineSettings? Engine { get; set; }
            public BrokerConfig? Broker { get; set; }
            public object? Data { get; set; }
        }

        private class DataWrapper
        {
            public DataConfig? Data { get; set; }
        }
    }
}
