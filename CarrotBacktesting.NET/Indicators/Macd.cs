using System;
using CarrotBacktesting.NET.Abstraction.Indicators;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 指数平滑异同移动平均线（Moving Average Convergence Divergence, MACD）。
    /// 遵循经典组合模式（Composite Pattern），自身实现 <see cref="IStreamIndicator"/> 契约（默认索引指向 Dif），
    /// 同时通过强类型只读属性暴露 <see cref="Dif"/>（快线-慢线）、<see cref="Dea"/>（信号线）与 <see cref="Hist"/>（柱状图差值）。
    /// 内部单次共享快速与慢速 EMA 计算，内置 NaN 停牌冻结跳过（Skip & Freeze）防御。
    /// </summary>
    public sealed class Macd : StreamIndicatorBase
    {
        private readonly Ema _fastEma;
        private readonly Ema _slowEma;
        private readonly Ema _signalEma;

        private readonly SubStreamIndicator _difStream;
        private readonly SubStreamIndicator _deaStream;
        private readonly SubStreamIndicator _histStream;

        /// <summary>
        /// 获取差离值子流（DIF = Fast EMA - Slow EMA）。
        /// </summary>
        public IStreamIndicator Dif => _difStream;

        /// <summary>
        /// 获取讯号线子流（DEA = Signal EMA of DIF）。
        /// </summary>
        public IStreamIndicator Dea => _deaStream;

        /// <summary>
        /// 获取柱状图差值子流（HIST = (DIF - DEA) * 2）。
        /// </summary>
        public IStreamIndicator Hist => _histStream;

        /// <summary>
        /// 构建流式 MACD 指标实例。
        /// </summary>
        /// <param name="fastPeriod">快线平滑周期（默认为 12）。</param>
        /// <param name="slowPeriod">慢线平滑周期（默认为 26）。</param>
        /// <param name="signalPeriod">信号线平滑周期（默认为 9）。</param>
        /// <param name="historyCapacity">历史回溯缓冲容量（默认 8）。</param>
        public Macd(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9, int historyCapacity = 8)
            : base(historyCapacity)
        {
            if (fastPeriod >= slowPeriod)
            {
                throw new ArgumentException($"MACD 快线周期 ({fastPeriod}) 必须严格小于慢线周期 ({slowPeriod})。");
            }
            if (signalPeriod < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(signalPeriod), "信号线周期必须大于等于 1。");
            }

            _fastEma = new Ema(fastPeriod, historyCapacity);
            _slowEma = new Ema(slowPeriod, historyCapacity);
            _signalEma = new Ema(signalPeriod, historyCapacity);

            _difStream = new SubStreamIndicator(historyCapacity);
            _deaStream = new SubStreamIndicator(historyCapacity);
            _histStream = new SubStreamIndicator(historyCapacity);
        }

        /// <inheritdoc />
        public override void Update(double open, double high, double low, double close, double volume)
        {
            // 停牌与脏数据防御（行业标准：冻结跳过 Skip & Freeze）
            if (double.IsNaN(close) || close <= 0.0)
            {
                return;
            }

            _fastEma.Update(open, high, low, close, volume);
            _slowEma.Update(open, high, low, close, volume);

            if (_slowEma.IsReady)
            {
                double dif = _fastEma.Current - _slowEma.Current;
                _difStream.Push(dif);

                _signalEma.Update(dif, dif, dif, dif, 0.0);
                if (_signalEma.IsReady)
                {
                    double dea = _signalEma.Current;
                    _deaStream.Push(dea);

                    double hist = (dif - dea) * 2.0;
                    _histStream.Push(hist);

                    IsReady = true;
                    // MACD 默认主索引器指向 DIF
                    PushValue(dif);
                }
            }
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            _fastEma.Reset();
            _slowEma.Reset();
            _signalEma.Reset();
            _difStream.Reset();
            _deaStream.Reset();
            _histStream.Reset();
        }
    }
}
