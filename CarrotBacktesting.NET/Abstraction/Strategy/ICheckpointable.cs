namespace CarrotBacktesting.NET.Abstraction.Strategy;

/// <summary>
/// 定义支持状态快照（Checkpoint）的组件契约，用于策略状态的保存与恢复。
/// </summary>
/// <typeparam name="TState">状态对象的类型。该对象应为支持序列化（如 MessagePack、JSON 等）的类或结构体。</typeparam>
public interface ICheckpointable<TState>
{
    /// <summary>
    /// 获取当前组件（如策略）的运行时状态快照。
    /// </summary>
    /// <returns>代表当前状态的快照对象。</returns>
    TState Save();

    /// <summary>
    /// 使用指定的快照数据恢复组件状态。
    /// </summary>
    /// <param name="state">之前保存的状态快照对象。</param>
    void Restore(TState state);
}
