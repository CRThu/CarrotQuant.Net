using System;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 指数移动平均线（Exponential Moving Average, EMA）。
    /// 采用增量递推公式 EMA_t = (Close - EMA_{t-1}) * alpha + EMA_{t-1} 实现的 O(1) 流式指标。
    /// 前期采用简单平均值完成冷启动对齐，内置 NaN 停牌冻结跳过（Skip & Freeze）防御。
    /// </summary>
    public sealed class Ema : StreamIndicatorBase
    {
        private readonly int _period;
        private readonly double _multiplier;
        private double _ema;
        private double _initSum;
        private int _samples;

        /// <summary>
        /// 获取均线周期长度。
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// 构建流式 EMA 指标实例。
        /// </summary>
        /// <param name="period">平滑周期（必须 >= 1）。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public Ema(int period, int historyCapacity = 8) : base(historyCapacity)
        {
            if (period < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "EMA 周期必须大于等于 1。");
            }

            _period = period;
            _multiplier = 2.0 / (period + 1.0);
            _ema = 0.0;
            _initSum = 0.0;
            _samples = 0;
        }

        /// <inheritdoc />
        public override void Update(double open, double high, double low, double close, double volume)
        {
            // 停牌与脏数据防御（行业标准：冻结跳过 Skip & Freeze；支持差离值、收益率等实数序列输入）
            if (double.IsNaN(close) || double.IsInfinity(close))
            {
                return;
            }

            if (_samples < _period)
            {
                _initSum += close;
                _samples++;

                if (_samples == _period)
                {
                    _ema = _initSum / _period;
                    IsReady = true;
                    PushValue(_ema);
                }
            }
            else
            {
                _ema = (close - _ema) * _multiplier + _ema;
                PushValue(_ema);
            }
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _ema = 0.0;
            _initSum = 0.0;
            _samples = 0;
        }
    }
}
