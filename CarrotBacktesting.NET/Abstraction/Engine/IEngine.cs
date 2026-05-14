namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 引擎运行状态枚举
/// </summary>
public enum EngineStatus
{
    Idle,       // 空闲
    Running,    // 运行中
    Paused,     // 已暂停
    Completed,  // 已完成
    Faulted     // 发生异常
}

/// <summary>
/// 引擎统一抽象接口
/// </summary>
public interface IEngine
{
    // --- 状态监控属性 ---
    EngineStatus Status { get; }
    double Progress { get; }      // 进度: 0.0 - 1.0
    DateTime CurrentTime { get; } // 当前驱动的逻辑时间

    // --- 执行控制 ---
    // void Run(IStrategy strategy); // 注：IStrategy 尚未定义，暂时注释或待后续定义
    void Pause();
    void Resume();
    void Stop();

    // --- 持久化 (Save/Restore) ---
    /// <summary>
    /// 将引擎及策略当前状态序列化为二进制数组
    /// </summary>
    byte[] SaveState();

    /// <summary>
    /// 从二进制数组恢复引擎及策略状态
    /// </summary>
    void RestoreState(byte[] state);
}
