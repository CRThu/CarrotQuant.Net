using System;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 批处理任务执行进度数据结构。
/// 仅记录客观事实状态，展示层据此计算派生指标（如平均吞吐、预计剩余时间 ETA 等）。
/// </summary>
/// <param name="Current">当前步数（从 1 开始）。</param>
/// <param name="Total">总步数。</param>
/// <param name="CurrentTime">当前时间戳。</param>
/// <param name="Elapsed">已耗费物理时间。</param>
public readonly record struct EngineProgress(int Current, int Total, DateTime CurrentTime, TimeSpan Elapsed)
{
    /// <summary>
    /// 获取当前任务完成百分比（0.0 ~ 1.0）。
    /// </summary>
    public double Percentage => Total > 0 ? (double)Current / Total : 0.0;
}
