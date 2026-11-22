using System;
using CarrotBacktesting.NET.Abstraction.Indicators;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 布林带指标（Bollinger Bands, BOLL）。
    /// 遵循经典组合模式（Composite Pattern），自身实现 <see cref="IIndicator"/> 契约（默认索引指向中轨 Middle），
    /// 同时通过强类型只读属性暴露 <see cref="Upper"/>（上轨）、<see cref="Middle"/>（中轨 / SMA）与 <see cref="Lower"/>（下轨）子流 <see cref="IIndicatorStream"/>。
    /// 内部单次共享滑动窗口均值与标准差计算，内置 NaN 停牌冻结跳过（Skip & Freeze）防御。
    /// </summary>
    public sealed class BollingerBands : StreamIndicatorBase
    {
        private readonly int _period;
        private readonly double _k;
        private readonly double _invPeriod;
        private readonly double[] _window;
        private double _sum;
        private double _sumSq;
        private int _samples;
        private int _windowIndex;

        private readonly SubStreamIndicator _upperStream;
        private readonly SubStreamIndicator _middleStream;
        private readonly SubStreamIndicator _lowerStream;

        /// <summary>
        /// 获取上轨子流（Upper Band = Middle + K * StdDev）。
        /// </summary>
        public IIndicatorStream Upper => _upperStream;

        /// <summary>
        /// 获取中轨子流（Middle Band = SMA）。
        /// </summary>
        public IIndicatorStream Middle => _middleStream;

        /// <summary>
        /// 获取下轨子流（Lower Band = Middle - K * StdDev）。
        /// </summary>
        public IIndicatorStream Lower => _lowerStream;

        /// <summary>
        /// 获取布林带周期长度。
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// 获取标准差倍数。
        /// </summary>
        public double Multiplier => _k;

        /// <summary>
        /// 构建流式布林带指标实例。
        /// </summary>
        /// <param name="period">平滑周期（默认为 20，必须 >= 1）。</param>
        /// <param name="k">标准差倍数（默认为 2.0，必须 > 0）。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public BollingerBands(int period = 20, double k = 2.0, int historyCapacity = 8)
            : base(historyCapacity)
        {
            if (period < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "布林带周期必须大于等于 1。");
            }
            if (k <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(k), "标准差倍数 K 必须大于 0。");
            }

            _period = period;
            _k = k;
            _invPeriod = 1.0 / period;
            _window = new double[period];
            _sum = 0.0;
            _sumSq = 0.0;
            _samples = 0;
            _windowIndex = 0;

            _upperStream = new SubStreamIndicator(historyCapacity);
            _middleStream = new SubStreamIndicator(historyCapacity);
            _lowerStream = new SubStreamIndicator(historyCapacity);
        }

        /// <inheritdoc />
        public override void Update(params ReadOnlySpan<double> values)
        {
            if (values.IsEmpty) return;
            double close = values[0];

            // 停牌与脏数据防御（行业标准：冻结跳过 Skip & Freeze；支持差离值、收益率与价差实数序列）
            if (double.IsNaN(close) || double.IsInfinity(close))
            {
                return;
            }

            if (_samples < _period)
            {
                _window[_windowIndex] = close;
                _sum += close;
                _sumSq += close * close;
                _samples++;
                _windowIndex = (_windowIndex + 1) % _period;

                if (_samples == _period)
                {
                    CalculateBands();
                }
            }
            else
            {
                double oldest = _window[_windowIndex];
                _sum = _sum - oldest + close;
                _sumSq = _sumSq - (oldest * oldest) + (close * close);
                _window[_windowIndex] = close;
                _windowIndex = (_windowIndex + 1) % _period;

                CalculateBands();
            }
        }

        private void CalculateBands()
        {
            double mean = _sum * _invPeriod;

            // O(1) 增量方差计算：Var = E[X^2] - (E[X])^2
            // 彻底杜绝 O(N) 循环遍历；严格防范浮点舍入负数下溢开方异常
            double variance = (_sumSq * _invPeriod) - (mean * mean);
            if (variance < 0.0)
            {
                variance = 0.0;
            }

            double stdDev = Math.Sqrt(variance);
            double upper = mean + _k * stdDev;
            double lower = mean - _k * stdDev;

            _middleStream.Push(mean);
            _upperStream.Push(upper);
            _lowerStream.Push(lower);

            IsReady = true;
            // 默认主索引器指向中轨
            PushValue(mean);
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _sum = 0.0;
            _sumSq = 0.0;
            _samples = 0;
            _windowIndex = 0;
            Array.Clear(_window, 0, _period);

            _upperStream.Reset();
            _middleStream.Reset();
            _lowerStream.Reset();
        }
    }
}
