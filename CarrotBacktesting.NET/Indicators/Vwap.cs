using System;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 成交量加权平均价（Volume-Weighted Average Price, VWAP）。
    /// 基于典型价格 TypicalPrice = (High + Low + Close) / 3 与成交量计算的增量 O(1) 流式指标。
    /// 支持累计全周期模式（period = 0）与定长滑动窗口模式（period > 0），热路径零堆分配（Zero GC）。
    /// </summary>
    public sealed class Vwap : StreamIndicatorBase
    {
        private readonly int _period;
        private readonly double[]? _pvWindow;
        private readonly double[]? _volWindow;
        private double _cumPV;
        private double _cumVol;
        private int _samples;
        private int _windowIndex;

        /// <summary>
        /// 获取均线周期长度（0 表示全周期累计模式）。
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// 构建流式 VWAP 指标实例。
        /// </summary>
        /// <param name="period">滚动周期（0 为自启动或跨日 Reset 的累计模式，> 0 为滑动窗口模式）。默认为 0。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public Vwap(int period = 0, int historyCapacity = 8) : base(historyCapacity)
        {
            if (period < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "VWAP 周期不能为负数。");
            }

            _period = period;
            if (_period > 0)
            {
                _pvWindow = new double[_period];
                _volWindow = new double[_period];
            }

            _cumPV = 0.0;
            _cumVol = 0.0;
            _samples = 0;
            _windowIndex = 0;
        }

        /// <summary>
        /// 默认所需的时序字段名集合（严格对应 values[0..3]：high, low, close, volume）。
        /// </summary>
        public static readonly string[] DefaultFields = ["high", "low", "close", "volume"];

        /// <inheritdoc />
        public override void Update(params ReadOnlySpan<double> values)
        {
            if (values.Length < 4) return;

            double high = values[0];
            double low = values[1];
            double close = values[2];
            double volume = values[3];

            if (double.IsNaN(high) || double.IsNaN(low) || double.IsNaN(close) ||
                double.IsInfinity(high) || double.IsInfinity(low) || double.IsInfinity(close))
            {
                return;
            }

            double vol = double.IsNaN(volume) || volume < 0.0 ? 0.0 : volume;
            double typicalPrice = (high + low + close) * (1.0 / 3.0);
            double pv = typicalPrice * vol;

            if (_period == 0)
            {
                // 累计全周期模式
                _cumPV += pv;
                _cumVol += vol;
                _samples++;

                IsReady = _cumVol > 0.0;
                double vwap = _cumVol > 0.0 ? _cumPV / _cumVol : typicalPrice;
                PushValue(vwap);
            }
            else
            {
                // 滑动窗口模式
                if (_samples < _period)
                {
                    _pvWindow![_samples] = pv;
                    _volWindow![_samples] = vol;
                    _cumPV += pv;
                    _cumVol += vol;
                    _samples++;

                    if (_samples == _period)
                    {
                        IsReady = true;
                        double vwap = _cumVol > 0.0 ? _cumPV / _cumVol : typicalPrice;
                        PushValue(vwap);
                    }
                }
                else
                {
                    double oldestPV = _pvWindow![_windowIndex];
                    double oldestVol = _volWindow![_windowIndex];

                    _cumPV = _cumPV - oldestPV + pv;
                    _cumVol = _cumVol - oldestVol + vol;

                    _pvWindow[_windowIndex] = pv;
                    _volWindow[_windowIndex] = vol;
                    _windowIndex = (_windowIndex + 1) % _period;

                    double vwap = _cumVol > 0.0 ? _cumPV / _cumVol : typicalPrice;
                    PushValue(vwap);
                }
            }
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _cumPV = 0.0;
            _cumVol = 0.0;
            _samples = 0;
            _windowIndex = 0;
            if (_pvWindow != null) Array.Clear(_pvWindow, 0, _period);
            if (_volWindow != null) Array.Clear(_volWindow, 0, _period);
        }
    }
}
