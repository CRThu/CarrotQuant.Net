using System;

namespace CarrotBacktesting.NET.Abstraction.Indicators
{
    /// <summary>
    /// 统一增量流式指标核心契约。
    /// 专为量化引擎与多策略流式计算设计，热路径零堆分配（Zero GC），
    /// 内置定长微型环形缓冲、负数时序索引器、状态重置生命周期与防未来函数偷价熔断。
    /// </summary>
    public interface IStreamIndicator
    {
        /// <summary>
        /// 统一负数时序索引器。
        /// 0 代表当前时步，-1 代表上一期，-k 代表前 k 期。
        /// 传入正数 offset &gt; 0 时强制抛出 <see cref="ArgumentOutOfRangeException"/>，坚决杜绝未来函数偷价。
        /// </summary>
        /// <param name="offset">时序偏移量（&lt;= 0）。</param>
        /// <returns>指定历史时间步的指标数值；若未就绪或越界则返回 <see cref="double.NaN"/>。</returns>
        double this[int offset] { get; }

        /// <summary>
        /// 便捷属性：获取当前时间步指标值（等价于 this[0]）。
        /// </summary>
        double Current => this[0];

        /// <summary>
        /// 获取指标是否已达到所需滑动窗口周期并完成预热。
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// 重置指标内部所有环形缓冲、累加和与状态，恢复未预热就绪状态（IsReady = false）。
        /// 保证策略多轮重跑与网格调优时退出零脏状态。
        /// </summary>
        void Reset();

        /// <summary>
        /// 统一增量喂价核心入口：每步传入完整的 OHLCV 市场行情。
        /// 满足所有单值与多值指标的计算诉求，契约极致对称，杜绝伪实现与隐式兜底。
        /// </summary>
        /// <param name="open">开盘价。</param>
        /// <param name="high">最高价。</param>
        /// <param name="low">最低价。</param>
        /// <param name="close">收盘价。</param>
        /// <param name="volume">成交量。</param>
        void Update(double open, double high, double low, double close, double volume);
    }
}
