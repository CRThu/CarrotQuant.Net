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
    /// 策略引擎核心运行时上下文实现。
    /// 提供行情数据访问、交易执行、指标注册与异构事件流的统一注入。
    /// </summary>
    public sealed class EngineContext : IEngineContext, IDisposable
    {
        private readonly ConcurrentDictionary<Type, object> _extensions = new();

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
        public ITimeseriesRegistry Timeseries => MarketData.Timeseries;

        /// <inheritdoc />
        public IMarketDataProvider MarketData { get; }

        /// <inheritdoc />
        public IEventRegistry Events { get; }

        /// <inheritdoc />
        public IFieldRegistry Fields { get; }

        /// <inheritdoc />
        public IBroker Broker { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> Symbols => MarketData.Metadata.Symbols;

        /// <inheritdoc />
        public int SymbolCount => MarketData.Metadata.Symbols.Count;

        /// <inheritdoc />
        public int GetSymbolIndex(string symbol) => MarketData.Metadata.GetSymbolIndex(symbol);

        /// <inheritdoc />
        public ITradingRecorder? Recorder { get; }

        /// <inheritdoc />
        public void RecordSignal(string symbol, double signalValue, string? signalGroup = null, string? reason = null)
        {
            Recorder?.Record(new SignalRecord(CurrentTime, symbol, signalValue, signalGroup ?? string.Empty, reason ?? string.Empty));
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
        /// <param name="marketData">统一行情与复权数据提供器（可选）。</param>
        /// <param name="factorMatrix">除权因子矩阵（可选，用于显式装配复权数据）。</param>
        /// <param name="stepToDateIndex">回测时步到日历日索引映射数组（可选）。</param>
        public EngineContext(
            ITimeseriesRegistry timeseries,
            IBroker broker,
            IMarketState? market = null,
            IEventRegistry? events = null,
            IFieldRegistry? fields = null,
            IndicatorRegistry? indicators = null,
            IEnumerable<string>? priceFields = null,
            ITradingRecorder? recorder = null,
            IMarketDataProvider? marketData = null,
            IReadOnlyMatrix2D<double>? factorMatrix = null,
            int[]? stepToDateIndex = null)
        {
            ArgumentNullException.ThrowIfNull(timeseries);
            Broker = broker ?? throw new ArgumentNullException(nameof(broker));
            MarketData = marketData ?? new MarketDataProvider(timeseries, factorMatrix, stepToDateIndex, priceFields);
            Market = market ?? new MarketState();
            Events = events ?? new EventRegistry();
            Fields = fields ?? new FieldRegistry();
            Indicators = indicators ?? new IndicatorRegistry(timeseries.Metadata);
            Recorder = recorder;

            CurrentIndex = -1;
            CurrentTime = MarketTime.MinValue;
        }

        /// <summary>
        /// 释放持有的资源。
        /// </summary>
        public void Dispose()
        {
            (MarketData as IDisposable)?.Dispose();
        }

        #region 时序与复权取价门面实现

        /// <inheritdoc />
        public EnginePhase Phase { get; internal set; } = EnginePhase.Close;

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowFutureOffsetException(int offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, $"时序 offset 越界 (offset = {offset} > 0)。禁止指定正向偏移量以防止前瞻偏差（Look-ahead Bias）。");
        }

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowPreMarketLookAheadException(string fieldName)
        {
            throw new InvalidOperationException($"当前处于 PreMarket 盘前阶段，当日行情尚未产生，严禁读取当期 '{fieldName}'（offset = 0）以防前瞻偏差（Look-ahead Bias）。");
        }

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowPhaseLookAheadException(string fieldName, EnginePhase phase)
        {
            throw new InvalidOperationException($"当前处于 {phase} 决策阶段，当期 '{fieldName}' 尚未完结产生，严禁读取当期数据（offset = 0）以防前瞻偏差（Look-ahead Bias）。");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateOpenAccessSecurity(int offset)
        {
            if (offset > 0) ThrowFutureOffsetException(offset);
            if (offset < 0) return;

            // 当 offset == 0 时，Open 价格仅在 PreMarket 盘前尚未产生，其余所有时序相位（Open/Intraday/Close/PostMarket）均绝对物理合法
            if (Phase == EnginePhase.PreMarket)
            {
                ThrowPreMarketLookAheadException(StandardFields.Open);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateBarClosedAccessSecurity(string fieldName, int offset)
        {
            if (offset > 0) ThrowFutureOffsetException(offset);
            if (offset < 0) return;

            // 当 offset == 0 时，收盘/量能/极值等完结型行情仅在 Close 与 PostMarket 相位完结产生
            if (Phase < EnginePhase.Close)
            {
                if (Phase == EnginePhase.PreMarket)
                {
                    ThrowPreMarketLookAheadException(fieldName);
                }
                ThrowPhaseLookAheadException(fieldName, Phase);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateFieldAccessSecurity(string fieldName, int offset)
        {
            if (offset > 0) ThrowFutureOffsetException(offset);
            if (offset < 0) return;

            if (Phase < EnginePhase.Close)
            {
                if (Phase == EnginePhase.PreMarket)
                {
                    ThrowPreMarketLookAheadException(fieldName);
                }

                // 在 Open 或 Intraday 相位，仅允许访问 open；优先单指令指针比对，消除高频字符串开销
                if (ReferenceEquals(fieldName, StandardFields.Open) ||
                    fieldName.Equals(StandardFields.Open, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                ThrowPhaseLookAheadException(fieldName, Phase);
            }
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetClose(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            ValidateBarClosedAccessSecurity(StandardFields.Close, offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            return MarketData.GetField(symbolIndex, StandardFields.Close, targetIndex, mode);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetClose(string symbol, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = MarketData.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetClose(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetOpen(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            ValidateOpenAccessSecurity(offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            return MarketData.GetField(symbolIndex, StandardFields.Open, targetIndex, mode);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetOpen(string symbol, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = MarketData.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetOpen(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetHigh(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            ValidateBarClosedAccessSecurity(StandardFields.High, offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            return MarketData.GetField(symbolIndex, StandardFields.High, targetIndex, mode);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetHigh(string symbol, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = MarketData.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetHigh(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetLow(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            ValidateBarClosedAccessSecurity(StandardFields.Low, offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            return MarketData.GetField(symbolIndex, StandardFields.Low, targetIndex, mode);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetLow(string symbol, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = MarketData.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetLow(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetVolume(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Raw)
        {
            ValidateBarClosedAccessSecurity(StandardFields.Volume, offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            return MarketData.GetField(symbolIndex, StandardFields.Volume, targetIndex, mode);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetVolume(string symbol, int offset = 0, PriceMode mode = PriceMode.Raw)
        {
            int idx = MarketData.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetVolume(idx, offset, mode) : double.NaN;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetField(int symbolIndex, string fieldName, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return double.NaN;
            ValidateFieldAccessSecurity(fieldName, offset);
            if (CurrentIndex < 0) return double.NaN;

            int targetIndex = CurrentIndex + offset;
            if (targetIndex < 0) return double.NaN;

            return MarketData.GetField(symbolIndex, fieldName, targetIndex, mode);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetField(string symbol, string fieldName, int offset = 0, PriceMode mode = PriceMode.Post)
        {
            int idx = MarketData.Metadata.GetSymbolIndex(symbol);
            return idx >= 0 ? GetField(idx, fieldName, offset, mode) : double.NaN;
        }

        /// <summary>
        /// 将指定标的在策略层视角的后复权价格（Post）折算为底层执行与撮合的物理真实价格（Raw）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double ToRawPrice(int symbolIndex, double postPrice)
        {
            return MarketData.ToRawPrice(symbolIndex, postPrice, CurrentIndex);
        }

        /// <summary>
        /// 将指定标的在策略层视角的后复权价格（Post）折算为底层执行与撮合的物理真实价格（Raw）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double ToRawPrice(string symbol, double postPrice)
        {
            return MarketData.ToRawPrice(symbol, postPrice, CurrentIndex);
        }

        /// <summary>
        /// 将指定标的的底层物理真实价格（Raw）换算为后复权价格（Post）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double ToPostPrice(int symbolIndex, double rawPrice)
        {
            return MarketData.ToPostPrice(symbolIndex, rawPrice, CurrentIndex);
        }

        /// <summary>
        /// 将指定标的的底层物理真实价格（Raw）换算为后复权价格（Post）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double ToPostPrice(string symbol, double rawPrice)
        {
            return MarketData.ToPostPrice(symbol, rawPrice, CurrentIndex);
        }

        #endregion

        #region Trading Facade (Class-level Direct Access)

        /// <inheritdoc cref="IEngineContext.Buy" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long Buy(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string orderTag = "", TimeInForce? timeInForce = null, bool reduceOnly = false)
        {
            double execPrice = (price > 0.0 && type != OrderType.Market) ? ToRawPrice(symbol, price) : price;
            return Broker.Buy(symbol, quantity, execPrice, type, orderTag, timeInForce, reduceOnly);
        }

        /// <inheritdoc cref="IEngineContext.Sell" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long Sell(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string orderTag = "", TimeInForce? timeInForce = null, bool reduceOnly = false)
        {
            double execPrice = (price > 0.0 && type != OrderType.Market) ? ToRawPrice(symbol, price) : price;
            return Broker.Sell(symbol, quantity, execPrice, type, orderTag, timeInForce, reduceOnly);
        }

        /// <inheritdoc cref="IEngineContext.ClosePosition" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ClosePosition(string symbol, double price = 0.0, OrderType type = OrderType.Market, string orderTag = "", TimeInForce? timeInForce = null)
        {
            double execPrice = (price > 0.0 && type != OrderType.Market) ? ToRawPrice(symbol, price) : price;
            return Broker.ClosePosition(symbol, execPrice, type, orderTag, timeInForce);
        }

        /// <inheritdoc cref="IEngineContext.StopLoss" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long StopLoss(string symbol, double stopPrice, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
        {
            double execStop = stopPrice > 0.0 ? ToRawPrice(symbol, stopPrice) : stopPrice;
            return Broker.StopLoss(symbol, execStop, quantity, orderTag, timeInForce);
        }

        /// <inheritdoc cref="IEngineContext.TakeProfit" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long TakeProfit(string symbol, double targetPrice, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
        {
            double execTarget = targetPrice > 0.0 ? ToRawPrice(symbol, targetPrice) : targetPrice;
            return Broker.TakeProfit(symbol, execTarget, quantity, orderTag, timeInForce);
        }

        /// <inheritdoc cref="IEngineContext.Bracket" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (long StopOrderId, long TakeProfitOrderId) Bracket(string symbol, decimal quantity, double stopPrice, double takeProfitPrice, string orderTag = "", TimeInForce? timeInForce = null, OrderDirection? closeDirection = null)
        {
            double execStop = stopPrice > 0.0 ? ToRawPrice(symbol, stopPrice) : stopPrice;
            double execTake = takeProfitPrice > 0.0 ? ToRawPrice(symbol, takeProfitPrice) : takeProfitPrice;
            return Broker.Bracket(symbol, quantity, execStop, execTake, orderTag, timeInForce, closeDirection);
        }

        #endregion

        /// <inheritdoc />
        public T? GetExtension<T>() where T : class
        {
            return _extensions.TryGetValue(typeof(T), out var ext) ? (T)ext : null;
        }

        /// <summary>
        /// 注册或更新自定义上下文扩展组件。
        /// </summary>
        public void SetExtension<T>(T extension) where T : class
        {
            ArgumentNullException.ThrowIfNull(extension);
            _extensions[typeof(T)] = extension;
        }
    }
}
