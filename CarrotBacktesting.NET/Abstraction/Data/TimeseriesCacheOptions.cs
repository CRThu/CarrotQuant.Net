using System;

namespace CarrotBacktesting.NET.Abstraction.Data
{
    /// <summary>
    /// 时序分块缓存持久化与非托管内存分页调度配置选项。
    /// 收敛分块行数、常驻页数限制、异步预读以及磁盘序列化策略。
    /// </summary>
    public sealed record TimeseriesCacheOptions
    {
        /// <summary>
        /// 默认配置选项（禁用本地缓存持久化，4096行分块，4页常驻，开启预读）。
        /// </summary>
        public static readonly TimeseriesCacheOptions Default = new();

        /// <summary>
        /// 本地分块缓存持久化物理根目录。为空则禁用本地磁盘分块读写。
        /// </summary>
        public string StorageRoot { get; init; } = "";

        /// <summary>
        /// 是否启用本地二进制对齐分块缓存，默认为 false。
        /// </summary>
        public bool EnableCache { get; init; } = false;

        /// <summary>
        /// 分页行数大小，默认固定为 4096（必须为 2 的幂，如 1024, 2048, 4096, 8192 等）。
        /// </summary>
        public int PageSize { get; init; } = 4096;

        /// <summary>
        /// 缓存刷新策略: Lazy (默认，首次缺失时构建一次), Auto (自动比对增量补充), Force (强制清空重建)。
        /// </summary>
        public CacheRefreshMode RefreshMode { get; init; } = CacheRefreshMode.Lazy;

        /// <summary>
        /// 缓存块压缩格式: Lz4 (默认，LZ4 原生块压缩), None (无压缩二进制块)。
        /// </summary>
        public CacheCompression Compression { get; init; } = CacheCompression.Lz4;

        /// <summary>
        /// 内存增量行定量落盘批次大小，默认 64 行。
        /// 实盘单行追加达到该行数阈值时自动触发批量 Commit 增量落盘；
        /// 设为 0 或负数则禁用定量落盘，仅在引擎停止或 Dispose 时触发保底 Commit。
        /// </summary>
        public int CommitBatchSize { get; init; } = 64;

        /// <summary>
        /// 最大常驻内存页数，默认为 4（必须为 2 的整数幂，如 2, 4, 8, 16 等）。
        /// 底层以环形槽位管理内存分页，最老冷数据自动淘汰释放，确保超长高频回测与实盘常驻内存严格守恒。
        /// 设为 0 或负数则禁用环形淘汰（全量常驻内存）。
        /// </summary>
        public int MaxPages { get; init; } = 4;

        /// <summary>
        /// 是否启用后台异步双缓冲预读（Prefetch），默认为 true。
        /// 仅在回测模式下生效；在实盘（Living）模式下自动物理禁用。
        /// </summary>
        public bool Prefetch { get; init; } = true;

        /// <summary>
        /// 是否运行于实盘（Living）模式。
        /// 实盘模式下将强行禁用异步预读（Prefetch），并开启增量单行追加与定量定时刷盘。
        /// </summary>
        public bool IsLive { get; init; } = false;

        /// <summary>
        /// 默认构造函数。
        /// </summary>
        public TimeseriesCacheOptions() { }

        /// <summary>
        /// 基于持久化根目录与缓存启用状态构建配置选项。
        /// </summary>
        public TimeseriesCacheOptions(string storageRoot, bool enableCache = true) : this()
        {
            StorageRoot = storageRoot ?? "";
            EnableCache = enableCache;
        }
    }
}
