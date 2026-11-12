namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 时序注册表纯只读输出契约，供引擎层与策略层安全消费。
/// 统筹管理所有加载的时序字段，聚合全局市场元数据（Metadata）与各字段非托管只读矩阵（GetMatrix）。
/// 遵循“只读优先”原则：暴露的矩阵均为 IReadOnlyMatrix2D，禁止外部写入。
/// 写入与生命周期调度请参见写入契约 <see cref="ITimeseriesWriter"/>。
/// </summary>
public interface ITimeseriesRegistry
{
    /// <summary>
    /// 获取行情元数据（交易日历、股票列表及维度索引）。
    /// </summary>
    IMarketMetadata Metadata { get; }

    /// <summary>
    /// 获取指定字段的只读二维时序矩阵。
    /// 矩阵的行维度为 TradeDates（时间步），列维度为 Symbols（股票代码）。
    /// </summary>
    /// <typeparam name="T">
    /// 数据类型，必须为非托管类型（unmanaged），以匹配非托管内存物理布局。
    /// 通常为 <see cref="float"/> 或 <see cref="double"/>。
    /// </typeparam>
    /// <param name="fieldName">字段名称，例如 "close"、"open"、"volume"。</param>
    /// <returns>对应字段的只读时序矩阵，零拷贝访问。</returns>
    IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged;

    /// <summary>
    /// 检查是否存在指定字段的数据。
    /// </summary>
    /// <param name="fieldName">字段名称。</param>
    /// <returns>若字段已加载则返回 true，否则返回 false。</returns>
    bool HasField(string fieldName);

    /// <summary>
    /// 推进时间步至指定逻辑行，触发常驻分块页面滑动淘汰与后台异步预读（Prefetch）。
    /// 默认空实现，由支持滑动分块调度的实现重写。
    /// </summary>
    /// <param name="currentLogicalRow">当前时间步对应的全局行索引。</param>
    void StepTo(int currentLogicalRow) { }
}
