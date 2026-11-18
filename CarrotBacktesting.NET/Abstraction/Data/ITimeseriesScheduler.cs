namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 时序数据层分页调度与时间步驱动契约。
/// 仅供引擎层在离散时间步循环推进时，驱动常驻分块页面滑动淘汰与后台异步预读（Prefetch）。
/// 严格物理隔离于策略只读门面（<see cref="ITimeseriesRegistry"/>），杜绝策略层误调或越权驱动底层页表。
/// </summary>
public interface ITimeseriesScheduler
{
    /// <summary>
    /// 推进时间步至指定逻辑行，触发常驻分块页面滑动淘汰与后台异步预读（Prefetch）。
    /// </summary>
    /// <param name="currentLogicalRow">当前时间步对应的全局行索引。</param>
    void StepTo(int currentLogicalRow);
}
