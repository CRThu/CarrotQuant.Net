using System;
using System.Collections.Concurrent;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Engine;

/// <summary>
/// 市场状态黑板（Market State Blackboard）权威默认实现。
/// 基于 <see cref="ConcurrentDictionary{TKey, TValue}"/> 实现多核并行管线下的绝对线程安全。
/// 契约支持默认按强类型全名作为权威单例索引，并支持按自定义 Key 进行多实例命名空间隔离。
/// </summary>
public sealed class MarketState : IMarketState
{
    private readonly ConcurrentDictionary<string, object> _states = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public T? GetState<T>(string? key = null) where T : class
    {
        var actualKey = ResolveKey<T>(key);
        return _states.TryGetValue(actualKey, out var obj) ? obj as T : null;
    }

    /// <inheritdoc />
    public void SetState<T>(T state, string? key = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(state);
        var actualKey = ResolveKey<T>(key);
        _states[actualKey] = state;
    }

    /// <inheritdoc />
    public bool HasState<T>(string? key = null) where T : class
    {
        var actualKey = ResolveKey<T>(key);
        return _states.TryGetValue(actualKey, out var obj) && obj is T;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _states.Clear();
    }

    /// <summary>
    /// 解析实际存储键：未显式传入时采用类型的完整限定名（typeof(T).FullName）。
    /// </summary>
    private static string ResolveKey<T>(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            return key;
        }

        return typeof(T).FullName ?? typeof(T).Name;
    }
}
