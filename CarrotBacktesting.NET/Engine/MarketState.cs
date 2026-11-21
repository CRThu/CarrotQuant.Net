using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Engine;

/// <summary>
/// 市场状态黑板（Market State Blackboard）权威默认实现。
/// 基于 <see cref="ConcurrentDictionary{TKey, TValue}"/> 实现多核并行管线下的线程安全访问。
/// 纯内存字典运行，按显式 Key 存取，热路径零额外开销。
/// </summary>
public sealed class MarketState : IMarketState, ICheckpointable<MarketStateSnapshot>
{
    private sealed record StateEntry(object Value, MarketTime? UpdatedTime);

    private readonly ConcurrentDictionary<string, StateEntry> _states = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public T? GetState<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_states.TryGetValue(key, out var entry) && entry.Value is T val)
        {
            return val;
        }

        return default;
    }

    /// <inheritdoc />
    public void SetState<T>(string key, T state, MarketTime? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(state);
        _states[key] = new StateEntry(state, timestamp);
    }

    /// <inheritdoc />
    public bool TryGetState<T>(string key, out T? state, out MarketTime? updatedTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_states.TryGetValue(key, out var entry) && entry.Value is T val)
        {
            state = val;
            updatedTime = entry.UpdatedTime;
            return true;
        }

        state = default;
        updatedTime = null;
        return false;
    }

    /// <inheritdoc />
    public bool HasState<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _states.TryGetValue(key, out var entry) && entry.Value is T;
    }

    /// <inheritdoc />
    public bool ClearState<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_states.TryGetValue(key, out var entry) && entry.Value is T)
        {
            return _states.TryRemove(key, out _);
        }

        return false;
    }

    /// <inheritdoc />
    public bool TryTakeState<T>(string key, out T? state, out MarketTime? updatedTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_states.TryRemove(key, out var entry) && entry.Value is T val)
        {
            state = val;
            updatedTime = entry.UpdatedTime;
            return true;
        }

        state = default;
        updatedTime = null;
        return false;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _states.Clear();
    }

    /// <summary>
    /// 保存市场黑板状态快照。
    /// 基于统一自描述条目集合序列化，保持完整类型与时间戳。
    /// </summary>
    public MarketStateSnapshot SaveState()
    {
        var entries = new List<MarketStateEntrySnapshot>(_states.Count);
        foreach (var kvp in _states)
        {
            string key = kvp.Key;
            object val = kvp.Value.Value;
            var time = kvp.Value.UpdatedTime;

            var valType = val.GetType();
            string typeName = valType.AssemblyQualifiedName ?? valType.FullName ?? valType.Name;
            string data = JsonSerializer.Serialize(val, valType);

            entries.Add(new MarketStateEntrySnapshot(key, typeName, data, time));
        }

        return new MarketStateSnapshot(entries);
    }

    /// <summary>
    /// 从快照恢复市场黑板状态。
    /// </summary>
    public void RestoreState(MarketStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _states.Clear();

        if (state.Entries == null || state.Entries.Count == 0)
        {
            return;
        }

        foreach (var entry in state.Entries)
        {
            var targetType = ResolveType(entry.Type);
            if (targetType == null)
            {
                throw new InvalidOperationException($"无法解析黑板条目 [{entry.Key}] 的目标类型: {entry.Type}");
            }

            var deserialized = JsonSerializer.Deserialize(entry.Data, targetType);
            if (deserialized == null)
            {
                throw new InvalidOperationException($"反序列化黑板条目 [{entry.Key}] 为类型 [{targetType.FullName}] 失败，得到空对象。");
            }

            _states[entry.Key] = new StateEntry(deserialized, entry.Time);
        }
    }

    private static readonly ConcurrentDictionary<string, Type> TypeResolutionCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 解析类型限定名至运行时 <see cref="Type"/>。
    /// 优先通过静态缓存提供 O(1) 访问；未命中时执行两级解析：
    /// 1. 尝试当前加载上下文标准解析；
    /// 2. 使用 CLR 官方委托管道在当前应用程序域已加载程序集中解析程序集简单名称与类型（原生支持泛型递归推导）。
    /// </summary>
    private static Type? ResolveType(string typeName)
    {
        if (TypeResolutionCache.TryGetValue(typeName, out var cachedType))
        {
            return cachedType;
        }

        var type = Type.GetType(typeName, throwOnError: false);
        if (type == null)
        {
            type = Type.GetType(
                typeName,
                assemblyResolver: asmName =>
                {
                    string targetName = asmName.Name ?? string.Empty;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (string.Equals(assembly.GetName().Name, targetName, StringComparison.OrdinalIgnoreCase))
                        {
                            return assembly;
                        }
                    }

                    return null;
                },
                typeResolver: (assembly, simpleName, ignoreCase) =>
                    assembly != null
                        ? assembly.GetType(simpleName, false, ignoreCase)
                        : Type.GetType(simpleName, false, ignoreCase),
                throwOnError: false
            );
        }

        if (type != null)
        {
            TypeResolutionCache.TryAdd(typeName, type);
        }

        return type;
    }
}
