using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Broker;

/// <summary>
/// 纯内存权威财务账本。
/// 专职负责资金余额、冻结资金、多空持仓、加权平均成本、T+N 日切解锁与资产盯市估值。
/// 零网络、零 I/O、纯算术驱动，回测与实盘 100% 共享单一内核。
/// </summary>
internal sealed class AccountLedger
{
    private readonly BrokerConfig _config;
    private readonly IFieldRegistry? _fields;
    private readonly Dictionary<string, PositionData> _positions = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, decimal>? _positionsSnapshot;

    private double _cash;
    private double _frozenCash;

    public double Cash => _cash;
    public double FrozenCash => _frozenCash;
    public double AvailableCash => _cash - _frozenCash;

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
            double price = resolvedPrice > 0.0 ? resolvedPrice : (pos.LastPrice > 0.0 ? pos.LastPrice : pos.AverageCost);

            double marginOccupied = Math.Abs((double)pos.TotalQuantity) * pos.AverageCost * mult * marginRate;
            double unrealizedPnL = (price - pos.AverageCost) * (double)pos.TotalQuantity * mult;
            total += marginOccupied + unrealizedPnL;
        }
        return total;
    }

    /// <summary>
    /// 挂单前两阶段原子预冻结（Phase 1）。
    /// 判定持仓可用度、执行反向翻仓双段切分，预冻结资金与持仓。
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
        double estimatedFee = openQty > 0m && estimatedPrice > 0.0
            ? Math.Max(_config.MinFee, (double)openQty * estimatedPrice * multiplier * _config.FeeRate)
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
            else
            {
                pos.AvailableQuantity -= unfreezePosition;
            }
            _positionsSnapshot = null;
        }
    }

    /// <summary>
    /// 订单物理成交落账（Phase 2 Commit）。
    /// 释放对应比例预冻结额度、扣除实际手续费、正式结转持仓成本并结转已实现盈亏。
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
        DateTime exchangeDate,
        out PositionEvent posEvent)
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

        // 3. 阶段 A：平仓结算（释放保证金并结转已实现盈亏）
        if (closeQty > 0m)
        {
            double pnlPerUnit = pos.TotalQuantity > 0m
                ? (report.FillPrice - pos.AverageCost)
                : (pos.AverageCost - report.FillPrice);
            double realizedPnL = pnlPerUnit * (double)closeQty * multiplier;
            double marginReleased = (double)closeQty * pos.AverageCost * multiplier * marginRate;

            _cash += (marginReleased + realizedPnL);

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

            if (pos.TotalQuantity == 0m)
            {
                pos.AverageCost = report.FillPrice;
                pos.TotalQuantity = isBuy ? openQty : -openQty;

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

        posEvent = new PositionEvent(report.Symbol, pos.TotalQuantity, pos.AverageCost, report.Time);
    }

    /// <summary>
    /// 推进交易所本地日切清算：将已到期的 T+N 在途持仓划转为可用持仓。
    /// </summary>
    public void ProcessDayRollover(DateTime exchangeDate)
    {
        DateTime targetDate = exchangeDate.Date;
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
