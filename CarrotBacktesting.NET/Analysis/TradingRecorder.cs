using System;
using System.Collections.Generic;
using System.IO;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Analysis;

/// <summary>
/// 交易事实记录器，负责接收运行期的交割单、平仓结算、净值点、每日快照与策略信号，生成交易账本结果。
/// 实现 ITradingRecorder 与 ICheckpointable，回测与实盘通用。
/// </summary>
public sealed class TradingRecorder : ITradingRecorder, ICheckpointable, ICheckpointable<TradingRecorderSnapshot>
{
    private const uint StateMagic = 0x54524452; // "TRDR"
    private const int StateVersion = 2;

    private readonly object _syncLock = new();
    private readonly List<SettlementRecord> _settlements;
    private readonly List<Trade> _closedTrades;
    private readonly List<EquityPoint> _equityCurve;
    private readonly List<DailySnapshot> _dailySnapshots;
    private readonly List<SignalRecord> _signals;

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
    }

    /// <inheritdoc />
    public void RecordSettlement(SettlementRecord settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        lock (_syncLock)
        {
            _settlements.Add(settlement);
        }
    }

    /// <inheritdoc />
    public void RecordClosedTrade(Trade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        lock (_syncLock)
        {
            _closedTrades.Add(trade);
        }
    }

    /// <inheritdoc />
    public void RecordStep(EquityPoint point)
    {
        lock (_syncLock)
        {
            _equityCurve.Add(point);
        }
    }

    /// <inheritdoc />
    public void RecordDailySnapshot(DailySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_syncLock)
        {
            _dailySnapshots.Add(snapshot);
        }
    }

    /// <inheritdoc />
    public void RecordSignal(SignalRecord signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        lock (_syncLock)
        {
            _signals.Add(signal);
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
                _signals.ToArray());
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
        }
    }

    /// <inheritdoc />
    public byte[] SaveCheckpoint()
    {
        lock (_syncLock)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(StateMagic);
            writer.Write(StateVersion);

            // 1. 交割单流水
            writer.Write(_settlements.Count);
            for (int i = 0; i < _settlements.Count; i++)
            {
                var s = _settlements[i];
                writer.Write(s.Time.Ticks);
                writer.Write(s.Symbol);
                writer.Write(s.OrderId);
                writer.Write((byte)s.Direction);
                writer.Write(s.Price);
                writer.Write((double)s.Quantity);
                writer.Write(s.Turnover);
                writer.Write(s.Commission);
                writer.Write(s.RealizedPnL);
                writer.Write(s.BalanceCash);
                writer.Write(s.OrderTag);
            }

            // 2. 已平仓交易
            writer.Write(_closedTrades.Count);
            for (int i = 0; i < _closedTrades.Count; i++)
            {
                var t = _closedTrades[i];
                writer.Write(t.Symbol);
                writer.Write((byte)t.Side);
                writer.Write(t.EntryTime.Ticks);
                writer.Write(t.EntryPrice);
                writer.Write(t.ExitTime.Ticks);
                writer.Write(t.ExitPrice);
                writer.Write((double)t.Quantity);
                writer.Write(t.Multiplier);
                writer.Write(t.RealizedPnL);
                writer.Write(t.ReturnRate);
                writer.Write(t.Commission);
                writer.Write(t.SlippageCost);
                writer.Write(t.HoldingSteps);
                writer.Write(t.EntryTag);
                writer.Write(t.ExitTag);
            }

            // 3. 净值曲线
            writer.Write(_equityCurve.Count);
            for (int i = 0; i < _equityCurve.Count; i++)
            {
                var pt = _equityCurve[i];
                writer.Write(pt.Time.Ticks);
                writer.Write(pt.TotalEquity);
                writer.Write(pt.AvailableCash);
                writer.Write(pt.FrozenCash);
                writer.Write(pt.OccupiedMargin);
                writer.Write(pt.TotalMarketValue);
                writer.Write(pt.UnrealizedPnL);
            }

            // 4. 每日快照
            writer.Write(_dailySnapshots.Count);
            for (int i = 0; i < _dailySnapshots.Count; i++)
            {
                var snap = _dailySnapshots[i];
                writer.Write(snap.Time.Ticks);
                writer.Write(snap.TotalEquity);
                writer.Write(snap.AvailableCash);
                writer.Write(snap.FrozenCash);
                writer.Write(snap.OccupiedMargin);
                writer.Write(snap.TotalMarketValue);
                writer.Write(snap.UnrealizedPnL);

                writer.Write(snap.Positions.Count);
                for (int p = 0; p < snap.Positions.Count; p++)
                {
                    var pos = snap.Positions[p];
                    writer.Write(pos.Symbol);
                    writer.Write((byte)pos.Side);
                    writer.Write((double)pos.Quantity);
                    writer.Write((double)pos.AvailableQuantity);
                    writer.Write((double)pos.FrozenQuantity);
                    writer.Write(pos.CostPrice);
                    writer.Write(pos.CurrentPrice);
                    writer.Write(pos.Multiplier);
                    writer.Write(pos.MarketValue);
                    writer.Write(pos.UnrealizedPnL);
                    writer.Write(pos.MarginRate);
                    writer.Write(pos.OccupiedMargin);
                    writer.Write(pos.Weight);
                }
            }

            // 5. 信号打点
            writer.Write(_signals.Count);
            for (int i = 0; i < _signals.Count; i++)
            {
                var sig = _signals[i];
                writer.Write(sig.Time.Ticks);
                writer.Write(sig.Symbol);
                writer.Write(sig.SignalValue);
                writer.Write(sig.SignalGroup);
                writer.Write(sig.Reason);
            }

            return ms.ToArray();
        }
    }

    /// <inheritdoc />
    public void RestoreCheckpoint(byte[] checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.Length < 8)
        {
            throw new ArgumentException($"恢复检查点数据字节长度不足，预期至少 8 字节，实际为 {checkpoint.Length} 字节。", nameof(checkpoint));
        }

        lock (_syncLock)
        {
            using var ms = new MemoryStream(checkpoint);
            using var reader = new BinaryReader(ms);

            uint magic = reader.ReadUInt32();
            if (magic != StateMagic)
            {
                throw new InvalidDataException($"无效的检查点魔数: 0x{magic:X8}，预期 0x{StateMagic:X8}。");
            }
            int version = reader.ReadInt32();
            if (version != StateVersion)
            {
                throw new InvalidDataException($"不支持的检查点版本: {version}，当前版本为 {StateVersion}。");
            }

            // 1. 交割单流水
            _settlements.Clear();
            int stCount = reader.ReadInt32();
            for (int i = 0; i < stCount; i++)
            {
                DateTime time = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                string symbol = reader.ReadString();
                string orderId = reader.ReadString();
                OrderDirection dir = (OrderDirection)reader.ReadByte();
                double price = reader.ReadDouble();
                decimal quantity = (decimal)reader.ReadDouble();
                double turnover = reader.ReadDouble();
                double commission = reader.ReadDouble();
                double realizedPnL = reader.ReadDouble();
                double balanceCash = reader.ReadDouble();
                string orderTag = reader.ReadString();

                _settlements.Add(new SettlementRecord(
                    time, symbol, orderId, dir, price, quantity, turnover,
                    commission, realizedPnL, balanceCash, orderTag));
            }

            // 2. 已平仓交易
            _closedTrades.Clear();
            int tradeCount = reader.ReadInt32();
            for (int i = 0; i < tradeCount; i++)
            {
                string symbol = reader.ReadString();
                TradeSide side = (TradeSide)reader.ReadByte();
                DateTime entryTime = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                double entryPrice = reader.ReadDouble();
                DateTime exitTime = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                double exitPrice = reader.ReadDouble();
                decimal quantity = (decimal)reader.ReadDouble();
                double multiplier = reader.ReadDouble();
                double realizedPnL = reader.ReadDouble();
                double returnRate = reader.ReadDouble();
                double commission = reader.ReadDouble();
                double slippageCost = reader.ReadDouble();
                int holdingSteps = reader.ReadInt32();
                string entryTag = reader.ReadString();
                string exitTag = reader.ReadString();

                _closedTrades.Add(new Trade(
                    symbol, side, entryTime, entryPrice, exitTime, exitPrice,
                    quantity, multiplier, realizedPnL, returnRate, commission,
                    slippageCost, holdingSteps, entryTag, exitTag));
            }

            // 3. 净值曲线
            _equityCurve.Clear();
            int eqCount = reader.ReadInt32();
            for (int i = 0; i < eqCount; i++)
            {
                DateTime time = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                double totalEquity = reader.ReadDouble();
                double availableCash = reader.ReadDouble();
                double frozenCash = reader.ReadDouble();
                double occupiedMargin = reader.ReadDouble();
                double totalMarketValue = reader.ReadDouble();
                double unrealizedPnL = reader.ReadDouble();

                _equityCurve.Add(new EquityPoint(
                    time, totalEquity, availableCash, frozenCash, occupiedMargin,
                    totalMarketValue, unrealizedPnL));
            }

            // 4. 每日快照
            _dailySnapshots.Clear();
            int snapCount = reader.ReadInt32();
            for (int i = 0; i < snapCount; i++)
            {
                DateTime time = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                double totalEquity = reader.ReadDouble();
                double availableCash = reader.ReadDouble();
                double frozenCash = reader.ReadDouble();
                double occupiedMargin = reader.ReadDouble();
                double totalMarketValue = reader.ReadDouble();
                double unrealizedPnL = reader.ReadDouble();

                int posCount = reader.ReadInt32();
                var positions = new List<PositionSnapshot>(posCount);
                for (int p = 0; p < posCount; p++)
                {
                    string symbol = reader.ReadString();
                    TradeSide side = (TradeSide)reader.ReadByte();
                    decimal quantity = (decimal)reader.ReadDouble();
                    decimal availableQty = (decimal)reader.ReadDouble();
                    decimal frozenQty = (decimal)reader.ReadDouble();
                    double costPrice = reader.ReadDouble();
                    double currentPrice = reader.ReadDouble();
                    double multiplier = reader.ReadDouble();
                    double marketValue = reader.ReadDouble();
                    double posUnrealizedPnL = reader.ReadDouble();
                    double marginRate = reader.ReadDouble();
                    double posOccupiedMargin = reader.ReadDouble();
                    double weight = reader.ReadDouble();

                    positions.Add(new PositionSnapshot(
                        symbol, side, quantity, availableQty, frozenQty, costPrice,
                        currentPrice, multiplier, marketValue, posUnrealizedPnL,
                        marginRate, posOccupiedMargin, weight));
                }

                _dailySnapshots.Add(new DailySnapshot(
                    time, totalEquity, availableCash, frozenCash, occupiedMargin,
                    totalMarketValue, unrealizedPnL, positions));
            }

            // 5. 信号打点
            _signals.Clear();
            int sigCount = reader.ReadInt32();
            for (int i = 0; i < sigCount; i++)
            {
                DateTime time = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                string symbol = reader.ReadString();
                double sigVal = reader.ReadDouble();
                string sigGroup = reader.ReadString();
                string reason = reader.ReadString();
                _signals.Add(new SignalRecord(time, symbol, sigVal, sigGroup, reason));
            }
        }
    }

    /// <summary>
    /// 获取当前已记录的所有已平仓交易不可变快照。
    /// </summary>
    public IReadOnlyList<Trade> GetClosedTradesSnapshot()
    {
        lock (_syncLock)
        {
            return _closedTrades.ToArray();
        }
    }

    /// <summary>
    /// 恢复历史已平仓交易列表。
    /// </summary>
    public void RestoreClosedTrades(IEnumerable<Trade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        lock (_syncLock)
        {
            _closedTrades.Clear();
            _closedTrades.AddRange(trades);
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
                _signals.ToArray()
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
        }
    }
}
