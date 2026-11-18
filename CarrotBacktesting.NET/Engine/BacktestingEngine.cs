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
    public class BacktestingEngine : IEngine, ICheckpointable, IDisposable
    {
        private readonly ITimeseriesRegistry _timeseries;
        private readonly IBroker _broker;
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
        private readonly IndicatorRegistry _indicators;

        private bool _hasAdjustments = false;
        private IEventTable? _adjTable;
        private DateTime _lastDate = DateTime.MinValue;

        private const uint StateMagic = 0x43514254; // "CQBT"
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

        private readonly Action<string, OrderStatus> _orderStatusHandler;
        private readonly Action<ExecutionReport> _tradeHandler;
        private readonly Action<SettlementRecord> _settlementHandler;
        private readonly Action<Trade> _tradeClosedHandler;
        private readonly Action<PositionEvent> _positionHandler;
        private bool _isSubscribed = false;

        /// <inheritdoc />
        public EngineStatus Status => _status;

        /// <inheritdoc />
        public double Progress => _progress;

        /// <summary>
        /// 获取当前引擎挂载的交易事实记录器。
        /// </summary>
        public ITradingRecorder Recorder => _recorder;

        /// <summary>
        /// 获取当前回测的结果集，包含已平仓交易、未平仓持仓、净值曲线与每日快照。
        /// </summary>
        public TradingResult GetResult()
        {
            var endingPositions = _snapshotProvider?.GetActivePositions() ?? Array.Empty<PositionSnapshot>();
            return _recorder.GetResult(endingPositions);
        }

        /// <inheritdoc />
        public DateTime CurrentTime => _context.CurrentTime;

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
            IProgress<EngineProgress>? progress = null)
        {
            _timeseries = timeseries ?? throw new ArgumentNullException(nameof(timeseries));
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));
            _snapshotProvider = broker as IAccountSnapshotProvider;
            _simExchange = simExchange ?? (broker as ISimulatedExchange) ?? ((broker as Broker.Broker)?.Gateway as ISimulatedExchange);
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _config = config ?? new EngineConfig();
            _monitor = monitor ?? NullMonitor.Instance;
            _adjustConfig = adjustConfig ?? new AdjustmentConfig();
            _recorder = recorder ?? new TradingRecorder();
            _progressReporter = progress ?? (monitor as IProgress<EngineProgress>);

            _context = new EngineContext(timeseries, broker, market, events, fields, priceFields: _adjustConfig.PriceFields, recorder: _recorder);
            _events = _context.Events;
            _fields = _context.Fields;
            _market = _context.Market;
            _indicators = _context.Indicators;

            _orderStatusHandler = (orderId, status) => _pipeline.OnOrderStatusChanged(_context, orderId, status);
            _tradeHandler = (report) => _pipeline.OnTrade(_context, report);
            _settlementHandler = (settlement) => _recorder.RecordSettlement(settlement);
            _tradeClosedHandler = (closedTrade) => _recorder.RecordClosedTrade(closedTrade);
            _positionHandler = (posEvent) => _pipeline.OnPositionChanged(_context, posEvent);
        }

        /// <summary>
        /// 便捷静态工厂：根据整体会话配置与数据供给装配回测引擎。
        /// 若未传入 timeseries，将自动依据 sessionConfig.Data 闭环装配时序、事件与维表数据层。
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
            var broker = new Broker.Broker(simGateway, sessionConfig.Broker, activeFields);

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
                adjustConfig: sessionConfig.Data.Adjust,
                recorder: recorder);
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
                    DateTime start = _config.StartDate.Value;
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
                    DateTime end = _config.EndDate.Value;
                    if (end.TimeOfDay == TimeSpan.Zero)
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
                if (_resumeFromIndex >= 0)
                {
                    startIndex = Math.Max(startIndex, _resumeFromIndex);
                    _resumeFromIndex = -1;
                    if (startIndex > endIndex)
                    {
                        FinishRun(EngineStatus.Completed);
                        return;
                    }
                }

                // 3.1 预热复权因子（从头极速滚动至起测日并执行跨模态一致性覆盖校验）
                PrewarmAdjustments(tradeDates[startIndex]);

                // 4. 触发策略启动生命周期
                _pipeline.OnStart(_context);

                int totalSteps = endIndex - startIndex + 1;
                _lastDate = DateTime.MinValue;
                long runStartTimestamp = Stopwatch.GetTimestamp();

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

                    DateTime currentTime = tradeDates[i];

                    // 5.1 更新上下文
                    _context.UpdateStep(i, currentTime);

                    // 5.1.1 跨日检测与日终快照沉淀
                    if (currentTime.Date != _lastDate)
                    {
                        if (_lastDate != DateTime.MinValue && _snapshotProvider != null && i > startIndex)
                        {
                            _recorder.RecordDailySnapshot(_snapshotProvider.CreateDailySnapshot(tradeDates[i - 1]));
                        }
                        _lastDate = currentTime.Date;
                        if (_hasAdjustments && _events.HasNewEvents(currentTime, _adjustConfig.Table))
                        {
                            UpdateDailyFactors(currentTime);
                        }
                    }

                    // 5.2 时序滑动窗口与预读驱动
                    _timeseries.StepTo(i);

                    // 5.3 驱动仿真交易所批量撮合在途限价单与开盘挂单
                    _simExchange?.Step(currentTime, _timeseries, i);

                    // 5.4 推进经纪商时钟与交易所本地跨日结算（T+N 在途持仓解锁）
                    _broker.Step(currentTime);

                    // 5.4 离散事件常数时间轻量探测与派发
                    if (_events.HasNewEvents(currentTime))
                    {
                        DispatchEvents(currentTime);
                    }

                    // 5.4.1 驱动指标注册中心步进更新（单步单次批量泵送）
                    _indicators.Step(_context);

                    // 5.5 驱动策略主决策计算
                    _pipeline.OnUpdate(_context);

                    // 5.6 记录当前时间步净值点
                    if (_snapshotProvider != null)
                    {
                        _recorder.RecordStep(_snapshotProvider.CreateEquityPoint(currentTime));
                    }

                    // 5.7 上报执行进度
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
                    _recorder.RecordDailySnapshot(_snapshotProvider.CreateDailySnapshot(tradeDates[endIndex]));
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

        private void DispatchEvents(DateTime currentTime)
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

        /// <summary>
        /// 预热除权因子数组，从全历史第一条除权事件极速滚动至起测日（StartDate），
        /// 并执行行情与因子的跨模态覆盖校验（有行情无因子精准 Warning 告警）。
        /// </summary>
        private void PrewarmAdjustments(DateTime startDate)
        {
            if (!_adjustConfig.Mode.Equals("post", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string tableName = _adjustConfig.Table;
            if (!_events.HasTable(tableName))
            {
                throw new InvalidOperationException($"启用了后复权模式 (mode: post)，但在事件注册表中未找到指定的除权表 '{tableName}'。");
            }

            _adjTable = _events[tableName];
            if (_adjTable.Count == 0)
            {
                throw new InvalidOperationException($"启用了后复权模式 (mode: post)，但除权表 '{tableName}' 中的记录总数为 0。");
            }

            _hasAdjustments = true;
            int symbolCount = _timeseries.Metadata.Symbols.Count;
            var factors = new double[symbolCount];
            Array.Fill(factors, 1.0);

            // 1. 从历史第一条除权事件开始，按时间升序快速遍历至 startDate 覆写因子（< 0.5ms）
            foreach (var date in _adjTable.Dates)
            {
                if (date > startDate) break;

                if (_adjTable.TryGetDay(date, out var dayEvents) && dayEvents != null)
                {
                    foreach (var (symbol, rows) in dayEvents)
                    {
                        if (rows.Count > 0)
                        {
                            var row = rows[^1];
                            double factor = row.GetDouble(_adjustConfig.FactorField, 0.0);
                            if (factor > 0.0)
                            {
                                int sIdx = _timeseries.Metadata.GetSymbolIndex(symbol);
                                if (sIdx >= 0)
                                {
                                    factors[sIdx] = factor;
                                }
                            }
                        }
                    }
                }
            }

            // 2. 统计除权表中每只标的首次提供复权因子的最早日期（用于一一对应严格校验）
            var firstAdjDates = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var date in _adjTable.Dates)
            {
                if (_adjTable.TryGetDay(date, out var dayEvents) && dayEvents != null)
                {
                    foreach (var (symbol, rows) in dayEvents)
                    {
                        if (rows.Count > 0 && !firstAdjDates.ContainsKey(symbol))
                        {
                            firstAdjDates[symbol] = date;
                        }
                    }
                }
            }

            // 3. 行情与因子的跨模态覆盖与时序前置严格校验（任何一天产生行情之前未给过复权必报警）
            IReadOnlyMatrix2D<double>? closeMatrix = null;
            try { closeMatrix = _timeseries.GetMatrix<double>("close"); } catch { }

            var unassignedSymbols = new List<string>();
            var tradeDates = _timeseries.Metadata.TradeDates;

            for (int s = 0; s < symbolCount; s++)
            {
                string symbol = _timeseries.Metadata.Symbols[s];

                // 寻找该标的首次出现有效价格的日期（上市日或首次交易日）
                DateTime? firstTradeDate = null;
                if (closeMatrix != null && s < closeMatrix.Width)
                {
                    int rowCount = closeMatrix.RowCount;
                    for (int r = 0; r < rowCount; r++)
                    {
                        double p = closeMatrix[r, s];
                        if (!double.IsNaN(p) && p > 0.0)
                        {
                            firstTradeDate = r < tradeDates.Count ? tradeDates[r] : startDate;
                            break;
                        }
                    }
                }

                // 若标的存在有效交易行情
                if (firstTradeDate.HasValue)
                {
                    if (!firstAdjDates.TryGetValue(symbol, out var firstAdjDate))
                    {
                        // 标的有行情，但在除权表中完全无任何记录
                        unassignedSymbols.Add($"{symbol}(首日:{firstTradeDate.Value:yyyy-MM-dd},除权表无记录)");
                    }
                    else if (firstAdjDate > firstTradeDate.Value)
                    {
                        // 标的已产生行情，但在产生行情之前/首日未提供复权因子（例如2025上市首条除权在2026）
                        unassignedSymbols.Add($"{symbol}(首日:{firstTradeDate.Value:yyyy-MM-dd},首条除权:{firstAdjDate:yyyy-MM-dd})");
                    }
                }
            }

            if (unassignedSymbols.Count > 0)
            {
                string samples = string.Join(", ", unassignedSymbols.Take(5));
                _monitor.Log(
                    $"[Warning] 发现 {unassignedSymbols.Count} 只标的在产生有效行情前未在除权表 '{tableName}' 中提供过复权因子（未严格一一对应，已按 1.0 补齐，存在严重漏配风险！）。样本: [{samples}{(unassignedSymbols.Count > 5 ? ", ..." : "")}]",
                    LogLevel.Warning);
            }

            // 4. 整体注入上下文基准数组
            _context.SetDailyFactors(factors);
        }

        private void UpdateDailyFactors(DateTime currentTime)
        {
            if (_adjTable == null) return;
            if (_adjTable.TryGetDay(currentTime.Date, out var dayEvents) && dayEvents != null)
            {
                foreach (var (symbol, rows) in dayEvents)
                {
                    if (rows.Count > 0)
                    {
                        var row = rows[^1];
                        double factor = row.GetDouble(_adjustConfig.FactorField, 0.0);
                        if (factor > 0.0)
                        {
                            int sIdx = _timeseries.Metadata.GetSymbolIndex(symbol);
                            if (sIdx >= 0)
                            {
                                _context.UpdateFactor(sIdx, factor);
                            }
                        }
                    }
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
                if (_broker is Broker.Broker realBroker)
                {
                    realBroker.OnSettlement += _settlementHandler;
                    realBroker.OnTradeClosed += _tradeClosedHandler;
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
                if (_broker is Broker.Broker realBroker)
                {
                    realBroker.OnSettlement -= _settlementHandler;
                    realBroker.OnTradeClosed -= _tradeClosedHandler;
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
        public byte[] SaveCheckpoint()
        {
            lock (_stateLock)
            {
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                writer.Write(StateMagic);
                writer.Write(StateVersion);
                writer.Write((int)_status);
                writer.Write(_context.CurrentIndex);
                writer.Write(_context.CurrentTime.Ticks);
                writer.Write(_progress);
                return ms.ToArray();
            }
        }

        /// <inheritdoc />
        public void RestoreCheckpoint(byte[] checkpoint)
        {
            ArgumentNullException.ThrowIfNull(checkpoint);
            if (checkpoint.Length < 32)
            {
                throw new ArgumentException($"恢复检查点数据字节长度不足，预期至少 32 字节，实际为 {checkpoint.Length} 字节。", nameof(checkpoint));
            }

            lock (_stateLock)
            {
                if (_status != EngineStatus.Idle)
                {
                    throw new InvalidOperationException($"仅可在引擎处于 Idle 状态时恢复检查点，当前状态为: {_status}");
                }

                using var ms = new MemoryStream(checkpoint);
                using var reader = new BinaryReader(ms);
                uint magic = reader.ReadUInt32();
                if (magic != StateMagic)
                {
                    throw new InvalidDataException($"无效的检查点数据魔数: 0x{magic:X8}，期望为 0x{StateMagic:X8}。");
                }
                int version = reader.ReadInt32();
                if (version != StateVersion)
                {
                    throw new InvalidDataException($"不支持的检查点数据版本: {version}，当前引擎版本为: {StateVersion}。");
                }

                _ = reader.ReadInt32(); // 忽略旧状态，恢复后保持 Idle
                int restoredIndex = reader.ReadInt32();
                DateTime restoredTime = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                _progress = reader.ReadDouble();

                _context.UpdateStep(restoredIndex, restoredTime);
                _resumeFromIndex = restoredIndex >= 0 ? restoredIndex + 1 : -1;
                _status = EngineStatus.Idle;
            }
        }

        #region 二分查找工具 (Binary Search Helpers)

        private static int FindFirstDateIndex(IReadOnlyList<DateTime> dates, DateTime target)
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

        private static int FindLastDateIndex(IReadOnlyList<DateTime> dates, DateTime target)
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
        }
    }
}
