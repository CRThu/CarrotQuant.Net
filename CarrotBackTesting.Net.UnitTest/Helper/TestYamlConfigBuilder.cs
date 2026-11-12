using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Config;

namespace CarrotBackTesting.Net.UnitTest.Helper
{
    /// <summary>
    /// 测试专用的 YAML 配置文件流式构建器。
    /// 允许单元测试以强类型方式自由排列组合配置项，一键生成符合规范的 YAML 文本或物理文件，
    /// 彻底告别多行字符串模板与魔法字符串硬编码。
    /// </summary>
    public class TestYamlConfigBuilder
    {
        private readonly EngineConfig _config = new()
        {
            Engine = new EngineSettings
            {
                Mode = "backtest",
                StartDate = new DateTime(2025, 1, 1),
                EndDate = new DateTime(2025, 12, 31),
                InitialCash = 1000000.0
            },
            Data = new DataConfig()
        };

        public TestYamlConfigBuilder WithEngineSettings(string mode = "backtest", DateTime? start = null, DateTime? end = null, double cash = 1000000.0)
        {
            _config.Engine.Mode = mode;
            if (start.HasValue) _config.Engine.StartDate = start.Value;
            if (end.HasValue) _config.Engine.EndDate = end.Value;
            _config.Engine.InitialCash = cash;
            return this;
        }

        public TestYamlConfigBuilder WithRoot(string storageRoot)
        {
            _config.Data.Root = storageRoot;
            return this;
        }

        public TestYamlConfigBuilder AddTimeseries(
            string tableId,
            bool cache = false,
            int pageSize = 4096,
            string refresh = "lazy",
            string compression = "lz4",
            int commitBatchSize = 64,
            int maxPages = 4,
            bool prefetch = false,
            List<string>? fields = null)
        {
            _config.Data.Timeseries.Add(new TimeseriesConfig
            {
                TableId = tableId,
                Cache = cache,
                PageSize = pageSize,
                Refresh = refresh,
                Compression = compression,
                CommitBatchSize = commitBatchSize,
                MaxPages = maxPages,
                Prefetch = prefetch,
                Fields = fields
            });
            return this;
        }

        public TestYamlConfigBuilder AddEvent(string name, string tableId, List<string>? fields = null)
        {
            _config.Data.Events.Add(new EventStreamConfig
            {
                Name = name,
                TableId = tableId,
                Fields = fields
            });
            return this;
        }

        public TestYamlConfigBuilder AddField(string name, string tableId, string? keyColumn = null, List<string>? valueColumns = null)
        {
            _config.Data.Fields.Add(new FieldTableConfig
            {
                Name = name,
                TableId = tableId,
                KeyColumn = keyColumn,
                ValueColumns = valueColumns
            });
            return this;
        }

        /// <summary>
        /// 获取生成的强类型配置模型。
        /// </summary>
        public EngineConfig BuildModel() => _config;

        /// <summary>
        /// 序列化为规范的 YAML 文本。
        /// </summary>
        public string BuildYaml() => EngineConfigSerializer.Serialize(_config);

        /// <summary>
        /// 将组合好的配置保存至指定目录下的 YAML 物理文件，返回该文件的完整绝对路径。
        /// </summary>
        public string Save(string directory, string fileName = "config.yaml")
        {
            string fullPath = Path.Combine(directory, fileName);
            EngineConfigSerializer.Save(_config, fullPath);
            return fullPath;
        }
    }
}
