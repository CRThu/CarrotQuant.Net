namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 通用强类型黑板接口，用于不同策略、组件间零装箱异步数据交换。
/// 作为通用状态容器，不包含任何特定策略业务字段，保证通用底座的中立性。
/// 设计遵循第一性原理：默认按类型单例索引（Singleton per Type），支持可选 Key 命名空间隔离。
/// </summary>
public interface IMarketState
{
    /// <summary>
    /// 获取强类型自定义状态/信号对象。
    /// </summary>
    /// <typeparam name="T">状态对象的引用类型。</typeparam>
    /// <param name="key">可选实例标识键（用于支持多策略实例输出同名结构隔离）。缺省时按类型全名索引。</param>
    /// <returns>若存在且类型匹配则返回实例；否则返回 null。</returns>
    T? GetState<T>(string? key = null) where T : class;

    /// <summary>
    /// 设置强类型状态/信号对象。
    /// </summary>
    /// <typeparam name="T">状态对象的引用类型。</typeparam>
    /// <param name="state">要发布的状态实例（严禁为 null）。</param>
    /// <param name="key">可选实例标识键。缺省时按类型全名索引。</param>
    void SetState<T>(T state, string? key = null) where T : class;

    /// <summary>
    /// 检查指定类型（与 Key）的状态在黑板中是否存在。
    /// </summary>
    /// <typeparam name="T">状态对象的引用类型。</typeparam>
    /// <param name="key">可选实例标识键。缺省时按类型全名索引。</param>
    bool HasState<T>(string? key = null) where T : class;

    /// <summary>
    /// 清理黑板中的所有状态数据。
    /// </summary>
    void Clear();
}
