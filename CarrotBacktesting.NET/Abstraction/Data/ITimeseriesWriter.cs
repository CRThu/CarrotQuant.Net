using System;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 时序数据写入与生命周期调度契约（面向实盘引擎与数据录入管道）。
/// 负责在内存中追加时间步截面、标的扩列与驱动持久化原子 Commit，保证底层 Storage 与 Cache 完全一致。
/// 与纯只读的 <see cref="ITimeseriesRegistry"/> 形成读写分离（CQRS）。
/// </summary>
public interface ITimeseriesWriter
{
    /// <summary>
    /// 向指定字段追加截面数据并同步推进元数据时间轴（纯物理内存操作，零 I/O 抖动）。
    /// </summary>
    /// <typeparam name="T">非托管数值类型。</typeparam>
    /// <param name="fieldName">字段名称（例如 "close"、"volume"）。</param>
    /// <param name="timestamp">当前时间步的时间戳/交易日。</param>
    /// <param name="rowValues">全市场标的在该时间步的截面数据，长度必须等于代码总数。</param>
    void AppendRow<T>(string fieldName, DateTime timestamp, ReadOnlySpan<T> rowValues) where T : unmanaged;

    /// <summary>
    /// 新增挂牌标的扩列（广播通知所有活跃字段拓宽列并填充 NaN 哨兵）。
    /// </summary>
    /// <param name="symbol">新增标的代码。</param>
    void AppendCol(string symbol);

    /// <summary>
    /// 将内存中未提交的脏数据原子同步提交至持久化存储，严格保证底层 Storage 与分块 Cache 完全一致。
    /// </summary>
    void Commit();
}
