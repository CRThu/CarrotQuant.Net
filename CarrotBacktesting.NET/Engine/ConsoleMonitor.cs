using System;
using System.Diagnostics;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Engine;

/// <summary>
/// 控制台监控与进度输出器，支持彩色日志输出与进度条呈现。
/// </summary>
public class ConsoleMonitor : IMonitor, IProgress<EngineProgress>
{
    private readonly LogLevel _minLevel;
    private readonly bool _showProgress;
    private readonly TextWriter _writer;
    private readonly object _consoleLock = new();

    private long _lastReportTimestamp = 0;
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(100); // 最小刷新时间间隔 100ms

    /// <summary>
    /// 初始化控制台监控器。
    /// </summary>
    /// <param name="minLevel">最低输出日志级别。</param>
    /// <param name="showProgress">是否开启吞吐与进度单行刷新看板。</param>
    /// <param name="writer">文本输出流（可选，缺省为 Console.Out）。</param>
    public ConsoleMonitor(LogLevel minLevel = LogLevel.Info, bool showProgress = true, TextWriter? writer = null)
    {
        _minLevel = minLevel;
        _showProgress = showProgress;
        _writer = writer ?? Console.Out;
    }

    /// <inheritdoc />
    public void Log(string message, LogLevel level)
    {
        if (level < _minLevel) return;

        lock (_consoleLock)
        {
            var color = level switch
            {
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => ConsoleColor.Cyan
            };

            Console.ForegroundColor = ConsoleColor.DarkGray;
            _writer.Write($"[{DateTime.Now:HH:mm:ss.fff}] ");
            Console.ForegroundColor = color;
            _writer.Write($"[{level.ToString().ToUpperInvariant(),-5}] ");
            Console.ResetColor();
            _writer.WriteLine(message);
        }
    }

    /// <inheritdoc />
    public void ReportMetric(string metricName, double value)
    {
        // 默认将度量指标路由至 Info 级日志输出
        Log($"Metric [{metricName}] = {value:F4}", LogLevel.Info);
    }

    /// <inheritdoc />
    public void Alert(string message)
    {
        lock (_consoleLock)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.DarkRed;
            _writer.Write($" [ALERT] {message} ");
            Console.ResetColor();
            _writer.WriteLine();
        }
    }

    /// <summary>
    /// 接收批处理进度更新。
    /// </summary>
    /// <param name="progress">进度数据对象。</param>
    public void Report(EngineProgress progress)
    {
        ReportProgress(progress.Current, progress.Total, progress.CurrentTime, progress.Elapsed);
    }

    /// <summary>
    /// 输出当前步数、总步数与基于物理耗时自适应计算的吞吐速率。
    /// </summary>
    public void ReportProgress(int currentStep, int totalSteps, MarketTime currentTime, TimeSpan elapsed)
    {
        double elapsedSec = elapsed.TotalSeconds;
        double speed = elapsedSec > 0.0 ? currentStep / elapsedSec : 0.0;
        ReportProgress(currentStep, totalSteps, currentTime, speed);
    }

    /// <summary>
    /// 输出当前步数、总步数与吞吐速率。
    /// </summary>
    /// <param name="currentStep">当前步数（从 1 开始）。</param>
    /// <param name="totalSteps">总步数。</param>
    /// <param name="currentTime">当前逻辑时钟。</param>
    /// <param name="barsPerSecond">执行吞吐速率（Bar/s）。</param>
    public void ReportProgress(int currentStep, int totalSteps, MarketTime currentTime, double barsPerSecond)
    {
        if (!_showProgress || totalSteps <= 0) return;

        bool isTerminal = currentStep >= totalSteps;
        long now = Stopwatch.GetTimestamp();

        // 纯物理时间自适应节流：未结束前，每 100ms 最多刷新一次控制台，自适应低频日线与超高频 Tick
        if (!isTerminal && _lastReportTimestamp != 0 && Stopwatch.GetElapsedTime(_lastReportTimestamp, now) < MinInterval)
        {
            return;
        }
        _lastReportTimestamp = now;

        double progressPct = Math.Min(100.0, (double)currentStep / totalSteps * 100.0);
        string speedStr = barsPerSecond >= 1_000_000
            ? $"{barsPerSecond / 1_000_000.0:F2}M Bar/s"
            : $"{barsPerSecond / 1_000.0:F1}K Bar/s";

        lock (_consoleLock)
        {
            _writer.Write($"\r[Progress: {progressPct,5:F1}%] Step: {currentStep}/{totalSteps} | Date: {currentTime:yyyy-MM-dd HH:mm} | Speed: {speedStr}   ");
            if (isTerminal)
            {
                _writer.WriteLine();
            }
        }
    }
}
