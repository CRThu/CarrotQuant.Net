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
    public sealed class EngineContext : IEngineContext, IDisposable
    {
        private readonly ConcurrentDictionary<Type, object> _extensions = new();
        private readonly HashSet<string> _priceFields;
        private ChunkMatrix2D<double>? _factorMatrix;
        private int[]? _stepToDateIndex;

        // 惰性快速引用缓存：首次访问时解析，避免重复的字典查找与类型检查
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
        public string Buy(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "", TimeInForce? timeInForce = null)
            => Broker.Buy(symbol, quantity, price, type, clientOrderId, orderTag, timeInForce);

        /// <inheritdoc />
        public string Sell(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "", TimeInForce? timeInForce = null)
            => Broker.Sell(symbol, quantity, price, type, clientOrderId, orderTag, timeInForce);

        /// <inheritdoc />
        public string ClosePosition(string symbol, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "", TimeInForce? timeInForce = null)
            => Broker.ClosePosition(symbol, price, type, clientOrderId, orderTag, timeInForce);

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
        /// 获取全市场当前时间步有效复权因子切片引用。
        /// </summary>
        public ReadOnlySpan<double> DailyFactors
        {
            get
            {
                if (_factorMatrix == null || CurrentIndex < 0) return ReadOnlySpan<double>.Empty;
                int dateRow = (_stepToDateIndex != null && (uint)CurrentIndex < (uint)_stepToDateIndex.Length)
                    ? _stepToDateIndex[CurrentIndex]
                    : CurrentIndex;
                if ((uint)dateRow >= (uint)_factorMatrix.RowCount) return ReadOnlySpan<double>.Empty;
                return _factorMatrix.GetRowSpan(dateRow);
            }
        }

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
        }

        /// <summary>
        /// 供引擎在启动预热完成后整体注入已对齐的全局基准复权因子矩阵与时步-交易日映射数组。
        /// </summary>
        internal void SetAdjustmentData(ChunkMatrix2D<double>? factorMatrix, int[]? stepToDateIndex)
        {
            _factorMatrix?.Dispose();
            _factorMatrix = factorMatrix;
            _stepToDateIndex = stepToDateIndex;
        }

        /// <summary>
        /// 释放非托管矩阵资源。
        /// </summary>
        public void Dispose()
        {
            _factorMatrix?.Dispose();
            _factorMatrix = null;
            _stepToDateIndex = null;
        }

        #region 时序与复权取价门面权威实现 (Zero Alloc & Inlined)

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

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowFutureOffsetException(int offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, $"时序 offset 越界 (offset = {offset} > 0)。禁止指定正向偏移量以防止前瞻偏差（Look-ahead Bias）。");
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetClose(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            if (offset > 0) ThrowFutureOffsetException(offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            var m = GetCloseMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || targetIndex >= m.RowCount) return double.NaN;

            double raw = m[targetIndex, symbolIndex];
            if (mode != PriceMode.Post || _factorMatrix == null) return raw;

            int dateRow = (_stepToDateIndex != null && (uint)targetIndex < (uint)_stepToDateIndex.Length)
                ? _stepToDateIndex[targetIndex]
                : targetIndex;
            if ((uint)dateRow >= (uint)_factorMatrix.RowCount || (uint)symbolIndex >= (uint)_factorMatrix.Width) return raw;

            return raw * _factorMatrix[dateRow, symbolIndex];
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetClose(string symbol, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetClose(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetOpen(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            if (offset > 0) ThrowFutureOffsetException(offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            var m = GetOpenMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || targetIndex >= m.RowCount) return double.NaN;

            double raw = m[targetIndex, symbolIndex];
            if (mode != PriceMode.Post || _factorMatrix == null) return raw;

            int dateRow = (_stepToDateIndex != null && (uint)targetIndex < (uint)_stepToDateIndex.Length)
                ? _stepToDateIndex[targetIndex]
                : targetIndex;
            if ((uint)dateRow >= (uint)_factorMatrix.RowCount || (uint)symbolIndex >= (uint)_factorMatrix.Width) return raw;

            return raw * _factorMatrix[dateRow, symbolIndex];
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetOpen(string symbol, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetOpen(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetHigh(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            if (offset > 0) ThrowFutureOffsetException(offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            var m = GetHighMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || targetIndex >= m.RowCount) return double.NaN;

            double raw = m[targetIndex, symbolIndex];
            if (mode != PriceMode.Post || _factorMatrix == null) return raw;

            int dateRow = (_stepToDateIndex != null && (uint)targetIndex < (uint)_stepToDateIndex.Length)
                ? _stepToDateIndex[targetIndex]
                : targetIndex;
            if ((uint)dateRow >= (uint)_factorMatrix.RowCount || (uint)symbolIndex >= (uint)_factorMatrix.Width) return raw;

            return raw * _factorMatrix[dateRow, symbolIndex];
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetHigh(string symbol, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetHigh(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetLow(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            if (offset > 0) ThrowFutureOffsetException(offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            var m = GetLowMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || targetIndex >= m.RowCount) return double.NaN;

            double raw = m[targetIndex, symbolIndex];
            if (mode != PriceMode.Post || _factorMatrix == null) return raw;

            int dateRow = (_stepToDateIndex != null && (uint)targetIndex < (uint)_stepToDateIndex.Length)
                ? _stepToDateIndex[targetIndex]
                : targetIndex;
            if ((uint)dateRow >= (uint)_factorMatrix.RowCount || (uint)symbolIndex >= (uint)_factorMatrix.Width) return raw;

            return raw * _factorMatrix[dateRow, symbolIndex];
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetLow(string symbol, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetLow(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetVolume(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Raw)
        {
            if (offset > 0) ThrowFutureOffsetException(offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            var m = GetVolumeMatrix();
            if (m == null || (uint)symbolIndex >= (uint)m.Width || targetIndex >= m.RowCount) return double.NaN;

            // 成交量严格保持物理原始量（Raw 股数），防止价格复权乘法虚造成交量放大
            return m[targetIndex, symbolIndex];
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetVolume(string symbol, int offset = 0, PriceMode mode = PriceMode.Raw)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetVolume(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetField(int symbolIndex, string fieldName, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            if (offset > 0) ThrowFutureOffsetException(offset);
            if (string.IsNullOrWhiteSpace(fieldName)) return double.NaN;

            // 高频 OHLCV 走极速直通分支
            if (fieldName.Equals("close", StringComparison.OrdinalIgnoreCase)) return GetClose(symbolIndex, offset, mode);
            if (fieldName.Equals("open", StringComparison.OrdinalIgnoreCase)) return GetOpen(symbolIndex, offset, mode);
            if (fieldName.Equals("high", StringComparison.OrdinalIgnoreCase)) return GetHigh(symbolIndex, offset, mode);
            if (fieldName.Equals("low", StringComparison.OrdinalIgnoreCase)) return GetLow(symbolIndex, offset, mode);
            if (fieldName.Equals("volume", StringComparison.OrdinalIgnoreCase)) return GetVolume(symbolIndex, offset, mode);

            if (CurrentIndex < 0 || !Timeseries.HasField(fieldName)) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            var m = Timeseries.GetMatrix<double>(fieldName);
            if (m == null || (uint)symbolIndex >= (uint)m.Width || targetIndex >= m.RowCount) return double.NaN;
            double raw = m[targetIndex, symbolIndex];

            // 仅对价格类字段在 Post 模式下乘复权因子，比率与统计类字段保持原始物理值
            bool isPriceField = _priceFields.Contains(fieldName);
            if (mode != PriceMode.Post || !isPriceField || _factorMatrix == null) return raw;

            int dateRow = (_stepToDateIndex != null && (uint)targetIndex < (uint)_stepToDateIndex.Length)
                ? _stepToDateIndex[targetIndex]
                : targetIndex;
            if ((uint)dateRow >= (uint)_factorMatrix.RowCount || (uint)symbolIndex >= (uint)_factorMatrix.Width) return raw;

            return raw * _factorMatrix[dateRow, symbolIndex];
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetField(string symbol, string fieldName, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = Timeseries.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetField(idx, fieldName, offset, mode) : double.NaN;
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
