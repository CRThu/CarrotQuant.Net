using System;
using CarrotBacktesting.NET.Abstraction.Indicators;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 随机指标（KDJ / Stochastic Oscillator）。
    /// 基于过去 N 日最高价与最低价计算 RSV，并采用 EMA 增量平滑驱动的 O(1) 多值流式复合指标。
    /// 严格遵循中国 A 股通达信/同花顺标准（初始 K=50, D=50, J=3K-2D），热路径零堆分配（Zero GC）。
    /// </summary>
    public sealed class Kdj : StreamIndicatorBase
    {
        private readonly int _period;
        private readonly int _m1;
        private readonly int _m2;
        private readonly double _kAlpha;
        private readonly double _dAlpha;

        private readonly double[] _highWindow;
        private readonly double[] _lowWindow;
        private int _windowIndex;
        private int _samples;

        private double _k;
        private double _d;

        private readonly SubStreamIndicator _kSub;
        private readonly SubStreamIndicator _dSub;
        private readonly SubStreamIndicator _jSub;

        /// <summary>
        /// 获取 K 线子流指标。
        /// </summary>
        public IIndicatorStream K => _kSub;

        /// <summary>
        /// 获取 D 线子流指标。
        /// </summary>
        public IIndicatorStream D => _dSub;

        /// <summary>
        /// 获取 J 线子流指标。
        /// </summary>
        public IIndicatorStream J => _jSub;

        /// <summary>
        /// 获取计算 RSV 所需的周期长度 N（默认 9）。
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// 构建流式 KDJ 指标实例。
        /// </summary>
        /// <param name="period">RSV 周期 N（默认 9）。必须 >= 1。</param>
        /// <param name="m1">K 值平滑周期 M1（默认 3）。必须 >= 1。</param>
        /// <param name="m2">D 值平滑周期 M2（默认 3）。必须 >= 1。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public Kdj(int period = 9, int m1 = 3, int m2 = 3, int historyCapacity = 8)
            : base(historyCapacity)
        {
            if (period < 1) throw new ArgumentOutOfRangeException(nameof(period), "KDJ period 必须 >= 1。");
            if (m1 < 1) throw new ArgumentOutOfRangeException(nameof(m1), "KDJ m1 必须 >= 1。");
            if (m2 < 1) throw new ArgumentOutOfRangeException(nameof(m2), "KDJ m2 必须 >= 1。");

            _period = period;
            _m1 = m1;
            _m2 = m2;
            _kAlpha = 1.0 / m1;
            _dAlpha = 1.0 / m2;

            _highWindow = new double[period];
            _lowWindow = new double[period];
            _windowIndex = 0;
            _samples = 0;

            _k = 50.0;
            _d = 50.0;

            _kSub = new SubStreamIndicator(historyCapacity);
            _dSub = new SubStreamIndicator(historyCapacity);
            _jSub = new SubStreamIndicator(historyCapacity);
        }

        /// <inheritdoc />
        public override void Update(double open, double high, double low, double close, double volume)
        {
            if (double.IsNaN(high) || double.IsNaN(low) || double.IsNaN(close) ||
                double.IsInfinity(high) || double.IsInfinity(low) || double.IsInfinity(close))
            {
                return;
            }

            _highWindow[_windowIndex] = high;
            _lowWindow[_windowIndex] = low;
            _windowIndex = (_windowIndex + 1) % _period;
            if (_samples < _period) _samples++;

            // 统计过去 _samples 根 Bar 的最高价与最低价
            int count = _samples;
            double highest = double.MinValue;
            double lowest = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (_highWindow[i] > highest) highest = _highWindow[i];
                if (_lowWindow[i] < lowest) lowest = _lowWindow[i];
            }

            double range = highest - lowest;
            double rsv = range <= 1e-12 ? 50.0 : ((close - lowest) / range) * 100.0;

            // 平滑递推
            _k = (1.0 - _kAlpha) * _k + _kAlpha * rsv;
            _d = (1.0 - _dAlpha) * _d + _dAlpha * _k;
            double j = 3.0 * _k - 2.0 * _d;

            if (_samples >= _period)
            {
                IsReady = true;
                _kSub.Push(_k);
                _dSub.Push(_d);
                _jSub.Push(j);
                PushValue(_k);
            }
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _windowIndex = 0;
            _samples = 0;
            _k = 50.0;
            _d = 50.0;
            Array.Clear(_highWindow, 0, _period);
            Array.Clear(_lowWindow, 0, _period);
            _kSub.Reset();
            _dSub.Reset();
            _jSub.Reset();
        }
    }
}
