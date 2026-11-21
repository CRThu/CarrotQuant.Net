using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Broker;

/// <summary>
/// 纯内存权威财务账本。
/// 专职负责资金余额、冻结资金、多空持仓、加权平均成本、T+N 日切解锁与资产盯市估值。
/// 零网络、零 I/O、纯算术驱动，回测与实盘 100% 共享单一内核。
/// </summary>
internal sealed class AccountLedger : ICheckpointable<AccountLedgerSnapshot>
{
    private readonly BrokerConfig _config;
    private readonly IFieldRegistry? _fields;
    private readonly Dictionary<string, PositionData> _positions = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, decimal>? _positionsSnapshot;
    private long _positionVersion = 1;

    private double _cash;
    private double _frozenCash;

    public double Cash => _cash;
    public double FrozenCash => _frozenCash;
    public double AvailableCash => _cash - _frozenCash;
    public long PositionVersion => _positionVersion;

    public AccountLedger(BrokerConfig config, IFieldRegistry? fields = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _fields = fields;
        _cash = config.InitialCash;
    }

    /// <summary>
    /// 获取指定标的的当前总持仓数量。
    /// </summary>
    public decimal GetPosition(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return _positions.TryGetValue(symbol, out var pos) ? pos.TotalQuantity : 0m;
    }

    /// <summary>
    /// 获取指定标的的当前可用持仓数量（受 T+N 限制与挂单平仓冻结约束）。
    /// </summary>
    public decimal GetAvailablePosition(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return _positions.TryGetValue(symbol, out var pos) ? pos.AvailableQuantity : 0m;
    }

    /// <summary>
    /// 获取指定标的的加权平均持仓成本价（无持仓时返回 0.0）。
    /// </summary>
    public double GetPositionCost(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return _positions.TryGetValue(symbol, out var pos) ? pos.AverageCost : 0.0;
    }

    /// <summary>
    /// 获取所有活跃非零持仓的只读快照字典。
    /// </summary>
    public IReadOnlyDictionary<string, decimal> GetPositions()
    {
        if (_positionsSnapshot != null)
        {
            return _positionsSnapshot;
        }

        var dict = new Dictionary<string, decimal>(_positions.Count, StringComparer.Ordinal);
        foreach (var kvp in _positions)
        {
            if (kvp.Value.TotalQuantity != 0m)
            {
                dict[kvp.Key] = kvp.Value.TotalQuantity;
            }
        }
        _positionsSnapshot = dict;
        return dict;
    }

    /// <summary>
    /// 获取当前账户的总动态资产权益（现金 + 占用保证金 + 浮动盈亏）。
    /// </summary>
    public double GetTotalEquity(Func<string, double>? priceResolver = null)
    {
        double total = _cash;
        foreach (var pos in _positions.Values)
        {
            if (pos.TotalQuantity == 0m) continue;

            double mult = GetMultiplier(pos.Symbol);
            double marginRate = GetMarginRate(pos.Symbol);
            double resolvedPrice = priceResolver?.Invoke(pos.Symbol) ?? 0.0;
            if (resolvedPrice > 0.0 && !double.IsNaN(resolvedPrice) && !double.IsInfinity(resolvedPrice))
            {
                pos.LastPrice = resolvedPrice;
            }
            double price = resolvedPrice > 0.0 ? resolvedPrice : (pos.LastPrice > 0.0 ? pos.LastPrice : pos.AverageCost);

            double marginOccupied = Math.Abs((double)pos.TotalQuantity) * pos.AverageCost * mult * marginRate;
            double unrealizedPnL = (price - pos.AverageCost) * (double)pos.TotalQuantity * mult;
            total += marginOccupied + unrealizedPnL;
        }
        return total;
    }

    /// <summary>
    /// 获取当前账户持仓所占用的总保证金。
    /// </summary>
    public double GetOccupiedMargin()
    {
        double margin = 0.0;
        foreach (var pos in _positions.Values)
        {
            if (pos.TotalQuantity == 0m) continue;
            double mult = GetMultiplier(pos.Symbol);
            double marginRate = GetMarginRate(pos.Symbol);
            margin += Math.Abs((double)pos.TotalQuantity) * pos.AverageCost * mult * marginRate;
        }
        return margin;
    }

    /// <summary>
    /// 获取当前所有活跃持仓的总未实现收益（浮动盈亏）。
    /// </summary>
    public double GetTotalUnrealizedPnL(Func<string, double>? priceResolver = null)
    {
        double total = 0.0;
        foreach (var pos in _positions.Values)
        {
            if (pos.TotalQuantity == 0m) continue;
            double mult = GetMultiplier(pos.Symbol);
            double resolvedPrice = priceResolver?.Invoke(pos.Symbol) ?? 0.0;
            if (resolvedPrice > 0.0 && !double.IsNaN(resolvedPrice) && !double.IsInfinity(resolvedPrice))
            {
                pos.LastPrice = resolvedPrice;
            }
            double price = resolvedPrice > 0.0 ? resolvedPrice : (pos.LastPrice > 0.0 ? pos.LastPrice : pos.AverageCost);
            double pnl = (price - pos.AverageCost) * (double)pos.TotalQuantity * mult;
            total += pnl;
        }
        return total;
    }

    /// <summary>
    /// 获取当前所有活跃持仓的快照列表。
    /// </summary>
    /// <param name="priceResolver">价格解析委托。</param>
    /// <param name="totalEquity">账户总净值（用于计算持仓权重）。</param>
    /// <returns>持仓快照列表。</returns>
    public IReadOnlyList<PositionSnapshot> GetActivePositionSnapshots(Func<string, double>? priceResolver, double totalEquity)
    {
        var list = new List<PositionSnapshot>();
        foreach (var pos in _positions.Values)
        {
            if (pos.TotalQuantity == 0m) continue;

            double mult = GetMultiplier(pos.Symbol);
            double marginRate = GetMarginRate(pos.Symbol);
            double resolvedPrice = priceResolver?.Invoke(pos.Symbol) ?? 0.0;
            if (resolvedPrice > 0.0 && !double.IsNaN(resolvedPrice) && !double.IsInfinity(resolvedPrice))
            {
                pos.LastPrice = resolvedPrice;
            }
            double price = resolvedPrice > 0.0 ? resolvedPrice : (pos.LastPrice > 0.0 ? pos.LastPrice : pos.AverageCost);

            TradeSide side = pos.TotalQuantity >= 0m ? TradeSide.Long : TradeSide.Short;
            decimal qty = Math.Abs(pos.TotalQuantity);
            decimal availQty = Math.Abs(pos.AvailableQuantity);
            decimal frozenQty = pos.FrozenQuantity;
            double marketValue = (double)qty * price * mult;
            double unrealizedPnL = side == TradeSide.Long
                ? (price - pos.AverageCost) * (double)qty * mult
                : (pos.AverageCost - price) * (double)qty * mult;
            double occupiedMargin = (double)qty * pos.AverageCost * mult * marginRate;
            double weight = totalEquity > 0.0 ? marketValue / totalEquity : 0.0;

            list.Add(new PositionSnapshot(
                pos.Symbol,
                side,
                qty,
                availQty,
                frozenQty,
                pos.AverageCost,
                price,
                mult,
                marketValue,
                unrealizedPnL,
                marginRate,
                occupiedMargin,
                weight));
        }

        return list.Count == 0 ? Array.Empty<PositionSnapshot>() : list;
    }

    /// <summary>
    /// 获取当前所有活跃持仓的总名义市值。
    /// </summary>
    public double GetTotalMarketValue(Func<string, double>? priceResolver = null)
    {
        double total = 0.0;
        foreach (var pos in _positions.Values)
        {
            if (pos.TotalQuantity == 0m) continue;
            double mult = GetMultiplier(pos.Symbol);
            double resolvedPrice = priceResolver?.Invoke(pos.Symbol) ?? 0.0;
            if (resolvedPrice > 0.0 && !double.IsNaN(resolvedPrice) && !double.IsInfinity(resolvedPrice))
            {
                pos.LastPrice = resolvedPrice;
            }
            double price = resolvedPrice > 0.0 ? resolvedPrice : (pos.LastPrice > 0.0 ? pos.LastPrice : pos.AverageCost);
            total += Math.Abs((double)pos.TotalQuantity) * price * mult;
        }
        return total;
    }

    /// <summary>
    /// 预冻结委托所需的资金或持仓。
    /// </summary>
    public bool TryFreeze(
        OrderRequest request,
        double estimatedPrice,
        double multiplier,
        double marginRate,
        out decimal closeQty,
        out decimal openQty,
        out double frozenCash,
        out decimal frozenPosition,
        out string? rejectReason)
    {
        closeQty = 0m;
        openQty = 0m;
        frozenCash = 0.0;
        frozenPosition = 0m;
        rejectReason = null;

        if (request.Quantity <= 0m)
        {
            rejectReason = "Order quantity must be positive.";
            return false;
        }

        var pos = GetOrCreatePosition(request.Symbol);

        // 1. 卖空权限与 T+N 可用持仓校验
        if (request.IsSell)
        {
            if (!_config.AllowShort)
            {
                if (pos.TotalQuantity <= 0m || request.Quantity > pos.AvailableQuantity)
                {
                    rejectReason = $"Insufficient available position. Total={pos.TotalQuantity}, Available={pos.AvailableQuantity}, Request={request.Quantity}";
                    return false;
                }
            }
            else if (pos.PendingLots.Count > 0 && request.Quantity > pos.AvailableQuantity)
            {
                rejectReason = $"Cannot sell unsettled T+N pending lots. Total={pos.TotalQuantity}, Available={pos.AvailableQuantity}, Request={request.Quantity}";
                return false;
            }
        }

        // 2. 双段切分逻辑（支持多翻空/空翻多）
        bool isOpposite = (request.IsBuy && pos.TotalQuantity < 0m) || (!request.IsBuy && pos.TotalQuantity > 0m);
        decimal closableCapacity = 0m;
        if (isOpposite)
        {
            closableCapacity = pos.TotalQuantity > 0m
                ? Math.Max(0m, pos.AvailableQuantity)
                : Math.Max(0m, Math.Abs(pos.TotalQuantity) - pos.FrozenQuantity);
        }

        closeQty = isOpposite ? Math.Min(request.Quantity, closableCapacity) : 0m;
        openQty = request.Quantity - closeQty;

        // 3. 资金可用性校验：纯平仓所需现金为 0（手续费在成交回款中扣除）；新开仓部分需冻结保证金与预估手续费
        double requiredMargin = (double)openQty * estimatedPrice * multiplier * marginRate;
        double estimatedTurnover = (double)openQty * estimatedPrice * multiplier;
        double estimatedFee = openQty > 0m && estimatedPrice > 0.0
            ? FeeCalculator.CalculateFee(estimatedTurnover, request, _config)
            : 0.0;
        double requiredCash = requiredMargin + estimatedFee;

        if (AvailableCash < requiredCash)
        {
            rejectReason = $"Insufficient cash: available={AvailableCash:F2}, required={requiredCash:F2}";
            return false;
        }

        // 4. 执行 Phase 1 预冻结
        _frozenCash += requiredCash;
        frozenCash = requiredCash;

        if (closeQty > 0m)
        {
            if (pos.TotalQuantity > 0m)
            {
                pos.AvailableQuantity -= closeQty;
            }
            else
            {
                // 空头买平挂单冻结：可用持仓朝零方向收敛（如 -5 + 2 = -3）
                pos.AvailableQuantity += closeQty;
            }
            pos.FrozenQuantity += closeQty;
            frozenPosition = closeQty;
        }

        _positionsSnapshot = null;
        return true;
    }

    /// <summary>
    /// 撤单或拒单时原路释放冻结额度。
    /// </summary>
    public void Unfreeze(string symbol, double unfreezeCash, decimal unfreezePosition, bool isBuy)
    {
        if (unfreezeCash > 0.0)
        {
            _frozenCash = Math.Max(0.0, _frozenCash - unfreezeCash);
        }

        if (unfreezePosition > 0m && _positions.TryGetValue(symbol, out var pos))
        {
            pos.FrozenQuantity = Math.Max(0m, pos.FrozenQuantity - unfreezePosition);
            if (pos.TotalQuantity > 0m)
            {
                pos.AvailableQuantity += unfreezePosition;
            }
            else if (pos.TotalQuantity < 0m)
            {
                // 空头持仓以负数记录，解冻平空持仓时反向扣回
                pos.AvailableQuantity -= unfreezePosition;
            }
            // 当 TotalQuantity == 0m 时说明持仓已被平尽，可用持仓保持归零，严禁反向冲减出负持仓
            _positionsSnapshot = null;
        }
    }

    /// <summary>
    /// 订单物理成交落账（Phase 2 Commit）基础重载。
    /// </summary>
    public void CommitFill(
        ExecutionReport report,
        bool isBuy,
        decimal closeQty,
        decimal openQty,
        double multiplier,
        double marginRate,
        int settleDays,
        double unfreezeCash,
        decimal unfreezePosition,
        MarketTime exchangeDate,
        out PositionEvent posEvent)
    {
        CommitFill(
            report, isBuy, closeQty, openQty, multiplier, marginRate, settleDays,
            unfreezeCash, unfreezePosition, exchangeDate, string.Empty,
            out posEvent, out _, out _);
    }

    /// <summary>
    /// 便捷重载：获取平仓结算记录（Trade）。
    /// </summary>
    public void CommitFill(
        ExecutionReport report,
        bool isBuy,
        decimal closeQty,
        decimal openQty,
        double multiplier,
        double marginRate,
        int settleDays,
        double unfreezeCash,
        decimal unfreezePosition,
        MarketTime exchangeDate,
        out PositionEvent posEvent,
        out Trade? closedTrade)
    {
        CommitFill(
            report, isBuy, closeQty, openQty, multiplier, marginRate, settleDays,
            unfreezeCash, unfreezePosition, exchangeDate, string.Empty,
            out posEvent, out closedTrade, out _);
    }

    /// <summary>
    /// 订单物理成交落账（Phase 2 Commit）。
    /// 释放对应比例预冻结额度、扣除实际手续费、正式结转持仓成本并结转已实现盈亏，生成交割单与平仓记录。
    /// </summary>
    public void CommitFill(
        ExecutionReport report,
        bool isBuy,
        decimal closeQty,
        decimal openQty,
        double multiplier,
        double marginRate,
        int settleDays,
        double unfreezeCash,
        decimal unfreezePosition,
        MarketTime exchangeDate,
        string orderTag,
        out PositionEvent posEvent,
        out Trade? closedTrade,
        out SettlementRecord settlement)
    {
        ArgumentNullException.ThrowIfNull(report);

        // 1. 释放对应的预冻结额度
        if (unfreezeCash > 0.0)
        {
            _frozenCash = Math.Max(0.0, _frozenCash - unfreezeCash);
        }

        var pos = GetOrCreatePosition(report.Symbol);
        if (unfreezePosition > 0m)
        {
            pos.FrozenQuantity = Math.Max(0m, pos.FrozenQuantity - unfreezePosition);
        }

        // 2. 扣除实际成交手续费
        _cash -= report.Commission;

        closedTrade = null;
        double closeRealizedPnL = 0.0;

        // 3. 阶段 A：平仓结算（释放保证金并结转已实现盈亏）
        if (closeQty > 0m)
        {
            TradeSide side = pos.TotalQuantity > 0m ? TradeSide.Long : TradeSide.Short;
            double pnlPerUnit = pos.TotalQuantity > 0m
                ? (report.FillPrice - pos.AverageCost)
                : (pos.AverageCost - report.FillPrice);
            double grossPnL = pnlPerUnit * (double)closeQty * multiplier;
            double marginReleased = (double)closeQty * pos.AverageCost * multiplier * marginRate;

            // 分摊开仓在途手续费池与平仓当笔手续费
            decimal currentAbsTotal = Math.Abs(pos.TotalQuantity);
            double allocatedOpenFee = currentAbsTotal > 0m
                ? (double)(closeQty / currentAbsTotal) * pos.OpenCommission
                : 0.0;
            pos.OpenCommission = Math.Max(0.0, pos.OpenCommission - allocatedOpenFee);

            double closeFee = report.FillQuantity > 0m
                ? (double)(closeQty / report.FillQuantity) * report.Commission
                : 0.0;
            closeRealizedPnL = grossPnL - allocatedOpenFee - closeFee;

            _cash += (marginReleased + grossPnL);

            double initialNotional = (double)closeQty * pos.AverageCost * multiplier;
            double returnRate = initialNotional > 0.0 ? closeRealizedPnL / initialNotional : 0.0;
            MarketTime entryTime = pos.OpenTime != MarketTime.MinValue ? pos.OpenTime : report.Time;
            int holdingSteps = Math.Max(1, (int)(report.Time - entryTime).TotalMinutes);

            closedTrade = new Trade(
                report.Symbol,
                side,
                entryTime,
                pos.AverageCost,
                report.Time,
                report.FillPrice,
                closeQty,
                multiplier,
                closeRealizedPnL,
                returnRate,
                allocatedOpenFee + closeFee,
                0.0,
                holdingSteps,
                pos.OpenTag,
                orderTag ?? string.Empty);

            if (pos.TotalQuantity > 0m)
            {
                pos.TotalQuantity -= closeQty;
                // AvailableQuantity 已在冻结时提前扣除，此处无需重复扣减
            }
            else
            {
                pos.TotalQuantity += closeQty;
            }

            if (pos.TotalQuantity == 0m)
            {
                pos.AverageCost = 0.0;
                pos.OpenCommission = 0.0;
                pos.OpenTime = MarketTime.MinValue;
                pos.OpenTag = string.Empty;
                if (openQty == 0m && pos.PendingLots.Count == 0 && pos.FrozenQuantity == 0m)
                {
                    _positions.Remove(report.Symbol);
                }
            }
        }

        // 4. 阶段 B：开仓建仓（扣除保证金，更新加权成本与到期队列）
        if (openQty > 0m)
        {
            double openTurnover = (double)openQty * report.FillPrice * multiplier;
            double marginAdded = openTurnover * marginRate;
            _cash -= marginAdded;

            // 分摊并累加开仓在途手续费池
            double openFee = report.FillQuantity > 0m
                ? (double)(openQty / report.FillQuantity) * report.Commission
                : 0.0;
            pos.OpenCommission += openFee;

            if (pos.TotalQuantity == 0m)
            {
                pos.AverageCost = report.FillPrice;
                pos.TotalQuantity = isBuy ? openQty : -openQty;
                pos.OpenTime = report.Time;
                pos.OpenTag = orderTag ?? string.Empty;

                if (settleDays == 0)
                {
                    pos.AvailableQuantity = pos.TotalQuantity;
                }
                else
                {
                    if (isBuy)
                    {
                        pos.PendingLots.Add(new PendingLot(openQty, exchangeDate.Date.AddDays(settleDays)));
                    }
                    else
                    {
                        pos.AvailableQuantity = -openQty;
                    }
                }
                _positions[report.Symbol] = pos;
            }
            else
            {
                decimal currentAbsQty = Math.Abs(pos.TotalQuantity);
                decimal newAbsQty = currentAbsQty + openQty;
                pos.AverageCost = ((double)currentAbsQty * pos.AverageCost + (double)openQty * report.FillPrice) / (double)newAbsQty;

                if (pos.OpenTime == MarketTime.MinValue)
                {
                    pos.OpenTime = report.Time;
                    pos.OpenTag = orderTag ?? string.Empty;
                }

                if (isBuy)
                {
                    pos.TotalQuantity += openQty;
                    if (settleDays == 0)
                    {
                        pos.AvailableQuantity += openQty;
                    }
                    else
                    {
                        pos.PendingLots.Add(new PendingLot(openQty, exchangeDate.Date.AddDays(settleDays)));
                    }
                }
                else
                {
                    pos.TotalQuantity -= openQty;
                    pos.AvailableQuantity -= openQty;
                }
            }
        }

        pos.LastPrice = report.FillPrice;
        _positionsSnapshot = null;
        _positionVersion++;

        posEvent = new PositionEvent(report.Symbol, pos.TotalQuantity, pos.AverageCost, report.Time);

        // 5. 生成交割单流水
        double turnover = (double)report.FillQuantity * report.FillPrice * multiplier;
        settlement = new SettlementRecord(
            report.Time,
            report.Symbol,
            report.OrderId,
            isBuy ? OrderDirection.Buy : OrderDirection.Sell,
            report.FillPrice,
            report.FillQuantity,
            turnover,
            report.Commission,
            closeRealizedPnL,
            _cash,
            orderTag ?? string.Empty);
    }

    /// <summary>
    /// 推进交易所本地日切清算：将已到期的 T+N 在途持仓划转为可用持仓。
    /// </summary>
    public void ProcessDayRollover(MarketTime exchangeDate)
    {
        MarketTime targetDate = exchangeDate.Date;
        foreach (var pos in _positions.Values)
        {
            if (pos.PendingLots.Count == 0) continue;

            decimal unlocked = 0m;
            for (int i = pos.PendingLots.Count - 1; i >= 0; i--)
            {
                var lot = pos.PendingLots[i];
                if (targetDate >= lot.AvailableDate)
                {
                    unlocked += lot.Quantity;
                    pos.PendingLots.RemoveAt(i);
                }
            }

            if (unlocked > 0m)
            {
                pos.AvailableQuantity += unlocked;
                _positionsSnapshot = null;
                _positionVersion++;
            }
        }
    }

    /// <summary>
    /// 外部权威对账状态同步（实盘断线重连或强制对账）。
    /// </summary>
    public void SyncState(double cash, IReadOnlyDictionary<string, decimal> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        _cash = cash;
        _frozenCash = 0.0;
        _positions.Clear();
        _positionsSnapshot = null;
        _positionVersion++;

        foreach (var kvp in positions)
        {
            if (kvp.Value != 0m)
            {
                _positions[kvp.Key] = new PositionData(kvp.Key)
                {
                    TotalQuantity = kvp.Value,
                    AvailableQuantity = kvp.Value
                };
            }
        }
    }

    /// <summary>
    /// 保存账本不可变状态快照（支持检查点持久化）。
    /// </summary>
    public AccountLedgerSnapshot SaveState()
    {
        var positionsList = new List<LedgerPositionSnapshot>(_positions.Count);
        foreach (var pos in _positions.Values)
        {
            var lots = new List<PendingLotSnapshot>(pos.PendingLots.Count);
            foreach (var lot in pos.PendingLots)
            {
                lots.Add(new PendingLotSnapshot(lot.Quantity, lot.AvailableDate));
            }

            positionsList.Add(new LedgerPositionSnapshot(
                pos.Symbol,
                pos.TotalQuantity,
                pos.AvailableQuantity,
                pos.FrozenQuantity,
                pos.AverageCost,
                pos.OpenCommission,
                pos.LastPrice,
                pos.OpenTime,
                pos.OpenTag,
                lots
            ));
        }

        return new AccountLedgerSnapshot(_cash, _frozenCash, positionsList);
    }

    /// <summary>
    /// 基于不可变状态快照精确恢复账本（支持断点续跑）。
    /// </summary>
    public void RestoreState(AccountLedgerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _cash = snapshot.Cash;
        _frozenCash = snapshot.FrozenCash;
        _positions.Clear();
        _positionsSnapshot = null;
        _positionVersion++;

        foreach (var snap in snapshot.Positions)
        {
            var pos = new PositionData(snap.Symbol)
            {
                TotalQuantity = snap.TotalQuantity,
                AvailableQuantity = snap.AvailableQuantity,
                FrozenQuantity = snap.FrozenQuantity,
                AverageCost = snap.AverageCost,
                OpenCommission = snap.OpenCommission,
                LastPrice = snap.LastPrice,
                OpenTime = snap.OpenTime,
                OpenTag = snap.OpenTag
            };

            foreach (var lot in snap.PendingLots)
            {
                pos.PendingLots.Add(new PendingLot(lot.Quantity, lot.AvailableDate));
            }

            _positions[snap.Symbol] = pos;
        }
    }

    private PositionData GetOrCreatePosition(string symbol)
    {
        if (!_positions.TryGetValue(symbol, out var pos))
        {
            pos = new PositionData(symbol);
            _positions[symbol] = pos;
        }
        return pos;
    }

    private double GetMultiplier(string symbol)
    {
        if (_config.OnGetMultiplier != null)
        {
            return _config.OnGetMultiplier(symbol, _fields);
        }
        return _config.Multiplier;
    }

    private double GetMarginRate(string symbol)
    {
        if (_config.OnGetMarginRate != null)
        {
            return _config.OnGetMarginRate(symbol, _fields);
        }
        return _config.MarginRate;
    }
}
