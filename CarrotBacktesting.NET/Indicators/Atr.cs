using System;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 真实波幅均值指标（Average True Range, ATR）。
    /// 基于 J. Welles Wilder 的平滑递推算法实现的 O(1) 流式波动率指标。
    /// 内置 NaN 停牌冻结跳过（Skip & Freeze）防御。
    /// </summary>
    public sealed class Atr : StreamIndicatorBase
    {
        private readonly int _period;
        private double _prevClose;
        private double _atr;
        private double _sumTr;
        private int _samples;

        /// <summary>
        /// 获取 ATR 周期长度。
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// 构建流式 ATR 指标实例。
        /// </summary>
        /// <param name="period">平滑周期（默认为 14，必须 >= 1）。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public Atr(int period = 14, int historyCapacity = 8) : base(historyCapacity)
        {
            if (period < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "ATR 周期必须大于等于 1。");
            }

            _period = period;
            _prevClose = double.NaN;
            _atr = 0.0;
            _sumTr = 0.0;
            _samples = 0;
        }

        /// <inheritdoc />
        public override void Update(double open, double high, double low, double close, double volume)
        {
            // 停牌与脏数据防御（行业标准：冻结跳过 Skip & Freeze）
            if (double.IsNaN(high) || double.IsNaN(low) || double.IsNaN(close) ||
                high <= 0.0 || low <= 0.0 || close <= 0.0)
            {
                return;
            }

            double tr;
            if (double.IsNaN(_prevClose))
            {
                tr = high - low;
            }
            else
            {
                double hl = high - low;
                double hpc = Math.Abs(high - _prevClose);
                double lpc = Math.Abs(low - _prevClose);
                tr = Math.Max(hl, Math.Max(hpc, lpc));
            }

            _prevClose = close;

            if (_samples < _period)
            {
                _sumTr += tr;
                _samples++;

                if (_samples == _period)
                {
                    _atr = _sumTr / _period;
                    IsReady = true;
                    PushValue(_atr);
                }
            }
            else
            {
                // Wilder 平滑递推
                _atr = (_atr * (_period - 1) + tr) / _period;
                PushValue(_atr);
            }
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _prevClose = double.NaN;
            _atr = 0.0;
            _sumTr = 0.0;
            _samples = 0;
        }
    }
}
