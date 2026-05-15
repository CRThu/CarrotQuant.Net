namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 事件注册表接口。
/// 引擎通过此接口统一管理所有外部 KV 数据流（如复权因子、龙虎榜、分红等）。
/// </summary>
public interface IEventRegistry
{
    /// <summary>
    /// 获取指定名称和类型的事件提供器。
    /// 例如：Events.GetProvider&lt;AdjustmentFactor&gt;("adjustments")
    /// </summary>
    /// <typeparam name="T">事件数据的类型。</typeparam>
    /// <param name="streamName">数据流名称（如 "adjustments", "dragon_tiger"）。</param>
    /// <returns>事件提供器实例。</returns>
    IEventProvider<T> GetProvider<T>(string streamName) where T : class;

    /// <summary>
    /// 检查是否存在特定的数据流。
    /// </summary>
    /// <param name="streamName">数据流名称。</param>
    /// <returns>如果存在则返回 true，否则返回 false。</returns>
    bool HasStream(string streamName);
}
