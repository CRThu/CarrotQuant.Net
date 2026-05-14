namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 表示引擎当前的运行状态。
/// </summary>
public enum EngineStatus
{
    /// <summary>
    /// 引擎处于空闲状态，尚未启动。
    /// </summary>
    Idle,

    /// <summary>
    /// 引擎正在运行中。
    /// </summary>
    Running,

    /// <summary>
    /// 引擎已暂停。
    /// </summary>
    Paused,

    /// <summary>
    /// 任务已正常执行完成。
    /// </summary>
    Completed,

    /// <summary>
    /// 引擎运行过程中发生异常。
    /// </summary>
    Faulted
}

/// <summary>
/// 引擎统一抽象接口，负责驱动回测或实盘运行。
/// </summary>
public interface IEngine
{
    /// <summary>
    /// 获取引擎当前的运行状态。
    /// </summary>
    EngineStatus Status { get; }

    /// <summary>
    /// 获取当前任务的执行进度（0.0 到 1.0 之间）。
    /// </summary>
    double Progress { get; }

    /// <summary>
    /// 获取当前驱动的逻辑时间（回测中为虚拟时间，实盘中为实时时间）。
    /// </summary>
    DateTime CurrentTime { get; }

    /// <summary>
    /// 暂停引擎的运行。
    /// </summary>
    void Pause();

    /// <summary>
    /// 恢复已暂停的引擎运行。
    /// </summary>
    void Resume();

    /// <summary>
    /// 停止引擎的运行并释放相关资源。
    /// </summary>
    void Stop();

    /// <summary>
    /// 将引擎及关联策略的当前状态序列化为二进制数组，用于持久化或状态恢复。
    /// </summary>
    /// <returns>包含序列化状态数据的字节数组。</returns>
    byte[] SaveState();

    /// <summary>
    /// 从指定的二进制数组中恢复引擎及策略的状态。
    /// </summary>
    /// <param name="state">之前通过 SaveState 获取的字节数组。</param>
    void RestoreState(byte[] state);
}
