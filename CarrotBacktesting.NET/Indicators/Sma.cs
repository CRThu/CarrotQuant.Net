using System;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 简单移动平均线（Simple Moving Average, SMA）。
    /// 基于双精度累加器（double _sum）与定长滑动窗口实现的增量 O(1) 流式指标。
    /// 预计算倒数乘法优化，内置 NaN 停牌冻结跳过（Skip & Freeze）防御，彻底免疫浮点舍入漂移。
    /// </summary>
    public sealed class Sma : StreamIndicatorBase
    {
        private readonly int _period;
        private readonly double _invPeriod;
        private readonly double[] _window;
        private double _sum;
        private int _samples;
        private int _windowIndex;

        /// <summary>
        /// 获取均线周期长度。
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// 构建流式 SMA 指标实例。
        /// </summary>
        /// <param name="period">平滑周期（必须 >= 1）。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public Sma(int period, int historyCapacity = 8) : base(historyCapacity)
        {
            if (period < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "SMA 周期必须大于等于 1。");
            }

            _period = period;
            _invPeriod = 1.0 / period;
            _window = new double[period];
            _sum = 0.0;
            _samples = 0;
            _windowIndex = 0;
        }

        /// <inheritdoc />
        public override void Update(params ReadOnlySpan<double> values)
        {
            if (values.IsEmpty) return;
            double close = values[0];

            // 停牌与脏数据防御（行业标准：冻结跳过 Skip & Freeze；支持价差、收益率等实数序列输入）
            if (double.IsNaN(close) || double.IsInfinity(close))
            {
                return;
            }

            if (_samples < _period)
            {
                _window[_windowIndex] = close;
                _sum += close;
                _samples++;
                _windowIndex = (_windowIndex + 1) % _period;

                if (_samples == _period)
                {
                    IsReady = true;
                    double currentSma = _sum * _invPeriod;
                    PushValue(currentSma);
                }
            }
            else
            {
                // O(1) 增量滑动更新：扣除滑出窗口的最旧值，加入当期最新值
                double oldest = _window[_windowIndex];
                _sum = _sum - oldest + close;
                _window[_windowIndex] = close;
                _windowIndex = (_windowIndex + 1) % _period;

                double currentSma = _sum * _invPeriod;
                PushValue(currentSma);
            }
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _sum = 0.0;
            _samples = 0;
            _windowIndex = 0;
            Array.Clear(_window, 0, _period);
        }
    }
}
