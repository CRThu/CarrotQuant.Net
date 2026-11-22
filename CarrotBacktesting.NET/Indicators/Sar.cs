using System;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 抛物转向系统（Parabolic Stop and Reverse, SAR）。
    /// 基于极值点（EP）、自适应加速因子（AF）与高低点限幅实现的增量 O(1) 流式趋势追踪与止损指标。
    /// 严格遵循 Welles Wilder 经典物理状态机与 TA-Lib C 内核标准，热路径零堆分配（Zero GC）。
    /// </summary>
    public sealed class Sar : StreamIndicatorBase
    {
        private readonly double _acceleration;
        private readonly double _maximum;

        private int _samples;
        private bool _isLong;
        private double _af;
        private double _ep;
        private double _sar;

        private double _prevHigh;
        private double _prevLow;
        private double _newHigh;
        private double _newLow;

        /// <summary>
        /// 获取加速因子步长（默认 0.02）。
        /// </summary>
        public double Acceleration => _acceleration;

        /// <summary>
        /// 获取加速因子上限（默认 0.2）。
        /// </summary>
        public double Maximum => _maximum;

        /// <summary>
        /// 获取当前多空方向（true 为多头，false 为空头）。
        /// </summary>
        public bool IsLong => _isLong;

        /// <summary>
        /// 构建流式 SAR 指标实例。
        /// </summary>
        /// <param name="acceleration">初始加速因子与步进增量（默认 0.02）。必须 > 0 且 &lt;= maximum。</param>
        /// <param name="maximum">最大加速因子（默认 0.2）。必须 >= acceleration。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public Sar(double acceleration = 0.02, double maximum = 0.2, int historyCapacity = 8)
            : base(historyCapacity)
        {
            if (acceleration <= 0.0 || double.IsNaN(acceleration))
            {
                throw new ArgumentOutOfRangeException(nameof(acceleration), "加速因子步长必须为大于 0 的有效浮点数。");
            }
            if (maximum < acceleration || double.IsNaN(maximum))
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), "最大加速因子必须大于等于步进加速因子。");
            }

            _acceleration = acceleration;
            _maximum = maximum;
            _samples = 0;
            _isLong = true;
            _af = _acceleration;
            _ep = 0.0;
            _sar = 0.0;
            _prevHigh = 0.0;
            _prevLow = 0.0;
            _newHigh = 0.0;
            _newLow = 0.0;
        }

        /// <summary>
        /// 默认所需的时序字段名集合（严格对应 values[0..1]：high, low）。
        /// </summary>
        public static readonly string[] DefaultFields = ["high", "low"];

        /// <inheritdoc />
        public override void Update(params ReadOnlySpan<double> values)
        {
            if (values.Length < 2) return;

            double high = values[0];
            double low = values[1];

            if (double.IsNaN(high) || double.IsNaN(low) || double.IsInfinity(high) || double.IsInfinity(low))
            {
                return;
            }

            if (_samples == 0)
            {
                _newHigh = high;
                _newLow = low;
                _samples = 1;
                PushValue(double.NaN);
            }
            else if (_samples == 1)
            {
                // TA-Lib 行业标准：以第一根走势确定初始方向
                double diffP = high - _newHigh;
                double diffM = _newLow - low;

                if (diffM > 0.0 && diffM > diffP)
                {
                    _isLong = false;
                    _ep = low;
                    _sar = _newHigh;
                }
                else
                {
                    _isLong = true;
                    _ep = high;
                    _sar = _newLow;
                }

                _af = _acceleration;
                // TA-Lib 建立期特殊对齐：首轮步进 prev 与 new 均为当前 Bar 1
                _prevHigh = high;
                _prevLow = low;
                _newHigh = high;
                _newLow = low;
                _samples = 2;

                IsReady = true;
                double outSar = StepCore();
                PushValue(outSar);
            }
            else
            {
                _prevHigh = _newHigh;
                _prevLow = _newLow;
                _newHigh = high;
                _newLow = low;

                double outSar = StepCore();
                PushValue(outSar);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private double StepCore()
        {
            double outSar;
            if (_isLong)
            {
                if (_newLow <= _sar)
                {
                    // 击穿下限，反转为空头
                    _isLong = false;
                    _sar = _ep;
                    if (_sar < _prevHigh) _sar = _prevHigh;
                    if (_sar < _newHigh) _sar = _newHigh;

                    outSar = _sar;
                    _af = _acceleration;
                    _ep = _newLow;
                    _sar = _af * (_ep - _sar) + _sar;
                    if (_sar < _prevHigh) _sar = _prevHigh;
                    if (_sar < _newHigh) _sar = _newHigh;
                }
                else
                {
                    // 延续多头
                    outSar = _sar;
                    if (_newHigh > _ep)
                    {
                        _ep = _newHigh;
                        _af = Math.Min(_maximum, _af + _acceleration);
                    }
                    _sar = _af * (_ep - _sar) + _sar;
                    if (_sar > _prevLow) _sar = _prevLow;
                    if (_sar > _newLow) _sar = _newLow;
                }
            }
            else
            {
                if (_newHigh >= _sar)
                {
                    // 向上突破，反转为多头
                    _isLong = true;
                    _sar = _ep;
                    if (_sar > _prevLow) _sar = _prevLow;
                    if (_sar > _newLow) _sar = _newLow;

                    outSar = _sar;
                    _af = _acceleration;
                    _ep = _newHigh;
                    _sar = _af * (_ep - _sar) + _sar;
                    if (_sar > _prevLow) _sar = _prevLow;
                    if (_sar > _newLow) _sar = _newLow;
                }
                else
                {
                    // 延续空头
                    outSar = _sar;
                    if (_newLow < _ep)
                    {
                        _ep = _newLow;
                        _af = Math.Min(_maximum, _af + _acceleration);
                    }
                    _sar = _af * (_ep - _sar) + _sar;
                    if (_sar < _prevHigh) _sar = _prevHigh;
                    if (_sar < _newHigh) _sar = _newHigh;
                }
            }
            return outSar;
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _samples = 0;
            _isLong = true;
            _af = _acceleration;
            _ep = 0.0;
            _sar = 0.0;
            _prevHigh = 0.0;
            _prevLow = 0.0;
            _newHigh = 0.0;
            _newLow = 0.0;
        }
    }
}
