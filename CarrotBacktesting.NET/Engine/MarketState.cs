using System;
using System.Collections.Concurrent;
using System.Text.Json;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Engine;

/// <summary>
/// 市场状态黑板（Market State Blackboard）权威默认实现。
/// 基于 <see cref="ConcurrentDictionary{TKey, TValue}"/> 实现多核并行管线下的绝对线程安全。
/// 支持引用类型与值类型（struct / record struct），并维护每个状态的最后更新逻辑时间戳。
/// </summary>
public sealed class MarketState : IMarketState, ICheckpointable<MarketStateSnapshot>
{
    private sealed record StateEntry(object Value, MarketTime UpdatedTime);

    private readonly ConcurrentDictionary<string, StateEntry> _states = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public T? GetState<T>(string? key = null)
    {
        var actualKey = ResolveKey<T>(key);
        if (_states.TryGetValue(actualKey, out var entry))
        {
            if (entry.Value is T val)
            {
                return val;
            }
            if (entry.Value is System.Text.Json.JsonElement elem)
            {
                try
                {
                    var deserialized = elem.Deserialize<T>();
                    if (deserialized != null)
                    {
                        _states[actualKey] = new StateEntry(deserialized, entry.UpdatedTime);
                        return deserialized;
                    }
                }
                catch
                {
                    // 反序列化降级
                }
            }
        }
        return default;
    }

    /// <inheritdoc />
    public void SetState<T>(T state, string? key = null)
    {
        SetState(state, MarketTime.MinValue, key);
    }

    /// <inheritdoc />
    public void SetState<T>(T state, MarketTime timestamp, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var actualKey = ResolveKey<T>(key);
        _states[actualKey] = new StateEntry(state, timestamp);
    }

    /// <inheritdoc />
    public bool TryGetState<T>(out T? state, out MarketTime updatedTime, string? key = null)
    {
        var actualKey = ResolveKey<T>(key);
        if (_states.TryGetValue(actualKey, out var entry))
        {
            if (entry.Value is T val)
            {
                state = val;
                updatedTime = entry.UpdatedTime;
                return true;
            }
            if (entry.Value is System.Text.Json.JsonElement elem)
            {
                try
                {
                    var deserialized = elem.Deserialize<T>();
                    if (deserialized != null)
                    {
                        _states[actualKey] = new StateEntry(deserialized, entry.UpdatedTime);
                        state = deserialized;
                        updatedTime = entry.UpdatedTime;
                        return true;
                    }
                }
                catch
                {
                    // 反序列化降级
                }
            }
        }

        state = default;
        updatedTime = MarketTime.MinValue;
        return false;
    }

    /// <summary>
    /// 保存市场黑板不可变状态快照。
    /// </summary>
    public MarketStateSnapshot SaveState()
    {
        var entries = new List<MarketStateEntrySnapshot>(_states.Count);
        foreach (var kvp in _states)
        {
            entries.Add(new MarketStateEntrySnapshot(kvp.Key, kvp.Value.Value, kvp.Value.UpdatedTime));
        }
        return new MarketStateSnapshot(entries);
    }

    /// <summary>
    /// 恢复市场黑板状态快照。
    /// </summary>
    public void RestoreState(MarketStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _states.Clear();
        if (state.Entries != null)
        {
            foreach (var e in state.Entries)
            {
                _states[e.Key] = new StateEntry(e.Value, e.UpdatedTime);
            }
        }
    }

    /// <inheritdoc />
    public bool HasState<T>(string? key = null)
    {
        var actualKey = ResolveKey<T>(key);
        if (!_states.TryGetValue(actualKey, out var entry))
        {
            return false;
        }
        if (entry.Value is T)
        {
            return true;
        }
        if (entry.Value is System.Text.Json.JsonElement elem)
        {
            try
            {
                var deserialized = elem.Deserialize<T>();
                if (deserialized != null)
                {
                    _states[actualKey] = new StateEntry(deserialized, entry.UpdatedTime);
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }
        return false;
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
