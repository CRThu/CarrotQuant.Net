namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 事件注册表接口。
/// 统筹管理所有已加载的异构事件流（如复权因子表、分红表、龙虎榜表等）。
/// </summary>
public interface IEventRegistry
{
    /// <summary>
    /// 获取指定名称的强类型事件表。
    /// </summary>
    /// <typeparam name="T">事件数据实体类型。</typeparam>
    /// <param name="streamName">事件流名称（如 "adjustments"）。</param>
    /// <returns>类型安全的事件表实例。</returns>
    IEventTable<T> GetTable<T>(string streamName) where T : class;

    /// <summary>
    /// 注册一个强类型事件表。
    /// </summary>
    void Register<T>(string streamName, IEventTable<T> table) where T : class;

    /// <summary>
    /// 检查指定名称的事件表是否已注册。
    /// </summary>
    bool HasStream(string streamName);
}
