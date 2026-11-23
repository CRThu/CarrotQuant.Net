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
    private MarketTime _lastRolloverDate = MarketTime.MinValue;

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
    /// 获取指定标的的最新盯市基准价（若无持仓或未记录有效价格则返回 double.NaN）。
    /// </summary>
    public double GetLastPrice(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return _positions.TryGetValue(symbol, out var pos) && pos.LastPrice > 0.0 && !double.IsNaN(pos.LastPrice) && !double.IsInfinity(pos.LastPrice)
            ? pos.LastPrice
            : double.NaN;
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
    /// 解析持仓在当前时间步的有效公允估值价格（单一事实来源 SSOT）。
    /// 遵循最新有效公允价值连续性原则：
    /// 1. 外部 priceResolver 解析出有效市价（> 0 且非 NaN/Infinity）：使用该价格并同步更新 pos.LastPrice；
    /// 2. 外部行情缺失：平滑回退至上一有效收盘价/成交价（pos.LastPrice），保持市值平稳守恒；
    /// 3. 若外部行情缺失且历史上从未产生过有效 LastPrice：坚决杜绝假兜底，返回 double.NaN。
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double ResolvePositionPrice(PositionData pos, Func<string, double>? priceResolver)
    {
        double resolvedPrice = priceResolver?.Invoke(pos.Symbol) ?? double.NaN;
        if (resolvedPrice > 0.0 && !double.IsNaN(resolvedPrice) && !double.IsInfinity(resolvedPrice))
        {
            pos.LastPrice = resolvedPrice;
            return resolvedPrice;
        }

        if (pos.LastPrice > 0.0 && !double.IsNaN(pos.LastPrice) && !double.IsInfinity(pos.LastPrice))
        {
            return pos.LastPrice;
        }

        return double.NaN;
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
            double price = ResolvePositionPrice(pos, priceResolver);

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
            double price = ResolvePositionPrice(pos, priceResolver);
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
            double price = ResolvePositionPrice(pos, priceResolver);

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
            double price = ResolvePositionPrice(pos, priceResolver);
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

        // =========================================================================
        // A. ReduceOnly（仅减仓 / 纯平仓模式，对应 ClosePosition / StopLoss / TakeProfit）
        // 核心契约：该订单物理上只能减少现有持仓绝对值 (|Total| 向 0 收敛)，严禁产生任何反向新持仓！
        // =========================================================================
        if (request.ReduceOnly)
        {
            if (pos.TotalQuantity == 0m)
            {
                rejectReason = $"Cannot submit ReduceOnly order for symbol '{request.Symbol}': no existing position to reduce.";
                return false;
            }

            // 方向必须与持仓严格相反（平仓反向）
            bool isCorrectDirection = (pos.TotalQuantity > 0m && request.IsSell) || (pos.TotalQuantity < 0m && request.IsBuy);
            if (!isCorrectDirection)
            {
                rejectReason = $"ReduceOnly order direction must be opposite to existing position for '{request.Symbol}'. Position={pos.TotalQuantity}, OrderDirection={request.Direction}.";
                return false;
            }

            // 计算当前方向的在途可用平仓额度
            decimal availableToClose = pos.TotalQuantity > 0m
                ? pos.AvailableQuantity
                : Math.Max(0m, Math.Abs(pos.TotalQuantity) - pos.FrozenQuantity);

            if (request.Quantity > availableToClose)
            {
                rejectReason = $"Insufficient available position for ReduceOnly order. Total={Math.Abs(pos.TotalQuantity)}, Available={availableToClose}, Request={request.Quantity}.";
                return false;
            }

            closeQty = request.Quantity;
            openQty = 0m;
        }
        else
        {
            // =========================================================================
            // B. 常规自由交易模式（ReduceOnly == false，完全镜像对称，自由开仓、平仓与反手翻仓）
            // =========================================================================
            if (pos.TotalQuantity == 0m)
            {
                // 空仓开仓
                if (request.IsSell && !_config.AllowShort)
                {
                    rejectReason = $"Cannot sell without existing position when short selling is disabled for '{request.Symbol}'.";
                    return false;
                }
                closeQty = 0m;
                openQty = request.Quantity;
            }
            else if ((pos.TotalQuantity > 0m && request.IsBuy) || (pos.TotalQuantity < 0m && request.IsSell))
            {
                // 同向加仓（多头买入，或空头卖出）
                closeQty = 0m;
                openQty = request.Quantity;
            }
            else
            {
                // 反向交易（多头卖出，或空头买入）
                decimal currentAbsTotal = Math.Abs(pos.TotalQuantity);
                decimal availableToClose = pos.TotalQuantity > 0m
                    ? pos.AvailableQuantity
                    : Math.Max(0m, currentAbsTotal - pos.FrozenQuantity);

                if (request.Quantity <= currentAbsTotal)
                {
                    // 意图：纯平仓（部分平仓或全额平仓）
                    if (request.Quantity > availableToClose)
                    {
                        rejectReason = $"Insufficient available position to close. Total={currentAbsTotal}, Available={availableToClose}, Request={request.Quantity}";
                        return false;
                    }
                    closeQty = request.Quantity;
                    openQty = 0m;
                }
                else
                {
                    // 意图：反手翻仓（多翻空 或 空翻多）
                    if (pos.TotalQuantity > 0m && !_config.AllowShort)
                    {
                        rejectReason = $"Insufficient position to sell and short selling is disabled. Total={currentAbsTotal}, Request={request.Quantity}";
                        return false;
                    }

                    // 翻仓前提：现有持仓必须全部处于可用状态（未被挂单冻结且无 T+N 在途锁定）
                    if (availableToClose < currentAbsTotal)
                    {
                        rejectReason = $"Cannot flip position while existing position is partially frozen or unsettled. Total={currentAbsTotal}, Available={availableToClose}";
                        return false;
                    }

                    closeQty = currentAbsTotal;
                    openQty = request.Quantity - currentAbsTotal;
                }
            }
        }

        // 3. 资金可用性校验：
        // 新开仓部分需计算基础保证金与预估手续费需求
        if (openQty > 0m && (estimatedPrice <= 0.0 || double.IsNaN(estimatedPrice) || double.IsInfinity(estimatedPrice)))
        {
            rejectReason = $"Cannot determine valid reference price for '{request.Symbol}' to estimate margin for opening position.";
            return false;
        }

        double requiredMargin = (double)openQty * estimatedPrice * multiplier * marginRate;
        double estimatedTurnover = (double)openQty * estimatedPrice * multiplier;
        double estimatedFee = openQty > 0m && estimatedPrice > 0.0
            ? FeeCalculator.CalculateFee(estimatedTurnover, request, _config)
            : 0.0;
        double requiredCash = requiredMargin + estimatedFee;

        // 翻仓场景信用对冲（First Principles）：
        // 当订单同时包含平仓 (closeQty > 0) 与反向开仓 (openQty > 0) 时，
        // 平仓成交将在落账瞬间释放持仓保证金与平仓货款 (marginReleased + grossPnL - closeFee)，
        // 此部分释放金额可作为反向开仓的信用缓冲，抵扣新开仓的现金预冻结需求。
        double netRequiredCash = requiredCash;
        if (closeQty > 0m && openQty > 0m)
        {
            // 平仓预计释放的持仓保证金
            double marginReleased = (double)closeQty * pos.AverageCost * multiplier * marginRate;

            // 平仓预计实现的毛盈亏
            double grossPnL = pos.TotalQuantity > 0m
                ? (estimatedPrice - pos.AverageCost) * (double)closeQty * multiplier
                : (pos.AverageCost - estimatedPrice) * (double)closeQty * multiplier;

            // 平仓预计手续费
            double closeTurnover = (double)closeQty * estimatedPrice * multiplier;
            double closeFee = FeeCalculator.CalculateFee(closeTurnover, request, _config);

            // 平仓预计净现金流入（正值抵扣新仓需求，负值追加亏损现金准备）
            double estimatedCloseCashInflow = marginReleased + grossPnL - closeFee;

            netRequiredCash = Math.Max(0.0, requiredCash - estimatedCloseCashInflow);
        }

        if (AvailableCash < netRequiredCash)
        {
            rejectReason = $"Insufficient cash: available={AvailableCash:F2}, required={netRequiredCash:F2}";
            return false;
        }

        // 4. 执行 Phase 1 预冻结
        _frozenCash += netRequiredCash;
        frozenCash = netRequiredCash;

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
        if (exchangeDate.Date > _lastRolloverDate)
        {
            _lastRolloverDate = exchangeDate.Date;
        }

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

            double allocatedOpenSlippage = currentAbsTotal > 0m
                ? (double)(closeQty / currentAbsTotal) * pos.OpenSlippageCost
                : 0.0;
            pos.OpenSlippageCost = Math.Max(0.0, pos.OpenSlippageCost - allocatedOpenSlippage);

            double closeFee = report.FillQuantity > 0m
                ? (double)(closeQty / report.FillQuantity) * report.Commission
                : 0.0;
            double closeSlippage = report.FillQuantity > 0m
                ? (double)(closeQty / report.FillQuantity) * report.Slippage
                : 0.0;

            closeRealizedPnL = grossPnL - allocatedOpenFee - closeFee;

            _cash += (marginReleased + grossPnL);

            double initialNotional = (double)closeQty * pos.AverageCost * multiplier;
            double returnRate = initialNotional > 0.0 ? closeRealizedPnL / initialNotional : 0.0;
            MarketTime entryTime = pos.OpenTime != MarketTime.MinValue ? pos.OpenTime : report.Time;
            int holdingDays = (report.Time.Date - entryTime.Date).Days;
            int holdingSteps = holdingDays > 0 ? holdingDays : Math.Max(1, (int)(report.Time - entryTime).TotalMinutes);

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
                allocatedOpenSlippage + closeSlippage,
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
                pos.OpenSlippageCost = 0.0;
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

            // 分摊并累加开仓在途手续费池与滑点损耗池
            double openFee = report.FillQuantity > 0m
                ? (double)(openQty / report.FillQuantity) * report.Commission
                : 0.0;
            pos.OpenCommission += openFee;

            double openSlippage = report.FillQuantity > 0m
                ? (double)(openQty / report.FillQuantity) * report.Slippage
                : 0.0;
            pos.OpenSlippageCost += openSlippage;

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
                        pos.PendingLots.Add(new PendingLot(openQty, settleDays));
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
                        pos.PendingLots.Add(new PendingLot(openQty, settleDays));
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
    /// 纯事件驱动步进：每个客观交易日步进递减 1 个交易日步长；归零时全额解冻，零未来依赖，回测与实盘绝对同构。
    /// </summary>
    public void ProcessDayRollover(MarketTime exchangeDate)
    {
        MarketTime today = exchangeDate.Date;
        if (today <= _lastRolloverDate)
        {
            return; // 同一日内或陈旧时间步，纯幂等守卫直接短路
        }
        _lastRolloverDate = today;

        foreach (var pos in _positions.Values)
        {
            if (pos.PendingLots.Count == 0) continue;

            decimal unlocked = 0m;
            for (int i = pos.PendingLots.Count - 1; i >= 0; i--)
            {
                var lot = pos.PendingLots[i];
                int remaining = lot.RemainingDays - 1;
                if (remaining <= 0)
                {
                    unlocked += lot.Quantity;
                    pos.PendingLots.RemoveAt(i);
                }
                else
                {
                    pos.PendingLots[i] = new PendingLot(lot.Quantity, remaining);
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
        _lastRolloverDate = MarketTime.MinValue;
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
                lots.Add(new PendingLotSnapshot(lot.Quantity, lot.RemainingDays));
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
                lots,
                pos.OpenSlippageCost
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
                OpenSlippageCost = snap.OpenSlippageCost,
                LastPrice = snap.LastPrice,
                OpenTime = snap.OpenTime,
                OpenTag = snap.OpenTag
            };

            foreach (var lot in snap.PendingLots)
            {
                int remDays = lot.RemainingDays > 0 ? lot.RemainingDays : 1;
                pos.PendingLots.Add(new PendingLot(lot.Quantity, remDays));
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
