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
    }

    public class AnalysisConfig
    {
        public ForwardReturnsConfig ForwardReturns { get; set; } = new();
        public List<string> Presenters { get; set; } = new();
    }

    public class ForwardReturnsConfig
    {
        public bool Enabled { get; set; } = true;
        public int BacktestDays { get; set; } = 30;
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