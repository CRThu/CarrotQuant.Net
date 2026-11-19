using System;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 通用强类型黑板接口，用于不同策略、组件间状态持久化与跨时间步数据交换。
/// 支持引用类型（class）与值类型（struct / readonly record struct），实现零堆分配状态存储。
/// 默认按类型单例索引（Singleton per Type），支持可选 Key 命名空间隔离。
/// </summary>
public interface IMarketState
{
    /// <summary>
    /// 获取强类型自定义状态/信号对象。
    /// </summary>
    /// <typeparam name="T">状态对象的类型（支持引用类型与值类型）。</typeparam>
    /// <param name="key">可选实例标识键（用于支持多策略实例输出同名结构隔离）。缺省时按类型全名索引。</param>
    /// <returns>若存在且类型匹配则返回实例；否则返回 default。</returns>
    T? GetState<T>(string? key = null);

    /// <summary>
    /// 设置强类型状态/信号对象。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="state">要发布的状态实例。</param>
    /// <param name="key">可选实例标识键。缺省时按类型全名索引。</param>
    void SetState<T>(T state, string? key = null);

    /// <summary>
    /// 设置强类型状态并显式记录该状态绑定的逻辑市场时间戳（防御跨 Bar 误判）。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="state">要发布的状态实例。</param>
    /// <param name="timestamp">状态产生的逻辑市场时间戳。</param>
    /// <param name="key">可选实例标识键。</param>
    void SetState<T>(T state, MarketTime timestamp, string? key = null);

    /// <summary>
    /// 尝试获取状态并返回该状态最后一次被更新的市场时间戳。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="state">输出的状态对象。</param>
    /// <param name="updatedTime">输出的状态最后更新时间。</param>
    /// <param name="key">可选实例标识键。</param>
    /// <returns>若存在且类型匹配则返回 true，否则返回 false。</returns>
    bool TryGetState<T>(out T? state, out MarketTime updatedTime, string? key = null);

    /// <summary>
    /// 检查指定类型（与 Key）的状态在黑板中是否存在。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="key">可选实例标识键。缺省时按类型全名索引。</param>
    bool HasState<T>(string? key = null);

    /// <summary>
    /// 清理黑板中的所有状态数据。
    /// </summary>
    void Clear();
}
