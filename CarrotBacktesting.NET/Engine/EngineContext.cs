using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Indicators;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 策略引擎核心运行时上下文权威实现。
    /// 统筹时序行情、账户账本、市场黑板、指标注册表与异构事件流的统一注入。
    /// </summary>
    public sealed class EngineContext : IEngineContext
    {
        private readonly ConcurrentDictionary<Type, object> _extensions = new();
        private readonly HashSet<string> _priceFields;
        private double[] _dailyFactors;

        // 惰性快速引用缓存：首次访问时通过 HasField 单次解析，彻底消除热路径内联中的字典查找与 try-catch 异常陷阱
        private IReadOnlyMatrix2D<double>? _closeMatrix;
        private bool _resolvedClose;
        private IReadOnlyMatrix2D<double>? _openMatrix;
        private bool _resolvedOpen;
        private IReadOnlyMatrix2D<double>? _highMatrix;
        private bool _resolvedHigh;
        private IReadOnlyMatrix2D<double>? _lowMatrix;
        private bool _resolvedLow;
        private IReadOnlyMatrix2D<double>? _volumeMatrix;
        private bool _resolvedVolume;

        /// <inheritdoc />
        public MarketTime CurrentTime { get; internal set; }

        /// <inheritdoc />
        public int CurrentIndex { get; internal set; }

        /// <summary>
        /// 供引擎在主时钟步进时原子更新当前时间与行索引（热路径内联）。
        /// </summary>
        /// <param name="index">当前时步索引。</param>
        /// <param name="time">当前时步时间戳。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateStep(int index, MarketTime time)
        {
            CurrentIndex = index;
            CurrentTime = time;
        }

        /// <inheritdoc />
        public ITimeseriesRegistry Timeseries { get; }

        /// <inheritdoc />
        public IEventRegistry Events { get; }

        /// <inheritdoc />
        public IFieldRegistry Fields { get; }

        /// <inheritdoc />
        public IBroker Broker { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> Symbols => Timeseries.Metadata.Symbols;

        /// <inheritdoc />
        public int SymbolCount => Timeseries.Metadata.Symbols.Count;

        /// <inheritdoc />
        public int GetSymbolIndex(string symbol) => Timeseries.Metadata.GetSymbolIndex(symbol);

        /// <inheritdoc />
        public string Buy(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "")
            => Broker.Buy(symbol, quantity, price, type, clientOrderId, orderTag);

        /// <inheritdoc />
        public string Sell(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "")
            => Broker.Sell(symbol, quantity, price, type, clientOrderId, orderTag);

        /// <inheritdoc />
        public string ClosePosition(string symbol, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "")
            => Broker.ClosePosition(symbol, price, type, clientOrderId, orderTag);

        /// <inheritdoc />
        public ITradingRecorder? Recorder { get; }

        /// <inheritdoc />
        public void RecordSignal(string symbol, double signalValue, string? signalGroup = null, string? reason = null)
        {
            Recorder?.RecordSignal(new SignalRecord(CurrentTime, symbol, signalValue, signalGroup ?? string.Empty, reason ?? string.Empty));
        }

        /// <inheritdoc />
        public IMarketState Market { get; }

        /// <inheritdoc cref="IEngineContext.Indicators" />
        public IndicatorRegistry Indicators { get; internal set; }

        /// <inheritdoc />
        IIndicatorRegistry IEngineContext.Indicators => Indicators;

        /// <inheritdoc />
        public decimal GetPosition(string symbol) => Broker.GetPosition(symbol);

        /// <inheritdoc />
        public decimal GetAvailablePosition(string symbol) => Broker.GetAvailablePosition(symbol);

        /// <inheritdoc />
        public double GetPositionCost(string symbol) => Broker.GetPositionCost(symbol);

        /// <inheritdoc />
        public double AvailableCash => Broker.GetCash();

        /// <inheritdoc />
        public double TotalEquity => Broker.GetTotalEquity();

        /// <summary>
        /// 获取全市场最新有效复权因子只读快照引用。
        /// </summary>
        public ReadOnlySpan<double> DailyFactors => _dailyFactors;

        /// <summary>
        /// 初始化引擎运行时上下文。
        /// </summary>
        /// <param name="timeseries">时序行情注册表。</param>
        /// <param name="broker">经纪商执行接口。</param>
        /// <param name="market">市场状态黑板（若为空则自动构建默认 MarketState 实例）。</param>
        /// <param name="events">异构事件流注册表（若为空则自动构建默认 EventRegistry 实例）。</param>
        /// <param name="fields">标的维表注册表（若为空则自动构建默认 FieldRegistry 实例）。</param>
        /// <param name="indicators">指标注册中心（若为空则依据时序元数据自动构建）。</param>
        /// <param name="priceFields">价格类需复权字段白名单（默认包含 open, high, low, close）。</param>
        /// <param name="recorder">量化事实记录器（可选）。</param>
        public EngineContext(
            ITimeseriesRegistry timeseries,
            IBroker broker,
            IMarketState? market = null,
            IEventRegistry? events = null,
            IFieldRegistry? fields = null,
            IndicatorRegistry? indicators = null,
            IEnumerable<string>? priceFields = null,
            ITradingRecorder? recorder = null)
        {
            Timeseries = timeseries ?? throw new ArgumentNullException(nameof(timeseries));
            Broker = broker ?? throw new ArgumentNullException(nameof(broker));
            Market = market ?? new MarketState();
            Events = events ?? new EventRegistry();
            Fields = fields ?? new FieldRegistry();
            Indicators = indicators ?? new IndicatorRegistry(timeseries.Metadata);
            Recorder = recorder;

            _priceFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "open", "high", "low", "close"
            };
            if (priceFields != null)
            {
                foreach (var f in priceFields)
                {
                    if (!string.IsNullOrWhiteSpace(f)) _priceFields.Add(f);
                }
            }

            CurrentIndex = -1;
            CurrentTime = MarketTime.MinValue;

            int symbolCount = timeseries.Metadata.Symbols.Count;
            _dailyFactors = new double[symbolCount];
            Array.Fill(_dailyFactors, 1.0);
        }

        /// <summary>
        /// 供引擎在启动预热完成后整体注入已对齐的全局基准复权因子数组。
        /// </summary>
        internal void SetDailyFactors(double[] factors)
        {
            ArgumentNullException.ThrowIfNull(factors);
            if (factors.Length != _dailyFactors.Length)
            {
                throw new ArgumentException(
                    $"复权因子数组长度 ({factors.Length}) 与全市场标的总数 ({_dailyFactors.Length}) 不一致。",
                    nameof(factors));
            }
            _dailyFactors = factors;
        }

        /// <summary>
        /// 供引擎在跨日检测到除权事件时增量更新指定标的的最新累计因子（纳秒级操作）。
        /// </summary>
        /// <param name="symbolIndex">标的列索引。</param>
        /// <param name="factor">最新有效因子。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateFactor(int symbolIndex, double factor)
        {
            if ((uint)symbolIndex < (uint)_dailyFactors.Length)
            {
                _dailyFactors[symbolIndex] = factor;
            }
        }

        #region 当期极简取价门面权威实现 (Zero Alloc & Inlined)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IReadOnlyMatrix2D<double>? GetCloseMatrix()
        {
            if (!_resolvedClose)
            {
                _closeMatrix = Timeseries.HasField("close") ? Timeseries.GetMatrix<double>("close") : null;
                _resolvedClose = true;
            }
            return _closeMatrix;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IReadOnlyMatrix2D<double>? GetOpenMatrix()
        {
            if (!_resolvedOpen)
            {
                _openMatrix = Timeseries.HasField("open") ? Timeseries.GetMatrix<double>("open") : null;
                _resolvedOpen = true;
            }
            return _openMatrix;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IReadOnlyMatrix2D<double>? GetHighMatrix()
        {
            if (!_resolvedHigh)
            {
                _highMatrix = Timeseries.HasField("high") ? Timeseries.GetMatrix<double>("high") : null;
                _resolvedHigh = true;
            }
            return _highMatrix;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IReadOnlyMatrix2D<double>? GetLowMatrix()
        {
            if (!_resolvedLow)
            {
                _lowMatrix = Timeseries.HasField("low") ? Timeseries.GetMatrix<double>("low") : null;
                _resolvedLow = true;
            }
            return _lowMatrix;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IReadOnlyMatrix2D<double>? GetVolumeMatrix()
        {
            if (!_resolvedVolume)
            {
                _volumeMatrix = Timeseries.HasField("volume") ? Timeseries.GetMatrix<double>("volume") : null;
                _resolvedVolume = true;
            }
            return _volumeMatrix;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetClose(int symbolIndex, PriceMode mode = PriceMode.Post)
        {
            if (CurrentIndex < 0) return double.NaN;
            var m = GetCloseMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || CurrentIndex >= m.RowCount) return double.NaN;
            double raw = m[CurrentIndex, symbolIndex];
            return mode == PriceMode.Post ? raw * _dailyFactors[symbolIndex] : raw;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetClose(string symbol, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetClose(idx, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetOpen(int symbolIndex, PriceMode mode = PriceMode.Post)
        {
            if (CurrentIndex < 0) return double.NaN;
            var m = GetOpenMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || CurrentIndex >= m.RowCount) return double.NaN;
            double raw = m[CurrentIndex, symbolIndex];
            return mode == PriceMode.Post ? raw * _dailyFactors[symbolIndex] : raw;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetOpen(string symbol, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetOpen(idx, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetHigh(int symbolIndex, PriceMode mode = PriceMode.Post)
        {
            if (CurrentIndex < 0) return double.NaN;
            var m = GetHighMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || CurrentIndex >= m.RowCount) return double.NaN;
            double raw = m[CurrentIndex, symbolIndex];
            return mode == PriceMode.Post ? raw * _dailyFactors[symbolIndex] : raw;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetHigh(string symbol, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetHigh(idx, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetLow(int symbolIndex, PriceMode mode = PriceMode.Post)
        {
            if (CurrentIndex < 0) return double.NaN;
            var m = GetLowMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || CurrentIndex >= m.RowCount) return double.NaN;
            double raw = m[CurrentIndex, symbolIndex];
            return mode == PriceMode.Post ? raw * _dailyFactors[symbolIndex] : raw;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetLow(string symbol, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetLow(idx, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetVolume(int symbolIndex, PriceMode mode = PriceMode.Raw)
        {
            if (CurrentIndex < 0) return double.NaN;
            var m = GetVolumeMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || CurrentIndex >= m.RowCount) return double.NaN;
            // 成交量严格保持物理原始量（Raw 股数），防止价格复权乘法虚造成交量放大
            return m[CurrentIndex, symbolIndex];
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetVolume(string symbol, PriceMode mode = PriceMode.Raw)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetVolume(idx, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetField(int symbolIndex, string fieldName, PriceMode mode = PriceMode.Post)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return double.NaN;

            // 高频 OHLCV 走极速直通分支
            if (fieldName.Equals("close", StringComparison.OrdinalIgnoreCase)) return GetClose(symbolIndex, mode);
            if (fieldName.Equals("open", StringComparison.OrdinalIgnoreCase)) return GetOpen(symbolIndex, mode);
            if (fieldName.Equals("high", StringComparison.OrdinalIgnoreCase)) return GetHigh(symbolIndex, mode);
            if (fieldName.Equals("low", StringComparison.OrdinalIgnoreCase)) return GetLow(symbolIndex, mode);
            if (fieldName.Equals("volume", StringComparison.OrdinalIgnoreCase)) return GetVolume(symbolIndex, mode);

            if (CurrentIndex < 0 || !Timeseries.HasField(fieldName)) return double.NaN;

            var m = Timeseries.GetMatrix<double>(fieldName);
            if (m == null || (uint)symbolIndex >= (uint)m.Width || CurrentIndex >= m.RowCount) return double.NaN;
            double raw = m[CurrentIndex, symbolIndex];

            // 仅对价格类字段在 Post 模式下乘复权因子，比率/基本面字段（PE、换手率等）严禁盲目乘因子！
            bool isPriceField = _priceFields.Contains(fieldName);
            return (mode == PriceMode.Post && isPriceField) ? raw * _dailyFactors[symbolIndex] : raw;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetField(string symbol, string fieldName, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetField(idx, fieldName, mode) : double.NaN;
        }

        #endregion

        /// <inheritdoc />
        public T? GetExtension<T>() where T : class
        {
            return _extensions.TryGetValue(typeof(T), out var ext) ? (T)ext : null;
        }

        /// <summary>
        /// 注册或更新自定义上下文扩展组件（如监控器、分析记录器等）。
        /// </summary>
        public void SetExtension<T>(T extension) where T : class
        {
            ArgumentNullException.ThrowIfNull(extension);
            _extensions[typeof(T)] = extension;
        }
    }
}
