using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
using CarrotBacktesting.NET.Indicators;
using CarrotBacktesting.NET.Strategy;

namespace CarrotBacktesting.NET.Engine
{
    /// <summary>
    /// 单一内核回测驱动引擎（BacktestingEngine）。
    /// 统领时序滑动窗口、T+N 跨日结算驱动、异构离散事件派发、策略管线步进与状态机生命周期控制。
    /// 遵循热路径零堆分配（零 GC）原则，基于确定性时间步循环驱动与线程安全状态机。
    /// </summary>
    public class BacktestingEngine : IEngine, ICheckpointable, ICheckpointable<EngineCheckpointState>, IDisposable
    {
        private readonly ITimeseriesRegistry _timeseries;
        private readonly ITimeseriesScheduler? _timeseriesScheduler;
        private readonly IBroker _broker;
        private readonly IBrokerEngineDriver? _brokerDriver;
        private readonly IAccountSnapshotProvider? _snapshotProvider;
        private readonly ISimulatedExchange? _simExchange;
        private readonly IStrategyPipeline _pipeline;
        private readonly IEventRegistry _events;
        private readonly IFieldRegistry _fields;
        private readonly IMarketState _market;
        private readonly EngineConfig _config;
        private readonly IMonitor _monitor;
        private readonly ITradingRecorder _recorder;
        private readonly IProgress<EngineProgress>? _progressReporter;
        private readonly AdjustmentConfig _adjustConfig;
        private readonly IMarketDataProvider _marketData;
        private readonly IndicatorRegistry _indicators;

        private readonly IStorageResolver? _resolver;
        private MarketTime _lastDate = MarketTime.MinValue;

        private const int StateVersion = 1;
        private int _resumeFromIndex = -1;

        private readonly EngineContext _context;
        private readonly object _stateLock = new();
        private readonly ManualResetEventSlim _pauseEvent = new(true);
        private readonly List<IDataRow> _eventBuffer = new();

        private volatile EngineStatus _status = EngineStatus.Idle;
        private volatile bool _stopRequested = false;
        private double _progress = 0.0;
        private bool _disposed = false;

        private readonly Action<long, OrderStatus> _orderStatusHandler;
        private readonly Action<ExecutionReport> _tradeHandler;
        private readonly Action<SettlementRecord> _settlementHandler;
        private readonly Action<Trade> _tradeClosedHandler;
        private readonly Action<PositionEvent> _positionHandler;
        private readonly Action<OrderEvent> _orderEventHandler;
        private bool _isSubscribed = false;

        /// <inheritdoc />
        public EngineStatus Status => _status;

        /// <inheritdoc />
        public double Progress => _progress;

        /// <summary>
        /// 是否成功加载并启用了复权因子。
        /// </summary>
        public bool HasAdjustments => _marketData.HasAdjustments;

        /// <summary>
        /// 获取当前引擎挂载的交易事实记录器。
        /// </summary>
        public ITradingRecorder Recorder => _recorder;

        /// <summary>
        /// 获取当前回测的结果集，包含已平仓交易、未平仓持仓、净值曲线与每日快照。
        /// </summary>
        public TradingResult GetResult()
        {
            Func<string, double>? finalPriceResolver = null;
            IReadOnlyMatrix2D<double>? closeMatrix = null;
            try
            {
                closeMatrix = _timeseries.GetMatrix<double>("close");
            }
            catch { }

            if (closeMatrix != null)
            {
                var metadata = _timeseries.Metadata;
                int finalIndex = _context.CurrentIndex >= 0 ? _context.CurrentIndex : closeMatrix.RowCount - 1;
                if (finalIndex >= 0 && finalIndex < closeMatrix.RowCount)
                {
                    finalPriceResolver = sym =>
                    {
                        int col = metadata.GetSymbolIndex(sym);
                        if (col >= 0 && col < closeMatrix.Width)
                        {
                            double p = closeMatrix[finalIndex, col];
                            if (p > 0.0 && !double.IsNaN(p) && !double.IsInfinity(p)) return p;
                        }
                        return 0.0;
                    };
                }
            }

            var endingPositions = _snapshotProvider?.GetActivePositions(finalPriceResolver) ?? Array.Empty<PositionSnapshot>();
            var status = _brokerDriver?.Status ?? TradingStatus.Normal;
            return _recorder.GetResult(endingPositions, status);
        }

        /// <inheritdoc />
        public MarketTime CurrentTime => _context.CurrentTime;

        /// <summary>
        /// 获取当前引擎持有的策略运行上下文。
        /// </summary>
        public IEngineContext Context => _context;

        /// <summary>
        /// 全参数显式构造函数，实现外部完全解耦与依赖注入。
        /// </summary>
        /// <param name="timeseries">时序行情注册表。</param>
        /// <param name="broker">交易与账户经纪商。</param>
        /// <param name="pipeline">策略管线。</param>
        /// <param name="events">异构事件流注册表（可选）。</param>
        /// <param name="fields">标的维表注册表（可选）。</param>
        /// <param name="market">市场黑板（可选）。</param>
        /// <param name="config">引擎运行配置（可选）。</param>
        /// <param name="monitor">监控与日志记录器（可选，缺省为 NullMonitor）。</param>
        /// <param name="simExchange">仿真交易所（可选，若未指定则自动自 broker.Gateway 感知）。</param>
        /// <param name="adjustConfig">除权除息配置（可选）。</param>
        /// <param name="recorder">交易事实记录器（可选，缺省自动构建默认 TradingRecorder）。</param>
        /// <param name="progress">批处理进度接收器（可选，若未指定且 monitor 实现了 IProgress&lt;EngineProgress&gt; 则自动复用）。</param>
        /// <param name="resolver">存储目录调度器（可选，用于除权数据及缓存按需解析）。</param>
        public BacktestingEngine(
            ITimeseriesRegistry timeseries,
            IBroker broker,
            IStrategyPipeline pipeline,
            IEventRegistry? events = null,
            IFieldRegistry? fields = null,
            IMarketState? market = null,
            EngineConfig? config = null,
            IMonitor? monitor = null,
            ISimulatedExchange? simExchange = null,
            AdjustmentConfig? adjustConfig = null,
            ITradingRecorder? recorder = null,
            IProgress<EngineProgress>? progress = null,
            IStorageResolver? resolver = null)
        {
            _timeseries = timeseries ?? throw new ArgumentNullException(nameof(timeseries));
            _timeseriesScheduler = timeseries as ITimeseriesScheduler;
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));
            _brokerDriver = broker as IBrokerEngineDriver;
            _snapshotProvider = broker as IAccountSnapshotProvider;
            _simExchange = simExchange ?? (broker as ISimulatedExchange) ?? ((broker as IExchangeGatewayProvider)?.Gateway as ISimulatedExchange);
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _config = config ?? new EngineConfig();
            if (_config.UpdatePhase != EnginePhase.Open && _config.UpdatePhase != EnginePhase.Close)
            {
                throw new ArgumentException(
                    $"EngineConfig.UpdatePhase 仅支持 EnginePhase.Open 或 EnginePhase.Close，不支持配置为 '{_config.UpdatePhase}'。",
                    nameof(config));
            }
            _monitor = monitor ?? NullMonitor.Instance;
            _adjustConfig = adjustConfig ?? new AdjustmentConfig();
            _recorder = recorder ?? new TradingRecorder();
            _progressReporter = progress ?? (monitor as IProgress<EngineProgress>);
            _resolver = resolver ?? (!string.IsNullOrWhiteSpace(_adjustConfig.StorageRoot) ? new StorageResolver(_adjustConfig.StorageRoot) : null);

            _marketData = new MarketDataProvider(timeseries, _adjustConfig, _resolver, events, _monitor);
            _context = new EngineContext(timeseries, broker, market, events, fields, priceFields: _adjustConfig.PriceFields, recorder: _recorder, marketData: _marketData);
            _events = _context.Events;
            _fields = _context.Fields;
            _market = _context.Market;
            _indicators = _context.Indicators;

            _orderStatusHandler = (orderId, status) => _pipeline.OnOrderStatusChanged(_context, orderId, status);
            _tradeHandler = (report) => _pipeline.OnTrade(_context, report);
            _settlementHandler = (settlement) => _recorder.Record(settlement);
            _tradeClosedHandler = (closedTrade) => _recorder.Record(closedTrade);
            _positionHandler = (posEvent) => _pipeline.OnPositionChanged(_context, posEvent);
            _orderEventHandler = (orderEvent) => _recorder.Record(orderEvent);
        }

        /// <summary>
        /// 根据会话配置构建回测引擎实例。
        /// 若未显式提供 timeseries，将根据 sessionConfig.Data 自动构建对应的数据层。
        /// </summary>
        /// <param name="sessionConfig">会话级总配置。</param>
        /// <param name="timeseries">时序行情供给源（可选，缺省由 sessionConfig.Data 自动构建）。</param>
        /// <param name="pipeline">外部构建或传入的策略管线（若为空则依据 sessionConfig.Pipeline 动态组装）。</param>
        /// <param name="events">事件注册表（可选，缺省由 sessionConfig.Data.Events 自动构建）。</param>
        /// <param name="fields">维表注册表（可选，缺省由 sessionConfig.Data.Fields 自动构建）。</param>
        /// <param name="market">市场黑板（可选）。</param>
        /// <param name="monitor">监控接口（可选）。</param>
        /// <param name="recorder">交易事实记录器（可选）。</param>
        /// <returns>已装配就绪的 BacktestingEngine 实例。</returns>
        public static BacktestingEngine Create(
            SessionConfig sessionConfig,
            ITimeseriesRegistry? timeseries = null,
            IStrategyPipeline? pipeline = null,
            IEventRegistry? events = null,
            IFieldRegistry? fields = null,
            IMarketState? market = null,
            IMonitor? monitor = null,
            ITradingRecorder? recorder = null)
        {
            ArgumentNullException.ThrowIfNull(sessionConfig);

            ITimeseriesRegistry activeTimeseries = timeseries!;
            IEventRegistry? activeEvents = events;
            IFieldRegistry? activeFields = fields;

            if (activeTimeseries == null)
            {
                var data = DataLayerFactory.Create(sessionConfig.Data, sessionConfig.Engine.StartDate, sessionConfig.Engine.EndDate, isLive: false);
                activeTimeseries = data.Timeseries;
                activeEvents ??= data.Events;
                activeFields ??= data.Fields;
            }

            var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker, activeFields);
            var broker = new Broker.Broker(simGateway, sessionConfig.Broker, activeFields, monitor);

            IStrategyPipeline activePipeline;
            if (pipeline != null)
            {
                activePipeline = pipeline;
            }
            else if (sessionConfig.Pipeline != null)
            {
                activePipeline = StrategyPipelineFactory.Create(sessionConfig.Pipeline);
            }
            else
            {
                activePipeline = StrategyPipeline.Sequential();
            }

            var adjConfig = sessionConfig.Data.Adjust;
            IStorageResolver? resolver = null;
            if (!string.IsNullOrWhiteSpace(sessionConfig.Data.Root))
            {
                resolver = new StorageResolver(sessionConfig.Data.Root);
            }
            else if (adjConfig != null && !string.IsNullOrWhiteSpace(adjConfig.StorageRoot))
            {
                resolver = new StorageResolver(adjConfig.StorageRoot);
            }

            return new BacktestingEngine(
                activeTimeseries,
                broker,
                activePipeline,
                activeEvents,
                activeFields,
                market,
                sessionConfig.Engine,
                monitor,
                simExchange: simGateway,
                adjustConfig: adjConfig,
                recorder: recorder,
                resolver: resolver);
        }

        /// <inheritdoc />
        public void Run()
        {
            RunInternal(CancellationToken.None);
        }

        /// <inheritdoc />
        public Task RunAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => RunInternal(cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 引擎主时钟驱动循环内部单点实现（单一内核）。
        /// </summary>
        private void RunInternal(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            lock (_stateLock)
            {
                if (_status != EngineStatus.Idle)
                {
                    throw new InvalidOperationException($"引擎当前处于 {_status} 状态，无法启动运行。");
                }
                _status = EngineStatus.Running;
                _stopRequested = false;
                _pauseEvent.Set();
            }

            try
            {
                // 1. 密封固化策略拓扑与 Lookback
                _pipeline.Compile();

                // 2. 桥接 Broker 事件通知到 Pipeline
                SubscribeBrokerEvents();

                // 2.1 重置指标注册中心
                _indicators.Reset();

                // 3. 获取时间坐标系与区间裁剪
                var tradeDates = _timeseries.Metadata.TradeDates;
                if (tradeDates == null || tradeDates.Count == 0)
                {
                    _monitor.Log("日历元数据中 TradeDates 为空，回测直接完成。", LogLevel.Warning);
                    FinishRun(EngineStatus.Completed);
                    return;
                }

                int startIndex = 0;
                int endIndex = tradeDates.Count - 1;

                if (_config.StartDate.HasValue)
                {
                    MarketTime start = _config.StartDate.Value;
                    startIndex = FindFirstDateIndex(tradeDates, start);
                    if (startIndex < 0 || startIndex >= tradeDates.Count)
                    {
                        _monitor.Log($"配置的起测日期 {start:yyyy-MM-dd} 超出日历范围，回测终止。", LogLevel.Warning);
                        FinishRun(EngineStatus.Completed);
                        return;
                    }
                }

                if (_config.EndDate.HasValue)
                {
                    MarketTime end = _config.EndDate.Value;
                    if ((end.Ticks % TimeSpan.TicksPerDay) == 0)
                    {
                        end = end.Date.AddDays(1).AddTicks(-1);
                    }
                    endIndex = FindLastDateIndex(tradeDates, end);
                    if (endIndex < startIndex)
                    {
                        _monitor.Log($"配置的截至日期 {end:yyyy-MM-dd} 小于起测日期，回测终止。", LogLevel.Warning);
                        FinishRun(EngineStatus.Completed);
                        return;
                    }
                }

                // 若存在断点续跑索引，则从断点向后推进
                bool isResuming = _resumeFromIndex >= 0;
                if (isResuming)
                {
                    startIndex = Math.Max(startIndex, _resumeFromIndex);
                    _resumeFromIndex = -1;
                    if (startIndex > endIndex)
                    {
                        FinishRun(EngineStatus.Completed);
                        return;
                    }
                }

                // 4. 触发策略启动生命周期
                _pipeline.OnStart(_context);

                // 若存在起测点之前的历史数据（如断点续跑或显式配置了 StartDate 启动），策略在 OnStart 中刚刚注册就绪的指标池必须在此刻执行历史时序补齐预热
                if (startIndex > 0)
                {
                    PrewarmIndicatorsAfterRestore(startIndex - 1);
                }

                int totalSteps = endIndex - startIndex + 1;
                if (!isResuming)
                {
                    _lastDate = MarketTime.MinValue;
                }
                long runStartTimestamp = Stopwatch.GetTimestamp();

                double ResolveStepPrice(string symbol, int step) => _marketData.GetLatestClose(symbol, step);

                // 5. 核心时间步驱动循环（热路径零分配）
                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (_stopRequested || cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (_status == EngineStatus.Paused)
                    {
                        _pauseEvent.Wait(cancellationToken);
                        if (_stopRequested || cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                    }

                    MarketTime currentTime = tradeDates[i];

                    // 5.1 更新上下文
                    _context.UpdateStep(i, currentTime);

                    // 5.1.1 跨日检测与日终快照沉淀
                    if (currentTime.Date != _lastDate)
                    {
                        if (_lastDate != MarketTime.MinValue && _snapshotProvider != null && i > startIndex)
                        {
                            int prevIndex = i - 1;
                            _recorder.Record(_snapshotProvider.CreateDailySnapshot(
                                tradeDates[prevIndex],
                                sym => ResolveStepPrice(sym, prevIndex)));
                        }
                        _lastDate = currentTime.Date;
                    }

                    // 5.2 时序滑动窗口与预读驱动
                    _timeseriesScheduler?.StepTo(i);

                    // ==================== Phase 1: PreMarket 相位 (盘前准备) ====================
                    _context.Phase = EnginePhase.PreMarket;
                    // 推进经纪商盘前时钟与跨日结算（T+N 在途持仓解锁，先结算后可用）
                    _brokerDriver?.PreMarketStep(currentTime);

                    // ==================== Phase 2: Open 相位 (开盘阶段) ====================
                    _context.Phase = EnginePhase.Open;
                    // 2.1 驱动仿真交易所执行开盘切片撮合（仅依据 bar.Open 撮合在途限价单与跨步单，严禁偷看当期 Bar 的 High/Low）
                    _simExchange?.Step(currentTime, _marketData, i, EnginePhase.Open);

                    // 2.2 确定本时间步策略决策时态相位（优先遵循管线策略的显式声明，否则遵循引擎配置）
                    EnginePhase effectivePhase = _pipeline.UpdatePhase != EnginePhase.Close
                        ? _pipeline.UpdatePhase
                        : _config.UpdatePhase;

                    // 2.3 若 UpdatePhase == EnginePhase.Open：执行开盘策略决策
                    if (effectivePhase == EnginePhase.Open)
                    {
                        _pipeline.OnUpdate(_context);

                        // 若策略在开盘发出了开盘价撮合订单，在此即刻完成开盘撮合；
                        // 若配置为 Close / Vwap / Average，则保留在 OrderBook 中挂起，留待收盘相位集中撮合
                        var execPrice = (_simExchange as SimulatedExchangeGateway)?.Config.DefaultExecutionPrice 
                            ?? (_broker as CarrotBacktesting.NET.Engine.Broker.Broker)?.Config.DefaultExecutionPrice 
                            ?? ExecutionPriceType.Open;

                        if (execPrice == ExecutionPriceType.Open)
                        {
                            _simExchange?.Step(currentTime, _marketData, i, EnginePhase.Open);
                        }
                    }

                    // ==================== Phase 3: Intraday 相位 (盘中阶段) ====================
                    _context.Phase = EnginePhase.Intraday;
                    // 3.1 驱动盘中条件单判定（针对已触发的价格条件单）
                    _brokerDriver?.ProcessConditionalOrders(currentTime);

                    // 3.2 离散事件常数时间轻量探测与派发
                    if (_events.HasNewEvents(currentTime))
                    {
                        DispatchEvents(currentTime);
                    }

                    // ==================== Phase 4: Close 相位 (收盘阶段) ====================
                    _context.Phase = EnginePhase.Close;
                    // 4.1 驱动指标注册中心步进更新（单步单次批量泵送当期完整 OHLCV）
                    _indicators.Step(_context);

                    // 4.2 驱动仿真交易所执行收盘撮合（针对开盘挂起至收盘成交的 VWAP / Close 订单）
                    _simExchange?.Step(currentTime, _marketData, i, EnginePhase.Close);

                    // 4.3 若 UpdatePhase == EnginePhase.Close：执行收盘策略决策（常规日频基准，委托进入挂单簿留待次日撮合）
                    if (effectivePhase == EnginePhase.Close)
                    {
                        _pipeline.OnUpdate(_context);
                    }

                    // ==================== Phase 5: PostMarket 相位 (盘后结算与快照) ====================
                    _context.Phase = EnginePhase.PostMarket;
                    // 5.1 日终穿仓与破产清算核验
                    _brokerDriver?.CheckLiquidation(currentTime);

                    // 5.2 记录当前时间步净值点
                    if (_snapshotProvider != null)
                    {
                        int currentIdx = i;
                        _recorder.Record(_snapshotProvider.CreateEquityPoint(
                            currentTime,
                            sym => ResolveStepPrice(sym, currentIdx)));
                    }

                    // 5.3 上报执行进度
                    int currentStep = i - startIndex + 1;
                    _progress = (double)currentStep / totalSteps;
                    if (_progressReporter != null)
                    {
                        TimeSpan elapsed = Stopwatch.GetElapsedTime(runStartTimestamp);
                        _progressReporter.Report(new EngineProgress(currentStep, totalSteps, currentTime, elapsed));
                    }
                }

                // 记录最终交易日收盘快照
                if (_snapshotProvider != null && endIndex >= startIndex)
                {
                    _recorder.Record(_snapshotProvider.CreateDailySnapshot(
                        tradeDates[endIndex],
                        sym => ResolveStepPrice(sym, endIndex)));
                }

                // 终局边界清理：回测结束时，由网关统一撤销挂单簿中所有在途未成交挂单，确保资产 100% 解冻归位
                if (endIndex >= 0 && endIndex < tradeDates.Count)
                {
                    _simExchange?.CloseGateway(tradeDates[endIndex]);
                }

                // 6. 正常收尾触发策略终止
                _pipeline.OnStop(_context);
                FinishRun(EngineStatus.Completed);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    _pipeline.OnStop(_context);
                }
                catch { }
                FinishRun(EngineStatus.Completed);
            }
            catch (ObjectDisposedException)
            {
                try
                {
                    _pipeline.OnStop(_context);
                }
                catch { }
                FinishRun(EngineStatus.Completed);
            }
            catch (Exception ex)
            {
                _monitor.Log($"回测主循环发生未捕获异常: {ex.Message}", LogLevel.Error);
                try
                {
                    _pipeline.OnStop(_context);
                }
                catch { }
                FinishRun(EngineStatus.Faulted);
                throw;
            }
            finally
            {
                UnsubscribeBrokerEvents();
            }
        }

        private void DispatchEvents(MarketTime currentTime)
        {
            var tableNames = _events.GetTableNames();
            for (int t = 0; t < tableNames.Count; t++)
            {
                string tableName = tableNames[t];
                var table = _events.GetTable(tableName);

                _eventBuffer.Clear();

                // 1. 全局广播事件
                if (table.TryGetGlobal(currentTime, out var globalRows) && globalRows.Count > 0)
                {
                    _eventBuffer.AddRange(globalRows);
                }

                // 2. 标的截面事件
                if (table.TryGetDay(currentTime, out var dayMap) && dayMap != null && dayMap.Count > 0)
                {
                    foreach (var kvp in dayMap)
                    {
                        if (kvp.Value.Count > 0)
                        {
                            _eventBuffer.AddRange(kvp.Value);
                        }
                    }
                }

                if (_eventBuffer.Count > 0)
                {
                    _pipeline.OnEvent(_context, tableName, _eventBuffer);
                }
            }
        }

        private void SubscribeBrokerEvents()
        {
            if (!_isSubscribed)
            {
                _broker.OnOrderStatusChanged += _orderStatusHandler;
                _broker.OnTrade += _tradeHandler;
                _broker.OnPositionChanged += _positionHandler;
                _broker.OnOrderEvent += _orderEventHandler;
                if (_snapshotProvider != null)
                {
                    _snapshotProvider.OnSettlement += _settlementHandler;
                    _snapshotProvider.OnTradeClosed += _tradeClosedHandler;
                }
                _isSubscribed = true;
            }
        }

        private void UnsubscribeBrokerEvents()
        {
            if (_isSubscribed)
            {
                _broker.OnOrderStatusChanged -= _orderStatusHandler;
                _broker.OnTrade -= _tradeHandler;
                _broker.OnPositionChanged -= _positionHandler;
                _broker.OnOrderEvent -= _orderEventHandler;
                if (_snapshotProvider != null)
                {
                    _snapshotProvider.OnSettlement -= _settlementHandler;
                    _snapshotProvider.OnTradeClosed -= _tradeClosedHandler;
                }
                _isSubscribed = false;
            }
        }

        private void FinishRun(EngineStatus endStatus)
        {
            lock (_stateLock)
            {
                _status = endStatus;
                if (!_disposed)
                {
                    try
                    {
                        _pauseEvent.Set();
                    }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        /// <inheritdoc />
        public void Pause()
        {
            lock (_stateLock)
            {
                if (!_disposed && _status == EngineStatus.Running)
                {
                    _status = EngineStatus.Paused;
                    try { _pauseEvent.Reset(); } catch (ObjectDisposedException) { }
                }
            }
        }

        /// <inheritdoc />
        public void Resume()
        {
            lock (_stateLock)
            {
                if (!_disposed && _status == EngineStatus.Paused)
                {
                    _status = EngineStatus.Running;
                    try { _pauseEvent.Set(); } catch (ObjectDisposedException) { }
                }
            }
        }

        /// <inheritdoc />
        public void Stop()
        {
            lock (_stateLock)
            {
                if (!_disposed && (_status == EngineStatus.Running || _status == EngineStatus.Paused))
                {
                    _stopRequested = true;
                    try { _pauseEvent.Set(); } catch (ObjectDisposedException) { }
                }
            }
        }

        /// <inheritdoc />
        public EngineCheckpointState SaveState()
        {
            lock (_stateLock)
            {
                BrokerSnapshot brokerSnap = (_broker as ICheckpointable<BrokerSnapshot>)?.SaveState()
                    ?? throw new NotSupportedException($"当前 Broker 实例 '{_broker.GetType().FullName}' 未实现 ICheckpointable<BrokerSnapshot>，无法抓取检查点。");

                ExchangeSnapshot exchangeSnap = (_simExchange as ICheckpointable<ExchangeSnapshot>)?.SaveState()
                    ?? throw new NotSupportedException($"当前 Exchange 实例 '{_simExchange?.GetType().FullName}' 未实现 ICheckpointable<ExchangeSnapshot>，无法抓取检查点。");

                TradingRecorderSnapshot? recorderSnap = (_recorder as ICheckpointable<TradingRecorderSnapshot>)?.SaveState();

                MarketStateSnapshot? marketSnap = (_context.Market as ICheckpointable<MarketStateSnapshot>)?.SaveState();

                return new EngineCheckpointState(
                    StateVersion,
                    _context.CurrentIndex,
                    _context.CurrentTime,
                    _lastDate,
                    _progress,
                    brokerSnap,
                    exchangeSnap,
                    recorderSnap,
                    marketSnap,
                    _context.Phase
                );
            }
        }

        /// <inheritdoc />
        public void RestoreState(EngineCheckpointState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            lock (_stateLock)
            {
                if (_status != EngineStatus.Idle)
                {
                    throw new InvalidOperationException($"仅可在引擎处于 Idle 状态时恢复检查点，当前状态为: {_status}");
                }

                if (state.Version != StateVersion)
                {
                    throw new InvalidDataException($"不支持的检查点数据版本: {state.Version}，当前引擎版本为: {StateVersion}。");
                }

                if (_broker is ICheckpointable<BrokerSnapshot> brokerCheckpointable)
                {
                    brokerCheckpointable.RestoreState(state.Broker);
                }

                if (_simExchange is ICheckpointable<ExchangeSnapshot> exchangeCheckpointable)
                {
                    exchangeCheckpointable.RestoreState(state.Exchange);
                }

                if (_recorder is ICheckpointable<TradingRecorderSnapshot> recorderCheckpointable && state.Recorder != null)
                {
                    recorderCheckpointable.RestoreState(state.Recorder);
                }

                if (_context.Market is ICheckpointable<MarketStateSnapshot> marketCheckpointable && state.Market != null)
                {
                    marketCheckpointable.RestoreState(state.Market);
                }

                _context.UpdateStep(state.CurrentIndex, state.CurrentTime);
                _context.Phase = state.Phase;
                _lastDate = state.LastDate;
                _progress = state.Progress;
                _resumeFromIndex = state.CurrentIndex >= 0 ? state.CurrentIndex + 1 : -1;

                PrewarmIndicatorsAfterRestore(state.CurrentIndex);

                _status = EngineStatus.Idle;
            }
        }

        private void PrewarmIndicatorsAfterRestore(int restoredIndex)
        {
            var dates = _timeseries.Metadata.TradeDates;
            if (restoredIndex < 0 || dates.Count == 0 || restoredIndex >= dates.Count)
            {
                return;
            }

            int lookback = Math.Max(20, _pipeline.Lookback);
            int startPrewarm = Math.Max(0, restoredIndex - lookback);

            _indicators.Reset();

            for (int k = startPrewarm; k <= restoredIndex; k++)
            {
                _context.UpdateStep(k, dates[k]);
                _timeseriesScheduler?.StepTo(k);
                _indicators.Step(_context);
            }

            _context.UpdateStep(restoredIndex, dates[restoredIndex]);
        }

        /// <inheritdoc />
        public byte[] SaveCheckpoint()
        {
            var state = SaveState();
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(state);
        }

        /// <inheritdoc />
        public void RestoreCheckpoint(byte[] checkpoint)
        {
            ArgumentNullException.ThrowIfNull(checkpoint);
            if (checkpoint.Length == 0)
            {
                throw new ArgumentException("恢复检查点数据字节数组不可为空。", nameof(checkpoint));
            }

            EngineCheckpointState state;
            try
            {
                state = System.Text.Json.JsonSerializer.Deserialize<EngineCheckpointState>(checkpoint)
                    ?? throw new InvalidDataException("反序列化检查点状态对象为空。");
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidDataException("无效的检查点数据，非合法 JSON 格式。", ex);
            }

            RestoreState(state);
        }

        #region 二分查找工具 (Binary Search Helpers)

        private static int FindFirstDateIndex(IReadOnlyList<MarketTime> dates, MarketTime target)
        {
            int low = 0;
            int high = dates.Count - 1;
            int result = -1;

            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                if (dates[mid] >= target)
                {
                    result = mid;
                    high = mid - 1;
                }
                else
                {
                    low = mid + 1;
                }
            }
            return result;
        }

        private static int FindLastDateIndex(IReadOnlyList<MarketTime> dates, MarketTime target)
        {
            int low = 0;
            int high = dates.Count - 1;
            int result = -1;

            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                if (dates[mid] <= target)
                {
                    result = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            return result;
        }

        #endregion

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;

            Stop();

            lock (_stateLock)
            {
                _disposed = true;
                try
                {
                    _pauseEvent.Dispose();
                }
                catch { }
            }

            UnsubscribeBrokerEvents();
            (_pipeline as IDisposable)?.Dispose();
            _context.Dispose();
        }
    }
}
