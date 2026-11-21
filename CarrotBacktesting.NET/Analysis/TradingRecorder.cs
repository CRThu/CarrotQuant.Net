using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Analysis;

/// <summary>
/// 交易事实记录器，负责接收运行期的交割单、平仓结算、净值点、每日快照与策略信号，生成交易账本结果。
/// 实现 ITradingRecorder 与 ICheckpointable&lt;TradingRecorderSnapshot&gt;，回测与实盘通用。
/// </summary>
public sealed class TradingRecorder : ITradingRecorder, ICheckpointable<TradingRecorderSnapshot>
{
    private readonly object _syncLock = new();
    private readonly List<SettlementRecord> _settlements;
    private readonly List<Trade> _closedTrades;
    private readonly List<EquityPoint> _equityCurve;
    private readonly List<DailySnapshot> _dailySnapshots;
    private readonly List<SignalRecord> _signals;
    private readonly List<OrderEvent> _orderEvents;

    /// <summary>
    /// 初始化记录器，支持预分配容器容量。
    /// </summary>
    /// <param name="initialStepCapacity">预估时间步容量（默认 65536）。</param>
    public TradingRecorder(int initialStepCapacity = 65536)
    {
        _settlements = new List<SettlementRecord>(1024);
        _closedTrades = new List<Trade>(1024);
        _equityCurve = new List<EquityPoint>(initialStepCapacity);
        _dailySnapshots = new List<DailySnapshot>(initialStepCapacity / 240 + 64);
        _signals = new List<SignalRecord>(1024);
        _orderEvents = new List<OrderEvent>(1024);
    }

    /// <inheritdoc />
    public void Record(SettlementRecord settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        lock (_syncLock)
        {
            _settlements.Add(settlement);
        }
    }

    /// <inheritdoc />
    public void Record(Trade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        lock (_syncLock)
        {
            _closedTrades.Add(trade);
        }
    }

    /// <inheritdoc />
    public void Record(EquityPoint point)
    {
        lock (_syncLock)
        {
            _equityCurve.Add(point);
        }
    }

    /// <inheritdoc />
    public void Record(DailySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_syncLock)
        {
            _dailySnapshots.Add(snapshot);
        }
    }

    /// <inheritdoc />
    public void Record(SignalRecord signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        lock (_syncLock)
        {
            _signals.Add(signal);
        }
    }

    /// <inheritdoc />
    public void Record(OrderEvent orderEvent)
    {
        ArgumentNullException.ThrowIfNull(orderEvent);
        lock (_syncLock)
        {
            _orderEvents.Add(orderEvent);
        }
    }

    /// <inheritdoc />
    public TradingResult GetResult(IReadOnlyList<PositionSnapshot> endingPositions)
    {
        lock (_syncLock)
        {
            return new TradingResult(
                _settlements.ToArray(),
                _closedTrades.ToArray(),
                endingPositions ?? Array.Empty<PositionSnapshot>(),
                _equityCurve.ToArray(),
                _dailySnapshots.ToArray(),
                _signals.ToArray(),
                _orderEvents.ToArray());
        }
    }

    /// <summary>
    /// 重置所有沉淀数据。
    /// </summary>
    public void Reset()
    {
        lock (_syncLock)
        {
            _settlements.Clear();
            _closedTrades.Clear();
            _equityCurve.Clear();
            _dailySnapshots.Clear();
            _signals.Clear();
            _orderEvents.Clear();
        }
    }

    /// <summary>
    /// 保存交易事实记录器不可变状态快照。
    /// </summary>
    public TradingRecorderSnapshot SaveState()
    {
        lock (_syncLock)
        {
            return new TradingRecorderSnapshot(
                _settlements.ToArray(),
                _closedTrades.ToArray(),
                _equityCurve.ToArray(),
                _dailySnapshots.ToArray(),
                _signals.ToArray(),
                _orderEvents.ToArray()
            );
        }
    }

    /// <summary>
    /// 恢复交易事实记录器不可变状态快照。
    /// </summary>
    public void RestoreState(TradingRecorderSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_syncLock)
        {
            _settlements.Clear();
            if (state.Settlements != null) _settlements.AddRange(state.Settlements);

            _closedTrades.Clear();
            if (state.ClosedTrades != null) _closedTrades.AddRange(state.ClosedTrades);

            _equityCurve.Clear();
            if (state.EquityCurve != null) _equityCurve.AddRange(state.EquityCurve);

            _dailySnapshots.Clear();
            if (state.DailySnapshots != null) _dailySnapshots.AddRange(state.DailySnapshots);

            _signals.Clear();
            if (state.Signals != null) _signals.AddRange(state.Signals);

            _orderEvents.Clear();
            if (state.OrderEvents != null) _orderEvents.AddRange(state.OrderEvents);
        }
    }
}
