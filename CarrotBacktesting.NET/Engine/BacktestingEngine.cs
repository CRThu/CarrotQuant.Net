using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine.Broker;
using CarrotBacktesting.NET.Engine.Exchange;
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
        private readonly ISimulatedExchange? _simExchange;
        private readonly IStrategyPipeline _pipeline;
        private readonly IEventRegistry _events;
        private readonly IFieldRegistry _fields;
        private readonly IMarketState _market;
        private readonly EngineConfig _config;
        private readonly IMonitor _monitor;

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
        private readonly Action<PositionEvent> _positionHandler;
        private bool _isSubscribed = false;

        /// <inheritdoc />
        public EngineStatus Status => _status;

        /// <inheritdoc />
        public double Progress => _progress;

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
        public BacktestingEngine(
            ITimeseriesRegistry timeseries,
            IBroker broker,
            IStrategyPipeline pipeline,
            IEventRegistry? events = null,
            IFieldRegistry? fields = null,
            IMarketState? market = null,
            EngineConfig? config = null,
            IMonitor? monitor = null,
            ISimulatedExchange? simExchange = null)
        {
            _timeseries = timeseries ?? throw new ArgumentNullException(nameof(timeseries));
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));
            _simExchange = simExchange ?? ((broker as Broker.Broker)?.Gateway as ISimulatedExchange);
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _config = config ?? new EngineConfig();
            _monitor = monitor ?? NullMonitor.Instance;

            _context = new EngineContext(timeseries, broker, market, events, fields);
            _events = _context.Events;
            _fields = _context.Fields;
            _market = _context.Market;

            _orderStatusHandler = (orderId, status) => _pipeline.OnOrderStatusChanged(_context, orderId, status);
            _tradeHandler = (report) => _pipeline.OnTrade(_context, report);
            _positionHandler = (posEvent) => _pipeline.OnPositionChanged(_context, posEvent);
        }

        /// <summary>
        /// 便捷静态工厂：根据整体会话配置与数据供给一键装配回测引擎。
        /// </summary>
        /// <param name="sessionConfig">会话级总配置。</param>
        /// <param name="timeseries">时序行情供给源。</param>
        /// <param name="pipeline">外部构建或传入的策略管线（若为空则依据 sessionConfig.Pipeline 动态组装）。</param>
        /// <param name="events">事件注册表（可选）。</param>
        /// <param name="fields">维表注册表（可选）。</param>
        /// <param name="market">市场黑板（可选）。</param>
        /// <param name="monitor">监控接口（可选）。</param>
        /// <returns>已装配就绪的 BacktestingEngine 实例。</returns>
        public static BacktestingEngine Create(
            SessionConfig sessionConfig,
            ITimeseriesRegistry timeseries,
            IStrategyPipeline? pipeline = null,
            IEventRegistry? events = null,
            IFieldRegistry? fields = null,
            IMarketState? market = null,
            IMonitor? monitor = null)
        {
            ArgumentNullException.ThrowIfNull(sessionConfig);
            ArgumentNullException.ThrowIfNull(timeseries);

            var simGateway = new SimulatedExchangeGateway(sessionConfig.Broker, fields);
            var broker = new Broker.Broker(simGateway, sessionConfig.Broker, fields);

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
                timeseries,
                broker,
                activePipeline,
                events,
                fields,
                market,
                sessionConfig.Engine,
                monitor,
                simExchange: simGateway);
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

                // 3. 触发策略启动生命周期
                _pipeline.OnStart(_context);

                // 4. 获取时间坐标系与区间裁剪
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

                int totalSteps = endIndex - startIndex + 1;
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

                    // 5.5 驱动策略主决策计算
                    _pipeline.OnUpdate(_context);

                    // 5.6 进度刷新
                    _progress = (double)(i - startIndex + 1) / totalSteps;
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

        private void SubscribeBrokerEvents()
        {
            if (!_isSubscribed)
            {
                _broker.OnOrderStatusChanged += _orderStatusHandler;
                _broker.OnTrade += _tradeHandler;
                _broker.OnPositionChanged += _positionHandler;
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
