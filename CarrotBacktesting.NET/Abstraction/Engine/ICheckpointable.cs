using System;
using System.Text.Json;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义组件支持二进制或字节流检查点保存与断点复原的基础契约。
/// </summary>
public interface ICheckpointable
{
    /// <summary>
    /// 将当前运行状态序列化为检查点字节数组。
    /// </summary>
    /// <returns>检查点字节数据。</returns>
    byte[] SaveCheckpoint();

    /// <summary>
    /// 从指定的检查点字节数据恢复运行状态。
    /// </summary>
    /// <param name="checkpoint">之前通过 SaveCheckpoint 获取的字节数组。</param>
    void RestoreCheckpoint(byte[] checkpoint);
}

/// <summary>
/// 定义组件支持强类型状态快照捕获与无损恢复的通用泛型能力契约。
/// 遵循单一职责与高内聚原则，使状态持久化能力与具体调度业务完全解耦。
/// </summary>
/// <typeparam name="TState">不可变状态模型类型约束。</typeparam>
public interface ICheckpointable<TState>
{
    /// <summary>
    /// 保存当前纯内存不可变状态快照。
    /// </summary>
    /// <returns>不可变状态数据模型。</returns>
    TState SaveState();

    /// <summary>
    /// 基于不可变状态快照精准复原组件状态机。
    /// </summary>
    /// <param name="state">状态快照数据模型。</param>
    void RestoreState(TState state);
}

/// <summary>
/// 检查点通用扩展工具，统筹强类型状态对象与 UTF-8 JSON 字节流的无损转换。
/// </summary>
public static class CheckpointExtensions
{
    /// <summary>
    /// 将强类型检查点状态序列化为 UTF-8 JSON 字节数组。
    /// </summary>
    public static byte[] ToJsonBytes<TState>(this ICheckpointable<TState> component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var state = component.SaveState();
        return JsonSerializer.SerializeToUtf8Bytes(state);
    }

    /// <summary>
    /// 从 UTF-8 JSON 字节数组反序列化并复原组件状态机。
    /// </summary>
    public static void RestoreFromJsonBytes<TState>(this ICheckpointable<TState> component, byte[] checkpoint)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(checkpoint);
        var state = JsonSerializer.Deserialize<TState>(checkpoint)
            ?? throw new InvalidOperationException($"无法反序列化类型为 {typeof(TState).Name} 的检查点数据。");
        component.RestoreState(state);
    }
}
