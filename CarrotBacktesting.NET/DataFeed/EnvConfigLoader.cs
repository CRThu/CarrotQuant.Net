using CarrotBacktesting.NET.DataFeed.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace CarrotBacktesting.NET.DataFeed
{
    public static class EnvConfigLoader
    {
        /// <summary>
        /// 加载并解析配置文件
        /// </summary>
        /// <param name="configPath">配置文件路径</param>
        /// <returns>配置对象</returns>
        /// <exception cref="FileNotFoundException">配置文件不存在</exception>
        /// <exception cref="InvalidOperationException">配置文件格式错误</exception>
        public static EnvConfig Load(string configPath = "env.xml")
        {
            // 验证文件存在性
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Configuration file not found: {Path.GetFullPath(configPath)}");
            }

            // 创建序列化器
            var serializer = new XmlSerializer(typeof(EnvConfig));

            using var stream = File.OpenRead(configPath);
            var config = (EnvConfig?)serializer.Deserialize(stream);

            // 验证配置文件
            if (config == null)
            {
                throw new InvalidOperationException("Configuration file is empty or invalid");
            }

            // 验证必要配置项
            if (string.IsNullOrWhiteSpace(config.Data.RawPath))
            {
                throw new InvalidOperationException("Data path configuration is required");
            }

            return config;
        }
    }
}
