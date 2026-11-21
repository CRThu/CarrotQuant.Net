using System;
using CarrotBacktesting.NET.Abstraction.Indicators;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 复合多值指标专用的受控内部子流（Sub-Stream）。
    /// 实现 <see cref="IIndicatorStream"/> 契约，由母指标统一计算泵送值，对外部策略端提供只读切片与负数索引。
    /// 彻底剥离驱动更新契约，杜绝抛出不支持异常的伪实现。
    /// </summary>
    public sealed class SubStreamIndicator : IIndicatorStream
    {
        private readonly double[] _history;
        private readonly int _capacity;
        private int _head;
        private int _count;

        /// <inheritdoc />
        public bool IsReady { get; private set; }

        /// <inheritdoc />
        public double Current => this[0];

        /// <summary>
        /// 构建受控子流实例。
        /// </summary>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public SubStreamIndicator(int historyCapacity = 8)
        {
            if (historyCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(historyCapacity), "环形缓冲容量必须大于等于 1。");
            }

            _capacity = historyCapacity;
            _history = new double[_capacity];
            _head = -1;
            _count = 0;
            IsReady = false;
        }

        /// <inheritdoc />
        public double this[int offset]
        {
            get
            {
                // 1. 前瞻偏差防御：禁止访问未来 Bar
                if (offset > 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(offset),
                        $"时序 offset 越界 (offset = {offset} > 0)。仅允许访问当前 (0) 或历史步长 (< 0)，以防止前瞻偏差。");
                }

                // 2. 未就绪或历史回溯深度超出当前已记录的缓冲窗口时安全返回 NaN
                if (!IsReady || -offset >= _count)
                {
                    return double.NaN;
                }

                int idx = (_head + offset) % _capacity;
                if (idx < 0) idx += _capacity;
                return _history[idx];
            }
        }

        /// <summary>
        /// 供母体复合指标在当期推入最新有效值并标记就绪（内部高频内联）。
        /// </summary>
        /// <param name="value">最新计算值。</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal void Push(double value)
        {
            IsReady = true;
            _head = (_head + 1) % _capacity;
            _history[_head] = value;
            if (_count < _capacity)
            {
                _count++;
            }
        }

        /// <summary>
        /// 供母体复合指标统一重置状态。
        /// </summary>
        internal void Reset()
        {
            _head = -1;
            _count = 0;
            IsReady = false;
            Array.Clear(_history, 0, _capacity);
        }
    }
}
