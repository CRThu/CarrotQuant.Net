namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义监控与日志接口。
/// 负责记录系统运行时的日志、统计指标以及发送告警通知。
/// </summary>
public interface IMonitor
{
    /// <summary>
    /// 记录一条指定级别的日志消息。
    /// </summary>
    /// <param name="message">日志内容。</param>
    /// <param name="level">日志级别。</param>
    void Log(string message, LogLevel level);

    /// <summary>
    /// 上报一个数值指标（用于性能监控或自定义分析）。
    /// </summary>
    /// <param name="metricName">指标名称。</param>
    /// <param name="value">指标数值。</param>
    void ReportMetric(string metricName, double value);

    /// <summary>
    /// 发送一条紧急告警消息。
    /// </summary>
    /// <param name="message">告警内容。</param>
    void Alert(string message);
}

/// <summary>
/// 定义日志的严重程度级别。
/// </summary>
public enum LogLevel 
{ 
    /// <summary>
    /// 普通信息日志。
    /// </summary>
    Info, 

    /// <summary>
    /// 警告日志，表示可能存在问题但系统仍可继续运行。
    /// </summary>
    Warning, 

    /// <summary>
    /// 错误日志，表示发生了严重问题。
    /// </summary>
    Error 
}
