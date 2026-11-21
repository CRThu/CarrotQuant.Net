using System;
using CarrotBacktesting.NET.Abstraction.Indicators;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 增量流式指标抽象基类。
    /// 统筹微型定长环形缓冲区管理、负数时序索引器边界检查、未来函数偷价熔断（offset > 0 抛错）及状态机生命周期 Reset。
    /// 热路径零堆内存分配（Zero GC）。
    /// </summary>
    public abstract class StreamIndicatorBase : IIndicator
    {
        private readonly double[] _history;
        private readonly int _capacity;
        private int _head;
        private int _count;

        /// <inheritdoc />
        public bool IsReady { get; protected set; }

        /// <inheritdoc />
        public double Current => this[0];

        /// <summary>
        /// 初始化流式指标基类。
        /// </summary>
        /// <param name="historyCapacity">内部微型环形缓冲区容量（用于支持 this[-k] 回溯，默认 8）。必须 >= 1。</param>
        protected StreamIndicatorBase(int historyCapacity = 8)
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
        /// 将当前时间步计算得出的有效指标最新值推入微型环形缓冲。
        /// </summary>
        /// <param name="value">当期指标有效值。</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        protected void PushValue(double value)
        {
            _head = (_head + 1) % _capacity;
            _history[_head] = value;
            if (_count < _capacity)
            {
                _count++;
            }
        }

        /// <inheritdoc />
        public virtual void Reset()
        {
            _head = -1;
            _count = 0;
            IsReady = false;
            Array.Clear(_history, 0, _capacity);
        }

        /// <inheritdoc />
        public abstract void Update(double open, double high, double low, double close, double volume);
    }
}
