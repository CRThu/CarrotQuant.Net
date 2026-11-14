using System;
using System.Collections.Concurrent;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Data;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 策略运行上下文的标准权威实现。
    /// 在回测全生命周期内作为单一常驻上下文容器，提供对行情时序、离散事件、维表数据、虚拟经纪商与黑板状态的统一供给。
    /// 具有热路径零堆分配、类型安全与轻量扩展注入特性。
    /// </summary>
    public class EngineContext : IEngineContext
    {
        private readonly ConcurrentDictionary<Type, object> _extensions = new();

        /// <inheritdoc />
        public DateTime CurrentTime { get; internal set; }

        /// <inheritdoc />
        public int CurrentIndex { get; internal set; }

        /// <summary>
        /// 供引擎在主时钟步进时原子更新当前时间与行索引（热路径内联）。
        /// </summary>
        /// <param name="index">当前时步索引。</param>
        /// <param name="time">当前时步时间戳。</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal void UpdateStep(int index, DateTime time)
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
        public IMarketState Market { get; }

        /// <summary>
        /// 初始化引擎运行时上下文。
        /// </summary>
        /// <param name="timeseries">时序行情注册表。</param>
        /// <param name="broker">经纪商执行接口。</param>
        /// <param name="market">市场状态黑板（若为空则自动构建默认 MarketState 实例）。</param>
        /// <param name="events">异构事件流注册表（若为空则自动构建默认 EventRegistry 实例）。</param>
        /// <param name="fields">标的维表注册表（若为空则自动构建默认 FieldRegistry 实例）。</param>
        public EngineContext(
            ITimeseriesRegistry timeseries,
            IBroker broker,
            IMarketState? market = null,
            IEventRegistry? events = null,
            IFieldRegistry? fields = null)
        {
            Timeseries = timeseries ?? throw new ArgumentNullException(nameof(timeseries));
            Broker = broker ?? throw new ArgumentNullException(nameof(broker));
            Market = market ?? new MarketState();
            Events = events ?? new EventRegistry();
            Fields = fields ?? new FieldRegistry();

            CurrentIndex = -1;
            CurrentTime = DateTime.MinValue;
        }

        /// <inheritdoc />
        public T? GetExtension<T>() where T : class
        {
            return _extensions.TryGetValue(typeof(T), out var ext) ? (T)ext : null;
        }

        /// <summary>
        /// 注册或更新自定义上下文扩展组件（如监控器、分析记录器等）。
        /// </summary>
        /// <typeparam name="T">扩展组件类型约束。</typeparam>
        /// <param name="extension">扩展组件实例。</param>
        public void SetExtension<T>(T extension) where T : class
        {
            ArgumentNullException.ThrowIfNull(extension);
            _extensions[typeof(T)] = extension;
        }
    }
}
