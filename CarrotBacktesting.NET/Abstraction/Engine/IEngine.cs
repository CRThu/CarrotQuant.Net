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
    /// 同步阻塞驱动引擎运行，直至任务完成或终止。
    /// </summary>
    void Run();

    /// <summary>
    /// 异步驱动引擎运行，支持外部协作式取消。
    /// </summary>
    /// <param name="cancellationToken">用于通知取消操作的令牌。</param>
    /// <returns>表示异步运行操作的任务。</returns>
    Task RunAsync(CancellationToken cancellationToken = default);

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
    /// 获取当前引擎持有的策略运行上下文。
    /// </summary>
    IEngineContext Context { get; }
}
