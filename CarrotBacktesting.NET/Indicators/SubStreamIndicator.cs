using System;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 复合多值指标专用的受控内部子流（Sub-Stream）。
    /// 实现 <see cref="Abstraction.Indicators.IStreamIndicator"/> 契约，由母指标统一驱动泵送值，对外部调用端提供只读切片与负数索引。
    /// </summary>
    public sealed class SubStreamIndicator : StreamIndicatorBase
    {
        /// <summary>
        /// 构建受控子流实例。
        /// </summary>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public SubStreamIndicator(int historyCapacity = 8) : base(historyCapacity)
        {
        }

        /// <summary>
        /// 供母体复合指标在当期推入最新有效值并标记就绪（内部高频内联）。
        /// </summary>
        /// <param name="value">最新计算值。</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal void Push(double value)
        {
            IsReady = true;
            PushValue(value);
        }

        /// <summary>
        /// 受控子流由复合指标母体统一协调更新，直接调用将抛出不支持异常。
        /// </summary>
        public override void Update(double open, double high, double low, double close, double volume)
        {
            throw new NotSupportedException("受控子流指标由母体复合指标统一协调更新，严禁外部独立调用 Update。");
        }
    }
}
