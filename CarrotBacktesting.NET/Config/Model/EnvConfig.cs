using CarrotBacktesting.NET.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace CarrotBacktesting.NET.Config.Model
{
    public class EnvConfig
    {
        public DataConfig Data { get; set; } = new();

        public Dictionary<string, FieldDefinition> Fields { get; set; } = new();

        public CacheConfig Cache { get; set; } = new();

        public RuntimeConfig Runtime { get; set; } = new();

        public AnalysisConfig Analysis { get; set; } = new();

        public OutConfig Out { get; set; } = new();

        [YamlIgnore]
        private string _envFileDirectory = string.Empty;

        /// <summary>
        /// (内部方法) 在加载配置后，由 EnvConfigLoader 调用，用于设置路径解析的基准目录。
        /// </summary>
        internal void SetBaseDirectory(string envFilePath)
        {
            _envFileDirectory = Path.GetDirectoryName(envFilePath) ?? string.Empty;
        }
        
        /// <summary>
        /// 将配置文件中的相对路径，转换为基于项目目录的完整物理路径。
        /// </summary>
        /// <param name="pathInConfig">在env.yaml中配置的路径</param>
        /// <returns>完整的物理路径</returns>
        public string ResolvePath(string? pathInConfig)
        {
            if (string.IsNullOrWhiteSpace(pathInConfig))
            {
                return string.Empty;
            }

            // 步骤 a: 解析项目目录 (ProjectDir)
            // Path.GetFullPath 会自动处理 project_dir 是绝对路径还是相对路径
            string projectFullPath = Path.GetFullPath(Runtime.ProjectDir);

            // 步骤 b: 将最终路径与解析好的项目目录合并
            // Path.Combine 会正确处理 pathInConfig 是绝对路径的情况
            // (如果 pathInConfig 是 "C:\...", 它会忽略 projectFullPath)
            string finalPath = Path.Combine(projectFullPath, pathInConfig);

            return Path.GetFullPath(finalPath);
        }
    }

    public class DataConfig
    {
        public string RawPath { get; set; } = "";
        public StorageMode Mode { get; set; } = StorageMode.TimeSeries;
    }

    public class CacheConfig
    {
        public bool Enabled { get; set; } = true;
        public bool ForceRefresh { get; set; } = false;
    }

    public class RuntimeConfig
    {
        public int ThreadCount { get; set; } = 4;
        public string ProjectDir { get; set; } = ".";
    }

    public class OutConfig
    {
        public string Signal { get; set; } = "signal.json";
        public string Exporter { get; set; } = "report";
    }

    public class AnalysisConfig
    {
        public int SignalAnalysisDays { get; set; } = 30;
        public bool UseSignalAnalyzer { get; set; } = true;
        public bool UseConsoleExporter { get; set; } = true;
        public bool UsePlotExporter { get; set; } = true;
    }

    public class FieldDefinition
    {
        public FieldFormat Format { get; set; } = FieldFormat.Auto;
        public string? Alias { get; set; }
        public Dictionary<string, string> ValueMap { get; set; } = new();
    }

    public enum FieldFormat
    {
        Null,
        Auto,
        Float,
        String,
    }

    public enum StorageMode
    {
        /// <summary>
        /// 纵向存储 (按股票时间序列)，适用于大多数策略
        /// </summary>
        TimeSeries,
        /// <summary>
        /// 横向存储 (按交易日截面)，适用于纯截面策略
        /// </summary>
        MarketSnapshot
    }
}