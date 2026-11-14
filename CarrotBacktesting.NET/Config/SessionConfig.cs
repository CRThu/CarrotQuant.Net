using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Config
{
    /// <summary>
    /// 全局会话配置根（SessionConfig Root）。
    /// 作为全任务总配置（All-in-one Configuration）的一体化管理契约，统筹四大正交子系统：
    /// 1. <see cref="Engine"/>: 引擎运行模式与起止时间 (<see cref="EngineConfig"/>)
    /// 2. <see cref="Broker"/>: 虚拟经纪商资金、费率与风控 (<see cref="BrokerConfig"/>)
    /// 3. <see cref="Data"/>: 数据层物理存储与时序/事件/维表挂载 (<see cref="DataConfig"/>)
    /// 4. <see cref="Pipeline"/>: 策略流水线拓扑编排 (<see cref="PipelineConfiguration"/>)
    /// </summary>
    public class SessionConfig
    {
        /// <summary>
        /// 引擎运行参数设置（模式、起止时间）。
        /// </summary>
        public EngineConfig Engine { get; set; } = new();

        /// <summary>
        /// 虚拟经纪商设置（资金账户、结算天数、成本滑点、风控与资产乘数）。
        /// </summary>
        public BrokerConfig Broker { get; set; } = new();

        /// <summary>
        /// 数据层配置。
        /// </summary>
        public DataConfig Data { get; set; } = new();

        /// <summary>
        /// 策略流水线配置（可选）。支持直接内嵌配置对象，也支持赋值为外部共享配置文件的相对路径（如 "shared/pipeline.yaml"）。
        /// </summary>
        public PipelineConfiguration? Pipeline { get; set; }

        #region 统一门面方法 (Unified Facade APIs)

        /// <summary>
        /// 从指定 YAML 配置文件物理路径加载会话配置，自动解析相对路径与多态外链配置。
        /// </summary>
        /// <param name="configFilePath">YAML 配置文件物理路径。</param>
        /// <returns>解析并完成物理路径合并的完整 SessionConfig 实例。</returns>
        public static SessionConfig Load(string configFilePath) => SessionConfigLoader.Load(configFilePath);

        /// <summary>
        /// 从 YAML 格式文本字符串反序列化会话配置模型，支持传入可选的基础目录解析相对路径。
        /// </summary>
        /// <param name="yamlContent">YAML 文本内容。</param>
        /// <param name="baseDirectory">相对路径解析基准目录（若为 null 则缺省为当前运行目录）。</param>
        /// <returns>反序列化并完成路径归一化的 SessionConfig 实例。</returns>
        public static SessionConfig FromYaml(string yamlContent, string? baseDirectory = null) => SessionConfigLoader.FromYaml(yamlContent, baseDirectory);

        /// <summary>
        /// 将当前会话配置实例序列化为标准下划线命名规范的 YAML 文本内容。
        /// </summary>
        public string ToYaml() => YamlSerializer.Serialize(this);

        /// <summary>
        /// 将当前会话配置实例序列化并保存至指定物理文件。
        /// </summary>
        /// <param name="filePath">输出文件物理路径。</param>
        public void Save(string filePath) => YamlSerializer.Save(this, filePath);

        #endregion
    }

    /// <summary>
    /// 引擎运行参数设置（纯引擎自身配置）。
    /// </summary>
    public class EngineConfig
    {
        public string Mode { get; set; } = "backtest";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
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

        /// <summary>
        /// 需要挂载的静态字段与多维属性字典表列表（如行业、概念、标的静态资料）。
        /// </summary>
        public List<FieldTableConfig> Fields { get; set; } = new();

        /// <summary>
        /// 将当前独立数据配置序列化为标准下划线命名规范的 YAML 文本内容。
        /// </summary>
        public string ToYaml() => YamlSerializer.Serialize(this);

        /// <summary>
        /// 将当前独立数据配置序列化并保存至指定物理文件。
        /// </summary>
        /// <param name="filePath">输出文件物理路径。</param>
        public void Save(string filePath) => YamlSerializer.Save(this, filePath);
    }

    /// <summary>
    /// 单个时序行情表配置。
    /// </summary>
    public class TimeseriesConfig
    {
        public string TableId { get; set; } = "";
        public List<string>? Fields { get; set; }

        /// <summary>
        /// 数据源时间戳时区标识符（遵循 IANA 标准如 "Asia/Shanghai", "America/New_York", "UTC"）。
        /// 若数据源时间戳为无时区本地时间，将根据此时区转换为全局单调的 UTC 时间戳。
        /// 若为 null 或空字符串，则假定数据源时间戳本身已为 UTC 时间。
        /// </summary>
        public string? TimeZone { get; set; }

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
        /// 缓存块压缩格式: "lz4" (默认，LZ4 原生块压缩), "none" (无压缩二进制块)。
        /// </summary>
        public string Compression { get; set; } = "lz4";

        /// <summary>
        /// 内存增量行定量落盘批次大小，默认 64 行。
        /// 实盘单行追加达到该行数阈值时自动触发批量 Commit 增量落盘；
        /// 设为 0 或负数则禁用定量落盘，仅在引擎停止或 Dispose 时触发保底 Commit。
        /// </summary>
        public int CommitBatchSize { get; set; } = 64;

        /// <summary>
        /// 最大常驻内存页数，默认为 4（必须为 2 的整数幂，如 2, 4, 8, 16 等）。
        /// 底层以环形槽位管理内存分页，最老冷数据自动淘汰释放，确保超长高频回测与实盘常驻内存严格守恒。
        /// 设为 0 或负数则禁用环形淘汰（全量常驻内存）。
        /// </summary>
        public int MaxPages { get; set; } = 4;

        /// <summary>
        /// 是否启用后台异步双缓冲预读（Prefetch），默认为 true。
        /// 仅在回测模式下生效；在实盘（Living）模式下自动物理禁用。
        /// </summary>
        public bool Prefetch { get; set; } = true;
    }

    /// <summary>
    /// 单个事件流配置。
    /// </summary>
    public class EventStreamConfig
    {
        public string Name { get; set; } = "";
        public string TableId { get; set; } = "";
        public List<string>? Fields { get; set; }

        /// <summary>
        /// 事件数据源时间戳时区标识符（遵循 IANA 标准如 "Asia/Shanghai", "America/New_York", "UTC"）。
        /// 若为 null 或空字符串，则假定事件数据源时间戳本身已为 UTC 时间。
        /// </summary>
        public string? TimeZone { get; set; }
    }

    /// <summary>
    /// 单个静态字段/维表配置。
    /// </summary>
    public class FieldTableConfig
    {
        public string Name { get; set; } = "";
        public string TableId { get; set; } = "";
        public string? KeyColumn { get; set; }
        public List<string>? ValueColumns { get; set; }
    }
}
