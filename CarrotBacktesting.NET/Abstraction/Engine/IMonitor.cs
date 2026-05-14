namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 监控/日志/告警接口
/// </summary>
public interface IMonitor
{
    void Log(string message, LogLevel level);
    void ReportMetric(string metricName, double value);
    void Alert(string message);
}

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel { Info, Warning, Error }
