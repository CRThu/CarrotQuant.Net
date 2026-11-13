using System;
using System.IO;

namespace CarrotBacktesting.NET.Config
{
    /// <summary>
    /// YAML 会话配置加载器，负责将 YAML 文件或文本反序列化为强类型 SessionConfig 并自动解析相对路径。
    /// 支持直接内嵌 data/pipeline 配置，以及通过多态指定外部独立 yaml 相对路径实现跨策略复用。
    /// 底层反序列化由全系统唯一的 <see cref="YamlSerializer"/> 统一驱动。
    /// </summary>
    public static class SessionConfigLoader
    {
        /// <summary>
        /// 从指定 YAML 路径加载会话配置，自动解析相对路径与多态外链。
        /// </summary>
        /// <param name="configFilePath">YAML 配置文件物理路径。</param>
        /// <returns>解析并完成物理路径合并的完整 SessionConfig 实例。</returns>
        public static SessionConfig Load(string configFilePath)
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

            return FromYaml(yamlContent, configDir);
        }

        /// <summary>
        /// 从 YAML 格式文本字符串反序列化会话配置模型，支持指定相对路径基准目录。
        /// </summary>
        /// <param name="yamlContent">YAML 文本内容。</param>
        /// <param name="baseDirectory">相对路径解析基准目录（若为空则默认为当前工作目录）。</param>
        /// <returns>解析并完成物理路径合并的完整 SessionConfig 实例。</returns>
        public static SessionConfig FromYaml(string yamlContent, string? baseDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(yamlContent))
            {
                throw new ArgumentException("YAML content cannot be null or empty.", nameof(yamlContent));
            }

            string effectiveBaseDir = string.IsNullOrWhiteSpace(baseDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(baseDirectory);

            // 首先探测各节点是外部文件路径（标量字符串）还是内嵌配置（映射对象）
            var rawConfig = YamlSerializer.Deserialize<RawSessionConfig>(yamlContent) ?? new RawSessionConfig();

            var config = new SessionConfig
            {
                Engine = rawConfig.Engine ?? new EngineConfig(),
                Broker = rawConfig.Broker ?? new BrokerConfig()
            };

            // 1. 处理 data 节点多态（外链文件路径 vs 内嵌对象）
            if (rawConfig.Data is string externalDataPath && !string.IsNullOrWhiteSpace(externalDataPath))
            {
                string fullDataPath = Path.IsPathRooted(externalDataPath)
                    ? externalDataPath
                    : Path.GetFullPath(Path.Combine(effectiveBaseDir, externalDataPath));

                if (!File.Exists(fullDataPath))
                {
                    throw new FileNotFoundException($"External data configuration file not found: {fullDataPath}");
                }

                string dataConfigDir = Path.GetDirectoryName(fullDataPath) ?? string.Empty;
                string dataYamlContent = File.ReadAllText(fullDataPath);

                DataConfig? externalData = null;
                try
                {
                    externalData = YamlSerializer.Deserialize<DataConfig>(dataYamlContent);
                }
                catch
                {
                    // ignored
                }

                if (externalData == null || string.IsNullOrWhiteSpace(externalData.Root))
                {
                    try
                    {
                        var wrapper = YamlSerializer.Deserialize<DataWrapper>(dataYamlContent);
                        if (wrapper?.Data != null)
                        {
                            externalData = wrapper.Data;
                        }
                    }
                    catch
                    {
                        // ignored
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
            else if (rawConfig.Data != null)
            {
                // 内嵌 data 配置
                string intermediateYaml = YamlSerializer.Serialize(rawConfig.Data);
                config.Data = YamlSerializer.Deserialize<DataConfig>(intermediateYaml) ?? new DataConfig();

                if (!string.IsNullOrWhiteSpace(config.Data.Root))
                {
                    config.Data.Root = Path.IsPathRooted(config.Data.Root)
                        ? Path.GetFullPath(config.Data.Root)
                        : Path.GetFullPath(Path.Combine(effectiveBaseDir, config.Data.Root));
                }
            }

            // 2. 处理 pipeline 节点多态（外链文件路径 vs 内嵌对象）
            if (rawConfig.Pipeline is string externalPipelinePath && !string.IsNullOrWhiteSpace(externalPipelinePath))
            {
                string fullPipelinePath = Path.IsPathRooted(externalPipelinePath)
                    ? externalPipelinePath
                    : Path.GetFullPath(Path.Combine(effectiveBaseDir, externalPipelinePath));

                if (!File.Exists(fullPipelinePath))
                {
                    throw new FileNotFoundException($"External pipeline configuration file not found: {fullPipelinePath}");
                }

                config.Pipeline = PipelineConfigLoader.Load(fullPipelinePath);
            }
            else if (rawConfig.Pipeline != null)
            {
                // 内嵌 pipeline 配置
                string intermediateYaml = YamlSerializer.Serialize(rawConfig.Pipeline);
                config.Pipeline = PipelineConfigLoader.FromYaml(intermediateYaml);
            }

            return config;
        }

        private class RawSessionConfig
        {
            public EngineConfig? Engine { get; set; }
            public BrokerConfig? Broker { get; set; }
            public object? Data { get; set; }
            public object? Pipeline { get; set; }
        }

        private class DataWrapper
        {
            public DataConfig? Data { get; set; }
        }
    }
}
