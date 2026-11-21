using System;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 通用强类型市场状态黑板契约，专为策略间特征传递、信号协同与检查点无损恢复设计。
/// 基于键值对（Key-Value）提供纯内存强类型存取，统一使用显式 Key 进行命名空间隔离。
/// </summary>
public interface IMarketState
{
    /// <summary>
    /// 获取指定 Key 的强类型状态数据。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="key">实例标识键。</param>
    /// <returns>若存在且类型匹配则返回实例；否则返回 default。</returns>
    T? GetState<T>(string key);

    /// <summary>
    /// 设置指定 Key 的强类型状态数据，可附带显式逻辑市场时间戳。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="key">实例标识键。</param>
    /// <param name="state">要发布的状态实例。</param>
    /// <param name="timestamp">状态绑定的逻辑市场时间戳（未指定时为 null）。</param>
    void SetState<T>(string key, T state, MarketTime? timestamp = null);

    /// <summary>
    /// 尝试获取指定 Key 的强类型状态并返回更新时间戳（先 key 后 out 规范，未命中或无时间戳时为 null）。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="key">实例标识键。</param>
    /// <param name="state">输出的状态对象。</param>
    /// <param name="updatedTime">输出的状态最后更新时间（若不存在或未设置则为 null）。</param>
    /// <returns>若存在且类型匹配则返回 true，否则返回 false。</returns>
    bool TryGetState<T>(string key, out T? state, out MarketTime? updatedTime);

    /// <summary>
    /// 检查指定 Key 的状态在黑板中是否存在且类型匹配。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="key">实例标识键。</param>
    /// <returns>若存在且类型匹配返回 true，否则返回 false。</returns>
    bool HasState<T>(string key);

    /// <summary>
    /// 精确移除指定 Key 且类型匹配的状态数据。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="key">实例标识键。</param>
    /// <returns>若存在且类型匹配并成功移除返回 true，否则返回 false。</returns>
    bool ClearState<T>(string key);

    /// <summary>
    /// 按指定 Key 原子获取并移除状态对象（读后即焚，防重复消费），同时输出最后更新逻辑时间戳（若不存在或未设置则为 null）。
    /// </summary>
    /// <typeparam name="T">状态对象的类型。</typeparam>
    /// <param name="key">实例标识键。</param>
    /// <param name="state">输出的状态对象。</param>
    /// <param name="updatedTime">输出的状态最后更新时间（若不存在或未设置则为 null）。</param>
    /// <returns>若存在且类型匹配并成功取出返回 true，否则返回 false。</returns>
    bool TryTakeState<T>(string key, out T? state, out MarketTime? updatedTime);

    /// <summary>
    /// 清理黑板中的所有状态数据。
    /// </summary>
    void Clear();
}
