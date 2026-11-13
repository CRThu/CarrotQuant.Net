using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Config;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CarrotBackTesting.Net.UnitTest.Helper
{
    /// <summary>
    /// 测试专用的引擎配置构建器。
    /// 以流式 Fluent 方式直接组装强类型 EngineConfig 模型并通过标准序列化器生成规范的 YAML 文本或物理文件，
    /// 遵循单一事实来源（SSOT），杜绝手工拼接缩进字符串的维护陷阱。
    /// </summary>
    public class TestYamlConfigBuilder
    {
        private string? _externalDataPath;
        private static readonly ISerializer Serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        private readonly EngineConfig _config = new()
        {
            Engine = new EngineSettings
            {
                Mode = "backtest",
                StartDate = new DateTime(2025, 1, 1),
                EndDate = new DateTime(2025, 12, 31)
            },
            Broker = new BrokerConfig
            {
                InitialCash = 1000000.0,
                SettleDays = 1,
                AllowShort = false,
                FeeRate = 0.0003,
                MinFee = 5.0,
                Slippage = 0.001,
                VolumeLimit = 0.0,
                CheckLimit = false,
                Multiplier = 1.0,
                MarginRate = 1.0
            },
            Data = new DataConfig()
        };

        public TestYamlConfigBuilder WithEngineSettings(string mode = "backtest", DateTime? start = null, DateTime? end = null)
        {
            _config.Engine.Mode = mode;
            if (start.HasValue) _config.Engine.StartDate = start.Value;
            if (end.HasValue) _config.Engine.EndDate = end.Value;
            return this;
        }

        public TestYamlConfigBuilder WithBrokerSettings(
            double initialCash = 1000000.0,
            int settleDays = 1,
            bool allowShort = false,
            double feeRate = 0.0003,
            double minFee = 5.0,
            double slippage = 0.001,
            double volumeLimit = 0.0,
            bool checkLimit = false,
            double multiplier = 1.0,
            double marginRate = 1.0)
        {
            _config.Broker.InitialCash = initialCash;
            _config.Broker.SettleDays = settleDays;
            _config.Broker.AllowShort = allowShort;
            _config.Broker.FeeRate = feeRate;
            _config.Broker.MinFee = minFee;
            _config.Broker.Slippage = slippage;
            _config.Broker.VolumeLimit = volumeLimit;
            _config.Broker.CheckLimit = checkLimit;
            _config.Broker.Multiplier = multiplier;
            _config.Broker.MarginRate = marginRate;
            return this;
        }

        public TestYamlConfigBuilder WithRoot(string storageRoot)
        {
            _config.Data.Root = storageRoot;
            return this;
        }

        public TestYamlConfigBuilder WithExternalData(string relativeOrAbsolutePath)
        {
            _externalDataPath = relativeOrAbsolutePath;
            return this;
        }

        public TestYamlConfigBuilder AddTimeseries(
            string tableId,
            bool? cache = null,
            int? pageSize = null,
            string? refresh = null,
            string? compression = null,
            int? commitBatchSize = null,
            int? maxPages = null,
            bool? prefetch = null,
            List<string>? fields = null)
        {
            var ts = new TimeseriesConfig
            {
                TableId = tableId,
                Fields = fields
            };
            if (cache.HasValue) ts.Cache = cache.Value;
            if (pageSize.HasValue) ts.PageSize = pageSize.Value;
            if (refresh != null) ts.Refresh = refresh;
            if (compression != null) ts.Compression = compression;
            if (commitBatchSize.HasValue) ts.CommitBatchSize = commitBatchSize.Value;
            if (maxPages.HasValue) ts.MaxPages = maxPages.Value;
            if (prefetch.HasValue) ts.Prefetch = prefetch.Value;

            _config.Data.Timeseries.Add(ts);
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
        /// 基于强类型模型与生产序列化器直接生成符合规范的 YAML 文本内容。
        /// </summary>
        public string BuildYaml()
        {
            if (!string.IsNullOrWhiteSpace(_externalDataPath))
            {
                var dict = new Dictionary<string, object?>
                {
                    ["engine"] = _config.Engine,
                    ["broker"] = _config.Broker,
                    ["data"] = _externalDataPath.Replace("\\", "/")
                };
                return Serializer.Serialize(dict);
            }

            return _config.ToYaml();
        }

        /// <summary>
        /// 将构造出的 YAML 文本直接保存至指定物理文件，返回绝对路径。
        /// </summary>
        public string Save(string directory, string fileName = "config.yaml")
        {
            string fullPath = Path.Combine(directory, fileName);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(fullPath, BuildYaml());
            return fullPath;
        }

        /// <summary>
        /// 获取内部构造的强类型 EngineConfig 模型实例。
        /// </summary>
        public EngineConfig ToConfig() => _config;
    }
}
