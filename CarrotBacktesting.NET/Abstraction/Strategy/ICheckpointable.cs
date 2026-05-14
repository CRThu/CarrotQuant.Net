namespace CarrotBacktesting.NET.Abstraction.Strategy;

/// <summary>
/// 定义策略的状态快照契约。
/// </summary>
/// <typeparam name="TState">状态对象的类型，应为可序列化（支持 MessagePack/JSON）的类或结构体。</typeparam>
public interface ICheckpointable<TState>
{
    /// <summary>
    /// 获取当前策略的运行时状态快照。
    /// </summary>
    TState Save();

    /// <summary>
    /// 使用指定的快照恢复策略状态。
    /// </summary>
    void Restore(TState state);
}
