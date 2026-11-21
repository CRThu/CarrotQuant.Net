using System;
using System.Collections.Generic;
using System.Threading;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Broker;

/// <summary>
/// 客户端权威经纪商（Broker）。
/// 面向策略层的统一入口，统领本地资产账本、多空持仓均价、两阶段预冻结与订单生命周期状态机。
/// 严格基于依赖倒置：通过通用接口 IExchangeGateway 路由订单，回测与实盘 100% 共享完全一致的单一内核代码。
/// </summary>
public sealed class Broker : IBroker, IBrokerEngineDriver, IAccountSnapshotProvider, ICheckpointable<BrokerSnapshot>, IExchangeGatewayProvider
{
    private readonly IExchangeGateway _gateway;
    private readonly BrokerConfig _config;
    private readonly IFieldRegistry? _fields;
    private readonly TimeZoneInfo _timeZone;
    private readonly AccountLedger _ledger;
    private readonly object _syncLock = new();

    private readonly Dictionary<string, BrokerOrderRecord> _orders = new(StringComparer.Ordinal);
    private long _orderSequence = 0;
    private MarketTime _currentTime = MarketTime.MinValue;

    public event Action<string, OrderStatus>? OnOrderStatusChanged;
    public event Action<ExecutionReport>? OnTrade;
    public event Action<TradeExecutionDetails>? OnTradeDetails;
    public event Action<Trade>? OnTradeClosed;
    public event Action<SettlementRecord>? OnSettlement;
    public event Action<PositionEvent>? OnPositionChanged;

    /// <summary>
    /// 显式实现 <see cref="IExchangeGatewayProvider"/>，封闭外部对原始网关的直接访问，避免策略绕过两阶段预冻结。
    /// </summary>
    IExchangeGateway IExchangeGatewayProvider.Gateway => _gateway;

    /// <summary>
    /// 获取经纪商当前推进的全局时间戳。
    /// </summary>
    public MarketTime CurrentTime => _currentTime;

    public Broker(IExchangeGateway gateway, BrokerConfig config, IFieldRegistry? fields = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _fields = fields;
        _timeZone = config.TimeZone ?? TimeZoneInfo.Utc;
        _ledger = new AccountLedger(config, fields);

        _gateway.OnExecution += HandleExecution;
        _gateway.OnOrderStateChanged += HandleOrderStateChanged;
    }

    /// <inheritdoc />
    public double GetCash()
    {
        lock (_syncLock)
        {
            return _ledger.AvailableCash;
        }
    }

    /// <summary>
    /// 当前账户可用现金。
    /// </summary>
    public double AvailableCash => GetCash();

    /// <summary>
    /// 当前账户在途冻结资金。
    /// </summary>
    public double FrozenCash
    {
        get
        {
            lock (_syncLock)
            {
                return _ledger.FrozenCash;
            }
        }
    }

    /// <inheritdoc />
    public double GetTotalEquity() => GetTotalEquity(null);

    /// <summary>
    /// 获取当前账户的总动态资产权益，允许传入外部实时行情解析委托（供实盘多品种行情源实时盯市估值）。
    /// </summary>
    /// <param name="priceResolver">实时行情解析委托（输入标的代码返回最新价格，未提供或返回 &lt;= 0 时自动回退至网关或持仓最近价）。</param>
    /// <returns>账户总权益。</returns>
    public double GetTotalEquity(Func<string, double>? priceResolver)
    {
        lock (_syncLock)
        {
            return _ledger.GetTotalEquity(sym => ResolvePrice(sym, priceResolver));
        }
    }

    /// <summary>
    /// 生成当前时间步的净值点数据。
    /// </summary>
    /// <param name="time">当前时间戳。</param>
    /// <param name="priceResolver">可选的价格解析委托。</param>
    /// <returns>净值点。</returns>
    public EquityPoint CreateEquityPoint(MarketTime time, Func<string, double>? priceResolver = null)
    {
        lock (_syncLock)
        {
            double equity = _ledger.GetTotalEquity(sym => ResolvePrice(sym, priceResolver));
            double cash = _ledger.AvailableCash;
            double frozenCash = _ledger.FrozenCash;
            double occupiedMargin = _ledger.GetOccupiedMargin();
            double totalMarketValue = _ledger.GetTotalMarketValue(sym => ResolvePrice(sym, priceResolver));
            double unrealizedPnL = _ledger.GetTotalUnrealizedPnL(sym => ResolvePrice(sym, priceResolver));
            return new EquityPoint(time, equity, cash, frozenCash, occupiedMargin, totalMarketValue, unrealizedPnL);
        }
    }

    /// <summary>
    /// 权威生成当前时点的日终全景资产与持仓快照。
    /// </summary>
    /// <param name="time">快照时间戳。</param>
    /// <param name="priceResolver">可选的价格解析委托。</param>
    /// <returns>日终资产与持仓快照。</returns>
    public DailySnapshot CreateDailySnapshot(MarketTime time, Func<string, double>? priceResolver = null)
    {
        lock (_syncLock)
        {
            double equity = _ledger.GetTotalEquity(sym => ResolvePrice(sym, priceResolver));
            double cash = _ledger.AvailableCash;
            double frozenCash = _ledger.FrozenCash;
            double occupiedMargin = _ledger.GetOccupiedMargin();
            double totalMarketValue = _ledger.GetTotalMarketValue(sym => ResolvePrice(sym, priceResolver));
            double unrealizedPnL = _ledger.GetTotalUnrealizedPnL(sym => ResolvePrice(sym, priceResolver));
            var positions = _ledger.GetActivePositionSnapshots(sym => ResolvePrice(sym, priceResolver), equity);

            return new DailySnapshot(time, equity, cash, frozenCash, occupiedMargin, totalMarketValue, unrealizedPnL, positions);
        }
    }

    /// <summary>
    /// 获取当前所有活跃持仓的快照列表。
    /// </summary>
    /// <param name="priceResolver">可选的价格解析委托。</param>
    /// <returns>活跃持仓快照列表。</returns>
    public IReadOnlyList<PositionSnapshot> GetActivePositions(Func<string, double>? priceResolver = null)
    {
        lock (_syncLock)
        {
            double equity = _ledger.GetTotalEquity(sym => ResolvePrice(sym, priceResolver));
            return _ledger.GetActivePositionSnapshots(sym => ResolvePrice(sym, priceResolver), equity);
        }
    }

    private double ResolvePrice(string symbol, Func<string, double>? customResolver)
    {
        double p = customResolver?.Invoke(symbol) ?? 0.0;
        if (p > 0.0 && !double.IsNaN(p) && !double.IsInfinity(p)) return p;
        if (_gateway is ISimulatedExchange sim)
        {
            p = sim.GetMarketPrice(symbol);
            if (p > 0.0 && !double.IsNaN(p) && !double.IsInfinity(p)) return p;
        }
        return 0.0;
    }

    /// <inheritdoc />
    public decimal GetPosition(string symbol)
    {
        lock (_syncLock)
        {
            return _ledger.GetPosition(symbol);
        }
    }

    /// <inheritdoc />
    public decimal GetAvailablePosition(string symbol)
    {
        lock (_syncLock)
        {
            return _ledger.GetAvailablePosition(symbol);
        }
    }

    /// <inheritdoc />
    public double GetPositionCost(string symbol)
    {
        lock (_syncLock)
        {
            return _ledger.GetPositionCost(symbol);
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, decimal> GetPositions()
    {
        lock (_syncLock)
        {
            return _ledger.GetPositions();
        }
    }

    /// <inheritdoc />
    public OrderStatus GetOrderStatus(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        lock (_syncLock)
        {
            return _orders.TryGetValue(orderId, out var ord) ? ord.Status : OrderStatus.Rejected;
        }
    }

    /// <inheritdoc />
    public string SubmitOrder(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 统一归一化订单时效，未指定时采用经纪商默认时效规则
        if (!request.TimeInForce.HasValue)
        {
            request = request with { TimeInForce = _config.DefaultTimeInForce };
        }

        string orderId = string.IsNullOrWhiteSpace(request.ClientOrderId)
            ? $"ord_{Interlocked.Increment(ref _orderSequence)}"
            : request.ClientOrderId;

        double multiplier = GetMultiplier(request.Symbol);
        double marginRate = GetMarginRate(request.Symbol);
        int settleDays = GetSettleDays(request.Symbol);

        bool freezeOk;
        string? rejectReason;
        decimal closeQty = 0m;
        decimal openQty = 0m;
        double frozenCash = 0.0;
        decimal frozenPosition = 0m;

        lock (_syncLock)
        {
            // 校验订单标识唯一性：拦截重复 ClientOrderId 避免覆盖在途活跃订单及关联冻结资产
            if (_orders.ContainsKey(orderId))
            {
                freezeOk = false;
                rejectReason = $"Order with ID '{orderId}' already exists.";
            }
            else
            {
                double estimatedPrice = GetEstimatedPrice(request);
                if (request.IsBuy && (estimatedPrice <= 0.0 || double.IsNaN(estimatedPrice) || double.IsInfinity(estimatedPrice)))
                {
                    freezeOk = false;
                    rejectReason = $"Cannot determine valid price for buy order on '{request.Symbol}' to reserve cash.";
                }
                else
                {
                    freezeOk = _ledger.TryFreeze(
                        request,
                        estimatedPrice,
                        multiplier,
                        marginRate,
                        out closeQty,
                        out openQty,
                        out frozenCash,
                        out frozenPosition,
                        out rejectReason);
                }

                var record = new BrokerOrderRecord(orderId, request)
                {
                    Multiplier = multiplier,
                    MarginRate = marginRate,
                    SettleDays = settleDays,
                    CloseQuantity = freezeOk ? closeQty : 0m,
                    OpenQuantity = freezeOk ? openQty : 0m,
                    RemainingCloseQuantity = freezeOk ? closeQty : 0m,
                    RemainingOpenQuantity = freezeOk ? openQty : 0m,
                    InitialFrozenCash = freezeOk ? frozenCash : 0.0,
                    RemainingFrozenCash = freezeOk ? frozenCash : 0.0,
                    InitialFrozenPosition = freezeOk ? frozenPosition : 0m,
                    RemainingFrozenPosition = freezeOk ? frozenPosition : 0m,
                    Status = freezeOk ? OrderStatus.Submitted : OrderStatus.Rejected,
                    RejectReason = rejectReason
                };

                _orders[orderId] = record;
            }
        }

        if (!freezeOk)
        {
            // 锁外派发拒绝事件
            OnOrderStatusChanged?.Invoke(orderId, OrderStatus.Rejected);
            return orderId;
        }

        // 锁外派发已提交事件并向网关发送订单（杜绝死锁）
        OnOrderStatusChanged?.Invoke(orderId, OrderStatus.Submitted);
        _gateway.SendOrder(orderId, request);

        return orderId;
    }

    /// <inheritdoc />
    public string Buy(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "", TimeInForce? timeInForce = null)
        => SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, type, price, quantity, orderTag, timeInForce) { ClientOrderId = clientOrderId ?? string.Empty });

    /// <inheritdoc />
    public string Sell(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "", TimeInForce? timeInForce = null)
        => SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, type, price, quantity, orderTag, timeInForce) { ClientOrderId = clientOrderId ?? string.Empty });

    /// <inheritdoc />
    public string ClosePosition(string symbol, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "", TimeInForce? timeInForce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var pos = GetPosition(symbol);
        if (pos == 0m) return string.Empty;

        var dir = pos > 0m ? OrderDirection.Sell : OrderDirection.Buy;
        return SubmitOrder(new OrderRequest(symbol, dir, type, price, Math.Abs(pos), orderTag, timeInForce) { ClientOrderId = clientOrderId ?? string.Empty });
    }

    /// <inheritdoc />
    public void CancelOrder(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        // 严格在锁外向网关发送撤单请求（撤单成功后由 OnOrderStateChanged 回调驱动解冻）
        _gateway.CancelOrder(orderId);
    }

    /// <inheritdoc />
    public void Step(MarketTime currentTime)
    {
        lock (_syncLock)
        {
            _currentTime = currentTime;
            if (_gateway is ISimulatedExchange sim)
            {
                sim.SetCurrentTime(currentTime);
            }
            MarketTime exchangeDate = ToExchangeDate(currentTime);
            _ledger.ProcessDayRollover(exchangeDate);
        }
    }

    /// <inheritdoc />
    public void SyncState(double cash, IReadOnlyDictionary<string, decimal> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        List<string>? canceledOrderIds = null;

        lock (_syncLock)
        {
            _ledger.SyncState(cash, positions);

            // 将所有未终结在途订单标记为 Canceled
            foreach (var kvp in _orders)
            {
                var ord = kvp.Value;
                if (ord.Status == OrderStatus.Submitted || ord.Status == OrderStatus.Pending || ord.Status == OrderStatus.PartialFilled)
                {
                    ord.Status = OrderStatus.Canceled;
                    ord.RemainingFrozenCash = 0.0;
                    ord.RemainingFrozenPosition = 0m;
                    canceledOrderIds ??= new List<string>();
                    canceledOrderIds.Add(ord.OrderId);
                }
            }
        }

        if (canceledOrderIds != null)
        {
            for (int i = 0; i < canceledOrderIds.Count; i++)
            {
                OnOrderStatusChanged?.Invoke(canceledOrderIds[i], OrderStatus.Canceled);
            }
        }
    }

    private void HandleExecution(ExecutionReport report)
    {
        PositionEvent? posEvent = null;
        OrderStatus newStatus = OrderStatus.Filled;
        bool shouldDispatchState = false;
        TradeExecutionDetails? details = null;
        Trade? closedTrade = null;
        SettlementRecord? settlement = null;

        lock (_syncLock)
        {
            if (!_orders.TryGetValue(report.OrderId, out var ord))
            {
                // 未知订单或外部自成交，以无订单模式直接落账
                return;
            }

            // 按先进先出顺序，优先结转平仓额度，剩余部分作为新开仓额度
            decimal thisFillClose = Math.Min(report.FillQuantity, ord.RemainingCloseQuantity);
            decimal thisFillOpen = Math.Min(report.FillQuantity - thisFillClose, ord.RemainingOpenQuantity);
            ord.RemainingCloseQuantity -= thisFillClose;
            ord.RemainingOpenQuantity -= thisFillOpen;

            // 计算该笔成交对应分配的预冻结释放量（平仓段精准释放持仓冻结，新开仓段按比例释放保证金冻结）
            double unfreezeCash = ord.OpenQuantity > 0m && ord.InitialFrozenCash > 0.0
                ? Math.Min(ord.RemainingFrozenCash, (double)(thisFillOpen / ord.OpenQuantity) * ord.InitialFrozenCash)
                : 0.0;

            decimal unfreezePos = ord.CloseQuantity > 0m && ord.InitialFrozenPosition > 0m
                ? Math.Min(ord.RemainingFrozenPosition, thisFillClose)
                : 0m;

            ord.RemainingFrozenCash = Math.Max(0.0, ord.RemainingFrozenCash - unfreezeCash);
            ord.RemainingFrozenPosition = Math.Max(0m, ord.RemainingFrozenPosition - unfreezePos);
            ord.FilledQuantity += report.FillQuantity;

            if (ord.FilledQuantity >= ord.Request.Quantity)
            {
                ord.Status = OrderStatus.Filled;
                newStatus = OrderStatus.Filled;
                shouldDispatchState = true;
            }
            else
            {
                ord.Status = OrderStatus.PartialFilled;
                newStatus = OrderStatus.PartialFilled;
                shouldDispatchState = true;
            }

            // 若外部撮合回报未携带券商佣金，则依据券商费率配置在此权威计算
            if (report.Commission <= 0.0 && (_config.FeeRate > 0.0 || _config.MinFee > 0.0 || _config.OnCalculateFee != null))
            {
                double turnover = (double)report.FillQuantity * report.FillPrice * ord.Multiplier;
                double commission = FeeCalculator.CalculateFee(turnover, ord.Request, _config);
                report = report with { Commission = commission };
            }

            MarketTime exchangeDate = ToExchangeDate(report.Time);
            _ledger.CommitFill(
                report,
                ord.Request.IsBuy,
                thisFillClose,
                thisFillOpen,
                ord.Multiplier,
                ord.MarginRate,
                ord.SettleDays,
                unfreezeCash,
                unfreezePos,
                exchangeDate,
                ord.Request.OrderTag,
                out posEvent,
                out closedTrade,
                out settlement);

            details = new TradeExecutionDetails(report, ord.Request.Direction, ord.Multiplier, ord.Request.OrderTag);
        }

        // 锁外派发状态、成交与持仓变更事件（Open Calls）
        if (shouldDispatchState)
        {
            OnOrderStatusChanged?.Invoke(report.OrderId, newStatus);
        }
        if (details.HasValue)
        {
            OnTradeDetails?.Invoke(details.Value);
        }
        OnTrade?.Invoke(report);
        if (settlement != null)
        {
            OnSettlement?.Invoke(settlement);
        }
        if (closedTrade != null)
        {
            OnTradeClosed?.Invoke(closedTrade);
        }
        if (posEvent != null)
        {
            OnPositionChanged?.Invoke(posEvent);
        }
    }

    private void HandleOrderStateChanged(string orderId, OrderStatus status)
    {
        bool shouldDispatch = false;

        lock (_syncLock)
        {
            if (_orders.TryGetValue(orderId, out var ord))
            {
                // 仅在撤单或拒绝时，释放订单当前剩余的预冻结额度
                if (status == OrderStatus.Canceled || status == OrderStatus.Rejected)
                {
                    if (ord.Status != OrderStatus.Filled && ord.Status != OrderStatus.Canceled && ord.Status != OrderStatus.Rejected)
                    {
                        ord.Status = status;
                        _ledger.Unfreeze(ord.Request.Symbol, ord.RemainingFrozenCash, ord.RemainingFrozenPosition, ord.Request.IsBuy);
                        ord.RemainingFrozenCash = 0.0;
                        ord.RemainingFrozenPosition = 0m;
                        shouldDispatch = true;
                    }
                }
                else if (ord.Status != OrderStatus.Filled)
                {
                    ord.Status = status;
                    shouldDispatch = true;
                }
            }
        }

        if (shouldDispatch)
        {
            OnOrderStatusChanged?.Invoke(orderId, status);
        }
    }

    private MarketTime ToExchangeDate(MarketTime time)
    {
        if (_timeZone.Equals(TimeZoneInfo.Utc))
        {
            return time.Date;
        }
        return time.ToExchangeTime(_timeZone).Date;
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

    private int GetSettleDays(string symbol)
    {
        if (_config.OnGetSettleDays != null)
        {
            return _config.OnGetSettleDays(symbol);
        }
        return _config.SettleDays;
    }

    private double GetEstimatedPrice(OrderRequest request)
    {
        if (request.Price > 0.0 && !double.IsNaN(request.Price) && !double.IsInfinity(request.Price))
        {
            return request.Price;
        }

        // 市价单（Price <= 0.0）：尝试从绑定的网关中查询当前标的公允参考市价
        double marketPrice = 0.0;
        if (_gateway is ISimulatedExchange sim)
        {
            marketPrice = sim.GetMarketPrice(request.Symbol);
        }

        if (marketPrice > 0.0 && !double.IsNaN(marketPrice) && !double.IsInfinity(marketPrice))
        {
            // 预估滑点缓冲：买单上浮预留充足保证金，卖单下浮保守预估
            return request.IsBuy
                ? marketPrice * (1.0 + Math.Max(0.0, _config.Slippage))
                : marketPrice * Math.Max(0.0, 1.0 - Math.Max(0.0, _config.Slippage));
        }

        return 0.0;
    }

    /// <summary>
    /// 导出经纪商资产与在途活跃订单快照。
    /// </summary>
    public BrokerSnapshot SaveState()
    {
        lock (_syncLock)
        {
            var ledgerSnap = _ledger.SaveState();
            var activeOrders = new List<BrokerOrderSnapshot>();
            foreach (var kvp in _orders)
            {
                var ord = kvp.Value;
                if (ord.Status == OrderStatus.Submitted || ord.Status == OrderStatus.Pending || ord.Status == OrderStatus.PartialFilled)
                {
                    activeOrders.Add(new BrokerOrderSnapshot(
                        ord.OrderId,
                        ord.Request,
                        ord.Status,
                        ord.RejectReason,
                        ord.Multiplier,
                        ord.MarginRate,
                        ord.SettleDays,
                        ord.CloseQuantity,
                        ord.OpenQuantity,
                        ord.RemainingCloseQuantity,
                        ord.RemainingOpenQuantity,
                        ord.FilledQuantity,
                        ord.InitialFrozenCash,
                        ord.RemainingFrozenCash,
                        ord.InitialFrozenPosition,
                        ord.RemainingFrozenPosition
                    ));
                }
            }

            return new BrokerSnapshot(ledgerSnap, activeOrders);
        }
    }

    /// <summary>
    /// 恢复经纪商资产与在途活跃订单快照。
    /// </summary>
    public void RestoreState(BrokerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_syncLock)
        {
            _ledger.RestoreState(snapshot.Ledger);
            _orders.Clear();
            if (snapshot.ActiveOrders != null)
            {
                foreach (var s in snapshot.ActiveOrders)
                {
                    var record = new BrokerOrderRecord(s.OrderId, s.Request)
                    {
                        Status = s.Status,
                        RejectReason = s.RejectReason,
                        Multiplier = s.Multiplier,
                        MarginRate = s.MarginRate,
                        SettleDays = s.SettleDays,
                        CloseQuantity = s.CloseQuantity,
                        OpenQuantity = s.OpenQuantity,
                        RemainingCloseQuantity = s.RemainingCloseQuantity,
                        RemainingOpenQuantity = s.RemainingOpenQuantity,
                        FilledQuantity = s.FilledQuantity,
                        InitialFrozenCash = s.InitialFrozenCash,
                        RemainingFrozenCash = s.RemainingFrozenCash,
                        InitialFrozenPosition = s.InitialFrozenPosition,
                        RemainingFrozenPosition = s.RemainingFrozenPosition
                    };
                    _orders[s.OrderId] = record;
                }
            }
        }
    }

    /// <summary>
    /// 兼容旧版仅恢复纯账本快照。
    /// </summary>
    public void RestoreState(AccountLedgerSnapshot snapshot) => _ledger.RestoreState(snapshot);
}

/// <summary>
/// 经纪商内部在途订单生命周期记录。
/// </summary>
internal sealed class BrokerOrderRecord
{
    public string OrderId { get; }
    public OrderRequest Request { get; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string? RejectReason { get; set; }

    public double Multiplier { get; set; } = 1.0;
    public double MarginRate { get; set; } = 1.0;
    public int SettleDays { get; set; } = 0;

    public decimal CloseQuantity { get; set; }
    public decimal OpenQuantity { get; set; }
    public decimal RemainingCloseQuantity { get; set; }
    public decimal RemainingOpenQuantity { get; set; }
    public decimal FilledQuantity { get; set; }

    public double InitialFrozenCash { get; set; }
    public double RemainingFrozenCash { get; set; }
    public decimal InitialFrozenPosition { get; set; }
    public decimal RemainingFrozenPosition { get; set; }

    public BrokerOrderRecord(string orderId, OrderRequest request)
    {
        OrderId = orderId;
        Request = request;
    }
}
