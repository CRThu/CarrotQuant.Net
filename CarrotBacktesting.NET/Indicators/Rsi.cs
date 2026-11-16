using System;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 相对强弱指标（Relative Strength Index, RSI）。
    /// 基于 J. Welles Wilder 的经典平滑法增量递推实现的 O(1) 流式指标。
    /// 内置 NaN 停牌冻结跳过（Skip & Freeze）防御。
    /// </summary>
    public sealed class Rsi : StreamIndicatorBase
    {
        private readonly int _period;
        private double _prevClose;
        private double _avgGain;
        private double _avgLoss;
        private double _sumGain;
        private double _sumLoss;
        private int _samples;

        /// <summary>
        /// 获取 RSI 周期长度。
        /// </summary>
        public int Period => _period;

        /// <summary>
        /// 构建流式 RSI 指标实例。
        /// </summary>
        /// <param name="period">平滑周期（默认为 14，必须 >= 1）。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public Rsi(int period = 14, int historyCapacity = 8) : base(historyCapacity)
        {
            if (period < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "RSI 周期必须大于等于 1。");
            }

            _period = period;
            _prevClose = double.NaN;
            _avgGain = 0.0;
            _avgLoss = 0.0;
            _sumGain = 0.0;
            _sumLoss = 0.0;
            _samples = 0;
        }

        /// <inheritdoc />
        public override void Update(double open, double high, double low, double close, double volume)
        {
            // 停牌与脏数据防御（行业标准：冻结跳过 Skip & Freeze）
            if (double.IsNaN(close) || close <= 0.0)
            {
                return;
            }

            if (double.IsNaN(_prevClose))
            {
                _prevClose = close;
                return;
            }

            double change = close - _prevClose;
            _prevClose = close;

            double gain = change > 0.0 ? change : 0.0;
            double loss = change < 0.0 ? -change : 0.0;

            if (_samples < _period)
            {
                _sumGain += gain;
                _sumLoss += loss;
                _samples++;

                if (_samples == _period)
                {
                    _avgGain = _sumGain / _period;
                    _avgLoss = _sumLoss / _period;
                    IsReady = true;

                    double rsi = CalculateRsi(_avgGain, _avgLoss);
                    PushValue(rsi);
                }
            }
            else
            {
                // Wilder 平滑递推
                _avgGain = (_avgGain * (_period - 1) + gain) / _period;
                _avgLoss = (_avgLoss * (_period - 1) + loss) / _period;

                double rsi = CalculateRsi(_avgGain, _avgLoss);
                PushValue(rsi);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static double CalculateRsi(double avgGain, double avgLoss)
        {
            if (avgLoss == 0.0)
            {
                return avgGain == 0.0 ? 50.0 : 100.0;
            }
            if (avgGain == 0.0)
            {
                return 0.0;
            }

            double rs = avgGain / avgLoss;
            return 100.0 - (100.0 / (1.0 + rs));
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _prevClose = double.NaN;
            _avgGain = 0.0;
            _avgLoss = 0.0;
            _sumGain = 0.0;
            _sumLoss = 0.0;
            _samples = 0;
        }
    }
}
