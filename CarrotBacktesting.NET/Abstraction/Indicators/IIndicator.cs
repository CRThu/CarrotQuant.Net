using System;

namespace CarrotBacktesting.NET.Abstraction.Indicators;

/// <summary>
/// 统一量化流式指标核心计算契约。
/// 继承自 <see cref="IIndicatorStream"/>，作为有状态指标计算单元的统一抽象。
/// 具备增量行情喂价计算（<see cref="Update"/>）与生命周期状态复位（<see cref="Reset"/>）能力。
/// </summary>
public interface IIndicator : IIndicatorStream
{
    /// <summary>
    /// 重置指标内部所有环形缓冲、累加和与状态，恢复未预热就绪状态（IsReady = false）。
    /// 保证策略多轮重跑与网格调优时退出零脏状态。
    /// </summary>
    void Reset();

    /// <summary>
    /// 统一增量喂价核心入口：每步传入完整的 OHLCV 市场行情进行状态递推。
    /// </summary>
    /// <param name="open">开盘价。</param>
    /// <param name="high">最高价。</param>
    /// <param name="low">最低价。</param>
    /// <param name="close">收盘价。</param>
    /// <param name="volume">成交量。</param>
    void Update(double open, double high, double low, double close, double volume);
}
