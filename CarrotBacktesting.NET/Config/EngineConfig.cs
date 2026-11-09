using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Config
{
    /// <summary>
    /// 引擎与数据顶层配置模型，对应 YAML 配置文件。
    /// 支持直接内嵌 data 配置，或通过 data_config 引用独立的共享数据配置文件。
    /// </summary>
    public class EngineConfig
    {
        /// <summary>
        /// 引擎运行参数设置（模式、起止时间、初始资金）。
        /// </summary>
        public EngineSettings Engine { get; set; } = new();

        /// <summary>
        /// 可选的外部独立数据配置文件相对路径（如 "shared/data.yaml"）。
        /// </summary>
        public string? DataConfig { get; set; }

        /// <summary>
        /// 数据层配置。若配置了 DataConfig，则优先加载外部文件填充此项。
        /// </summary>
        public DataConfig Data { get; set; } = new();
    }

    /// <summary>
    /// 引擎运行参数设置。
    /// </summary>
    public class EngineSettings
    {
        public string Mode { get; set; } = "backtest";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public double InitialCash { get; set; } = 1000000.0;
    }

    /// <summary>
    /// 数据区与数据源配置，可作为独立 data.yaml 在多策略间复用。
    /// </summary>
    public class DataConfig
    {
        /// <summary>
        /// 统一存储数据区物理根目录路径。
        /// </summary>
        public string Root { get; set; } = "";

        /// <summary>
        /// 需要加载的时序行情矩阵列表（稠密数据）。
        /// </summary>
        public List<TimeseriesConfig> Timeseries { get; set; } = new();

        /// <summary>
        /// 需要挂载的异构事件流列表（稀疏数据）。
        /// </summary>
        public List<EventStreamConfig> Events { get; set; } = new();
    }

    /// <summary>
    /// 单个时序行情表配置。
    /// </summary>
    public class TimeseriesConfig
    {
        public string TableId { get; set; } = "";
        public List<string>? Fields { get; set; }
    }

    /// <summary>
    /// 单个事件流配置。
    /// </summary>
    public class EventStreamConfig
    {
        public string Name { get; set; } = "";
        public string TableId { get; set; } = "";
    }
}
