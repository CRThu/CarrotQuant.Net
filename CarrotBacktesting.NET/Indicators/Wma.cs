using System;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 加权移动平均线（Weighted Moving Average, WMA）。
    /// 赋予近期价格线性递增权重的增量 O(1) 流式指标。
    /// 基于数学差分递推公式 W_t = W_{t-1} + N * Close_t - Sum_{t-1} 实现，热路径零堆分配（Zero GC）。
    /// </summary>
    public sealed class Wma : StreamIndicatorBase
    {
        private readonly int _period;
        private readonly double _invDenom;
        private readonly double[] _window;
        private double _sum;
        private double _weightedSum;
        private int _samples;
        private int _windowIndex;

        /// <summary>
        /// 获取均线周期长度。
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// 构建流式 WMA 指标实例。
        /// </summary>
        /// <param name="period">平滑周期（必须 >= 1）。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public Wma(int period, int historyCapacity = 8) : base(historyCapacity)
        {
            if (period < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "WMA 周期必须大于等于 1。");
            }

            _period = period;
            _invDenom = 2.0 / (period * (period + 1.0));
            _window = new double[period];
            _sum = 0.0;
            _weightedSum = 0.0;
            _samples = 0;
            _windowIndex = 0;
        }

        /// <inheritdoc />
        public override void Update(params ReadOnlySpan<double> values)
        {
            if (values.IsEmpty) return;
            double close = values[0];

            if (double.IsNaN(close) || double.IsInfinity(close))
            {
                return;
            }

            if (_samples < _period)
            {
                _window[_samples] = close;
                _sum += close;
                _samples++;

                if (_samples == _period)
                {
                    double wSum = 0.0;
                    for (int i = 0; i < _period; i++)
                    {
                        wSum += (i + 1) * _window[i];
                    }

                    _weightedSum = wSum;
                    _windowIndex = 0;
                    IsReady = true;
                    PushValue(_weightedSum * _invDenom);
                }
            }
            else
            {
                // O(1) 增量滑动递推：
                // W_t = W_{t-1} + N * Close_t - Sum_{t-1}
                // Sum_t = Sum_{t-1} - Oldest + Close_t
                double oldest = _window[_windowIndex];
                _weightedSum = _weightedSum + _period * close - _sum;
                _sum = _sum - oldest + close;
                _window[_windowIndex] = close;
                _windowIndex = (_windowIndex + 1) % _period;

                PushValue(_weightedSum * _invDenom);
            }
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _sum = 0.0;
            _weightedSum = 0.0;
            _samples = 0;
            _windowIndex = 0;
            Array.Clear(_window, 0, _period);
        }
    }
}
