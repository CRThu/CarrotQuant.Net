using CarrotBacktesting.NET.Config.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace CarrotBacktesting.NET.Config
{
    public static class EnvConfigLoader
    {

        public static EnvConfig Load(string configPath = "env.yaml")
        {
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Configuration file not found: {Path.GetFullPath(configPath)}");
            }

            var yamlContent = File.ReadAllText(configPath);

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var config = deserializer.Deserialize<EnvConfig>(yamlContent);

            if (string.IsNullOrWhiteSpace(config.Data.RawPath))
            {
                throw new InvalidOperationException("Data path configuration is required");
            }

            return config;
        }
    }
}