using System;

namespace CarrotBacktesting.NET.Abstraction.Indicators;

/// <summary>
/// 统一量化流式指标核心计算契约。
/// 继承自 <see cref="IIndicatorStream"/>，作为有状态指标计算单元的统一抽象。
/// 遵循第一性原理，全系统收敛为单一统一增量推进入口 <see cref="Update"/>，
/// 原生支持单 Field（单值序列算子）与多 Field（多维复合算子），
/// 栈上传递，严格零 GC 堆分配。
/// </summary>
public interface IIndicator : IIndicatorStream
{
    /// <summary>
    /// 重置指标内部所有环形缓冲、累加和与状态机，恢复未预热就绪状态（IsReady = false）。
    /// 保证策略多轮重跑与网格调优时退出零脏状态。
    /// </summary>
    void Reset();

    /// <summary>
    /// 全系统唯一统一增量推进入口。
    /// 单 Field 输入时 values.Length == 1，按 values[0] 消费；
    /// 多 Field 输入时 values 严格按注册时声明的字段顺序传递，按 values[0..k-1] 确定性解构。
    /// 栈上传递，100% 零 GC 堆分配。
    /// </summary>
    /// <param name="values">当前时间步的字段输入切片。</param>
    void Update(params ReadOnlySpan<double> values);
}
