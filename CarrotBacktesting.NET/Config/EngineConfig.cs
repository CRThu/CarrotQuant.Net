using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Config
{
    /// <summary>
    /// 引擎与数据顶层配置模型，对应 YAML 配置文件。
    /// data 属性采用多态统一入口：既支持直接内嵌配置对象，也支持赋值为外部共享配置文件的相对路径（如 "shared/data.yaml"）。
    /// </summary>
    public class EngineConfig
    {
        /// <summary>
        /// 引擎运行参数设置（模式、起止时间、初始资金）。
        /// </summary>
        public EngineSettings Engine { get; set; } = new();

        /// <summary>
        /// 数据层配置。
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

        /// <summary>
        /// 是否启用二进制对齐缓存，默认为 true。
        /// </summary>
        public bool Cache { get; set; } = true;

        /// <summary>
        /// 分页行数大小，默认固定为 4096（必须为 2 的幂，如 1024, 2048, 4096, 8192 等）。
        /// </summary>
        public int PageSize { get; set; } = 4096;

        /// <summary>
        /// 缓存刷新策略:
        /// "lazy" (默认，首次缺失时构建一次，后续只要缓存存在则直接加载且不检查原始数据),
        /// "auto" (自动比对原始数据与缓存的头尾时间戳，存在新数据时增量补充),
        /// "force" (强制清空现有缓存并从原始数据全量重新构建)。
        /// </summary>
        public string Refresh { get; set; } = "lazy";

        /// <summary>
        /// 缓存块压缩格式: "lz4" (默认，3~5GB/s 超高速解压缩，节省 60%~70% 磁盘空间), "none" (无压缩裸二进制块)。
        /// </summary>
        public string Compression { get; set; } = "lz4";
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
