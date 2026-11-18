using System;
using System.Collections.Concurrent;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Engine;

/// <summary>
/// 市场状态黑板（Market State Blackboard）权威默认实现。
/// 基于 <see cref="ConcurrentDictionary{TKey, TValue}"/> 实现多核并行管线下的绝对线程安全。
/// 支持引用类型与值类型（struct / record struct），并维护每个状态的最后更新逻辑时间戳。
/// </summary>
public sealed class MarketState : IMarketState
{
    private sealed record StateEntry(object Value, DateTime UpdatedTime);

    private readonly ConcurrentDictionary<string, StateEntry> _states = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public T? GetState<T>(string? key = null)
    {
        var actualKey = ResolveKey<T>(key);
        if (_states.TryGetValue(actualKey, out var entry) && entry.Value is T val)
        {
            return val;
        }
        return default;
    }

    /// <inheritdoc />
    public void SetState<T>(T state, string? key = null)
    {
        SetState(state, DateTime.UtcNow, key);
    }

    /// <inheritdoc />
    public void SetState<T>(T state, DateTime timestamp, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var actualKey = ResolveKey<T>(key);
        _states[actualKey] = new StateEntry(state, timestamp);
    }

    /// <inheritdoc />
    public bool TryGetState<T>(out T? state, out DateTime updatedTime, string? key = null)
    {
        var actualKey = ResolveKey<T>(key);
        if (_states.TryGetValue(actualKey, out var entry) && entry.Value is T val)
        {
            state = val;
            updatedTime = entry.UpdatedTime;
            return true;
        }

        state = default;
        updatedTime = DateTime.MinValue;
        return false;
    }

    /// <inheritdoc />
    public bool HasState<T>(string? key = null)
    {
        var actualKey = ResolveKey<T>(key);
        return _states.TryGetValue(actualKey, out var entry) && entry.Value is T;
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
