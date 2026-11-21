using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    private readonly Dictionary<long, BrokerOrderRecord> _orders = new();
    private readonly Dictionary<long, BrokerOrderRecord> _pendingConditionals = new();
    private long _orderSequence = 0;
    private long _ocoGroupSequence = 0;
    private MarketTime _currentTime = MarketTime.MinValue;

    // 活跃条件单状态机步进复用缓冲区：
    // 1. 规避高频每步 new 集合的 GC 压力，实现热路径内存零开销（Rule 10）；
    // 2. 贯彻 Open Calls 模式：在临界区内完成撮合与状态跃迁，将触发单与事件暂存至缓冲区，锁外执行外部事件通知与网关下单，根除潜在死锁隐患。
    private readonly List<BrokerOrderRecord> _conditionalSortBuffer = new();
    private readonly List<(BrokerOrderRecord Order, double ExecPrice)> _triggeredOrdersBuffer = new();
    private readonly List<OrderEvent> _orderEventsBuffer = new();
    private readonly HashSet<long> _triggeredOcoGroupsBuffer = new();

    public event Action<long, OrderStatus>? OnOrderStatusChanged;
    public event Action<ExecutionReport>? OnTrade;
    public event Action<TradeExecutionDetails>? OnTradeDetails;
    public event Action<Trade>? OnTradeClosed;
    public event Action<SettlementRecord>? OnSettlement;
    public event Action<PositionEvent>? OnPositionChanged;
    public event Action<OrderEvent>? OnOrderEvent;

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
            Func<string, double> priceFunc = sym => ResolvePrice(sym, priceResolver);
            double equity = _ledger.GetTotalEquity(priceFunc);
            double cash = _ledger.AvailableCash;
            double frozenCash = _ledger.FrozenCash;
            double occupiedMargin = _ledger.GetOccupiedMargin();
            double totalMarketValue = _ledger.GetTotalMarketValue(priceFunc);
            double unrealizedPnL = _ledger.GetTotalUnrealizedPnL(priceFunc);
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
            Func<string, double> priceFunc = sym => ResolvePrice(sym, priceResolver);
            double equity = _ledger.GetTotalEquity(priceFunc);
            double cash = _ledger.AvailableCash;
            double frozenCash = _ledger.FrozenCash;
            double occupiedMargin = _ledger.GetOccupiedMargin();
            double totalMarketValue = _ledger.GetTotalMarketValue(priceFunc);
            double unrealizedPnL = _ledger.GetTotalUnrealizedPnL(priceFunc);
            var positions = _ledger.GetActivePositionSnapshots(priceFunc, equity);

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
            Func<string, double> priceFunc = sym => ResolvePrice(sym, priceResolver);
            double equity = _ledger.GetTotalEquity(priceFunc);
            return _ledger.GetActivePositionSnapshots(priceFunc, equity);
        }
    }

    private double ResolvePrice(string symbol, Func<string, double>? customResolver)
    {
        double p = customResolver?.Invoke(symbol) ?? 0.0;
        if (p > 0.0 && !double.IsNaN(p) && !double.IsInfinity(p)) return p;
        if (_gateway is ISimulatedExchange sim)
        {
            p = sim.GetBar(symbol).CloseOr(0.0);
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
    public OrderStatus GetOrderStatus(long orderId)
    {
        if (orderId <= 0) return OrderStatus.Rejected;
        lock (_syncLock)
        {
            return _orders.TryGetValue(orderId, out var ord) ? ord.Status : OrderStatus.Rejected;
        }
    }

    /// <inheritdoc />
    public (long StopOrderId, long TakeProfitOrderId) Bracket(
        string symbol,
        decimal quantity,
        double stopPrice,
        double takeProfitPrice,
        string orderTag = "",
        TimeInForce? timeInForce = null,
        OrderDirection? closeDirection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");

        long groupId = Interlocked.Increment(ref _ocoGroupSequence);
        var tif = timeInForce ?? TimeInForce.GoodTilCanceled;
        OrderDirection dir;
        if (closeDirection.HasValue)
        {
            dir = closeDirection.Value;
        }
        else
        {
            var pos = GetPosition(symbol);
            dir = pos < 0m ? OrderDirection.Buy : OrderDirection.Sell;
        }

        var stopReq = new OrderRequest(symbol, dir, OrderType.Stop, 0.0, quantity, orderTag, tif)
        {
            StopPrice = stopPrice,
            OcoGroupId = groupId
        };

        var tpReq = new OrderRequest(symbol, dir, OrderType.TakeProfit, 0.0, quantity, orderTag, tif)
        {
            StopPrice = takeProfitPrice,
            OcoGroupId = groupId
        };

        long stopId = SubmitOrder(stopReq);
        long tpId = SubmitOrder(tpReq);
        return (stopId, tpId);
    }

    /// <inheritdoc />
    public long SubmitOrder(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 统一归一化订单时效，未指定时采用经纪商默认时效规则（条件单强制默认 GTC 防持仓裸奔）
        if (!request.TimeInForce.HasValue)
        {
            request = request with { TimeInForce = IsConditional(request.Type) ? TimeInForce.GoodTilCanceled : _config.DefaultTimeInForce };
        }

        if (request.OrderId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.OrderId, "订单 OrderId 不能为负数。");
        }

        long orderId = request.OrderId == 0
            ? Interlocked.Increment(ref _orderSequence)
            : request.OrderId;

        if (request.OrderId == 0)
        {
            request = request with { OrderId = orderId };
        }

        double multiplier = GetMultiplier(request.Symbol);
        double marginRate = GetMarginRate(request.Symbol);
        int settleDays = GetSettleDays(request.Symbol);
        var submitTime = _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch;

        // 条件单处理（触发前零冻结）
        if (IsConditional(request.Type))
        {
            bool isDuplicate = false;
            lock (_syncLock)
            {
                if (_orders.ContainsKey(orderId))
                {
                    isDuplicate = true;
                }
                else
                {
                    var record = new BrokerOrderRecord(orderId, request)
                    {
                        Multiplier = multiplier,
                        MarginRate = marginRate,
                        SettleDays = settleDays,
                        Status = OrderStatus.Pending
                    };
                    _orders[orderId] = record;
                    _pendingConditionals[orderId] = record;
                }
            }

            if (isDuplicate)
            {
                var rejectEvt = new OrderEvent(
                    submitTime,
                    orderId,
                    request.Symbol,
                    request.Direction,
                    request.Type,
                    request.Price,
                    request.Quantity,
                    OrderEventType.Rejected,
                    OrderStatus.Rejected,
                    Reason: $"Order with ID '{orderId}' already exists.",
                    OrderTag: request.OrderTag);
                OnOrderStatusChanged?.Invoke(orderId, OrderStatus.Rejected);
                OnOrderEvent?.Invoke(rejectEvt);
                return orderId;
            }

            var conditionalSubmitEvt = new OrderEvent(
                submitTime,
                orderId,
                request.Symbol,
                request.Direction,
                request.Type,
                request.Price,
                request.Quantity,
                OrderEventType.Submitted,
                OrderStatus.Pending,
                LeavesQuantity: request.Quantity,
                OrderTag: request.OrderTag);

            OnOrderStatusChanged?.Invoke(orderId, OrderStatus.Pending);
            OnOrderEvent?.Invoke(conditionalSubmitEvt);
            return orderId;
        }

        bool freezeOk;
        string? rejectReason;
        decimal closeQty = 0m;
        decimal openQty = 0m;
        double frozenCash = 0.0;
        decimal frozenPosition = 0m;

        lock (_syncLock)
        {
            // 校验订单标识唯一性：拦截重复 OrderId 避免覆盖在途活跃订单及关联冻结资产
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
            var rejectTime = _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch;
            var rejectEvt = new OrderEvent(
                rejectTime,
                orderId,
                request.Symbol,
                request.Direction,
                request.Type,
                request.Price,
                request.Quantity,
                OrderEventType.Rejected,
                OrderStatus.Rejected,
                FillQuantity: 0m,
                CumQuantity: 0m,
                LeavesQuantity: request.Quantity,
                Reason: rejectReason ?? "Pre-freeze check failed.",
                OrderTag: request.OrderTag);

            // 锁外派发拒绝事件
            OnOrderStatusChanged?.Invoke(orderId, OrderStatus.Rejected);
            OnOrderEvent?.Invoke(rejectEvt);
            return orderId;
        }

        submitTime = _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch;
        var submitEvt = new OrderEvent(
            submitTime,
            orderId,
            request.Symbol,
            request.Direction,
            request.Type,
            request.Price,
            request.Quantity,
            OrderEventType.Submitted,
            OrderStatus.Submitted,
            FillQuantity: 0m,
            CumQuantity: 0m,
            LeavesQuantity: request.Quantity,
            OrderTag: request.OrderTag);

        // 锁外派发已提交事件并向网关发送订单（杜绝死锁）
        OnOrderStatusChanged?.Invoke(orderId, OrderStatus.Submitted);
        OnOrderEvent?.Invoke(submitEvt);
        _gateway.SendOrder(orderId, request);

        return orderId;
    }

    /// <inheritdoc />
    public long Buy(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string orderTag = "", TimeInForce? timeInForce = null)
        => SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, type, price, quantity, orderTag, timeInForce));

    /// <inheritdoc />
    public long Sell(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string orderTag = "", TimeInForce? timeInForce = null)
        => SubmitOrder(new OrderRequest(symbol, OrderDirection.Sell, type, price, quantity, orderTag, timeInForce));

    /// <inheritdoc />
    public long ClosePosition(string symbol, double price = 0.0, OrderType type = OrderType.Market, string orderTag = "", TimeInForce? timeInForce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var pos = GetPosition(symbol);
        if (pos == 0m) return 0L;

        var dir = pos > 0m ? OrderDirection.Sell : OrderDirection.Buy;
        return SubmitOrder(new OrderRequest(symbol, dir, type, price, Math.Abs(pos), orderTag, timeInForce));
    }

    /// <inheritdoc />
    public long StopLoss(string symbol, double stopPrice, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var pos = GetPosition(symbol);
        var dir = pos < 0m ? OrderDirection.Buy : OrderDirection.Sell;
        return SubmitOrder(new OrderRequest(symbol, dir, OrderType.Stop, 0.0, quantity, orderTag, timeInForce ?? TimeInForce.GoodTilCanceled)
        {
            StopPrice = stopPrice
        });
    }

    /// <inheritdoc />
    public long TakeProfit(string symbol, double targetPrice, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var pos = GetPosition(symbol);
        var dir = pos < 0m ? OrderDirection.Buy : OrderDirection.Sell;
        return SubmitOrder(new OrderRequest(symbol, dir, OrderType.TakeProfit, 0.0, quantity, orderTag, timeInForce ?? TimeInForce.GoodTilCanceled)
        {
            StopPrice = targetPrice
        });
    }

    /// <inheritdoc />
    public void CancelOrder(long orderId)
    {
        if (orderId <= 0) return;
        OrderEvent? cancelEvt = null;
        lock (_syncLock)
        {
            if (_orders.TryGetValue(orderId, out var ord) && ord.Status == OrderStatus.Pending && IsConditional(ord.Request.Type))
            {
                _pendingConditionals.Remove(orderId);
                ord.Status = OrderStatus.Canceled;
                ord.RejectReason = "Canceled by user.";
                var time = _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch;
                cancelEvt = new OrderEvent(
                    time,
                    ord.OrderId,
                    ord.Request.Symbol,
                    ord.Request.Direction,
                    ord.Request.Type,
                    ord.Request.Price,
                    ord.Request.Quantity,
                    OrderEventType.Canceled,
                    OrderStatus.Canceled,
                    LeavesQuantity: 0m,
                    Reason: "Canceled by user.",
                    OrderTag: ord.Request.OrderTag);
            }
        }

        if (cancelEvt != null)
        {
            OnOrderStatusChanged?.Invoke(orderId, OrderStatus.Canceled);
            OnOrderEvent?.Invoke(cancelEvt);
            return;
        }

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

        ProcessConditionalOrders(currentTime);
    }

    /// <inheritdoc />
    public void ProcessConditionalOrders(MarketTime currentTime)
    {
        if (_gateway is not ISimulatedExchange sim) return;

        lock (_syncLock)
        {
            if (_pendingConditionals.Count == 0) return;

            _conditionalSortBuffer.Clear();
            foreach (var kvp in _pendingConditionals)
            {
                _conditionalSortBuffer.Add(kvp.Value);
            }

            // 悲观仲裁：先排 Stop/StopLimit，再排 TakeProfit/TakeProfitLimit
            _conditionalSortBuffer.Sort((a, b) =>
            {
                bool aIsStop = a.Request.Type is OrderType.Stop or OrderType.StopLimit;
                bool bIsStop = b.Request.Type is OrderType.Stop or OrderType.StopLimit;
                if (aIsStop && !bIsStop) return -1;
                if (!aIsStop && bIsStop) return 1;
                return 0;
            });

            _triggeredOrdersBuffer.Clear();
            _orderEventsBuffer.Clear();
            _triggeredOcoGroupsBuffer.Clear();

            for (int i = 0; i < _conditionalSortBuffer.Count; i++)
            {
                var ord = _conditionalSortBuffer[i];
                if (ord.Status != OrderStatus.Pending) continue;

                if (ord.Request.OcoGroupId > 0 && _triggeredOcoGroupsBuffer.Contains(ord.Request.OcoGroupId))
                {
                    ord.Status = OrderStatus.Canceled;
                    ord.RejectReason = "Canceled by OCO bracket trigger.";
                    _pendingConditionals.Remove(ord.OrderId);
                    _orderEventsBuffer.Add(new OrderEvent(
                        currentTime,
                        ord.OrderId,
                        ord.Request.Symbol,
                        ord.Request.Direction,
                        ord.Request.Type,
                        ord.Request.Price,
                        ord.Request.Quantity,
                        OrderEventType.Canceled,
                        OrderStatus.Canceled,
                        Reason: ord.RejectReason,
                        OrderTag: ord.Request.OrderTag));
                    continue;
                }

                var bar = sim.GetBar(ord.Request.Symbol);
                if (!bar.IsValid) continue;

                if (CheckTrigger(ord.Request.Type, ord.Request.Direction, ord.Request.StopPrice, in bar))
                {
                    double execPrice = CalculateTriggerExecutionPrice(ord.Request.Type, ord.Request.Direction, ord.Request.StopPrice, in bar);

                    bool freezeOk = _ledger.TryFreeze(
                        ord.Request,
                        execPrice,
                        ord.Multiplier,
                        ord.MarginRate,
                        out var closeQty,
                        out var openQty,
                        out var frozenCash,
                        out var frozenPos,
                        out var rejectReason);

                    if (!freezeOk)
                    {
                        ord.Status = OrderStatus.Rejected;
                        ord.RejectReason = rejectReason ?? "Pre-freeze check failed due to insufficient available position/cash.";
                        _pendingConditionals.Remove(ord.OrderId);
                        _orderEventsBuffer.Add(new OrderEvent(
                            currentTime,
                            ord.OrderId,
                            ord.Request.Symbol,
                            ord.Request.Direction,
                            ord.Request.Type,
                            ord.Request.Price,
                            ord.Request.Quantity,
                            OrderEventType.Rejected,
                            OrderStatus.Rejected,
                            Reason: ord.RejectReason,
                            OrderTag: ord.Request.OrderTag));
                    }
                    else
                    {
                        ord.CloseQuantity = closeQty;
                        ord.OpenQuantity = openQty;
                        ord.RemainingCloseQuantity = closeQty;
                        ord.RemainingOpenQuantity = openQty;
                        ord.InitialFrozenCash = frozenCash;
                        ord.RemainingFrozenCash = frozenCash;
                        ord.InitialFrozenPosition = frozenPos;
                        ord.RemainingFrozenPosition = frozenPos;
                        ord.Status = OrderStatus.Submitted;

                        _pendingConditionals.Remove(ord.OrderId);
                        _triggeredOrdersBuffer.Add((ord, execPrice));
                        if (ord.Request.OcoGroupId > 0)
                        {
                            _triggeredOcoGroupsBuffer.Add(ord.Request.OcoGroupId);
                        }
                    }
                }
            }

            // 联动注销同组其他未触发单
            if (_triggeredOcoGroupsBuffer.Count > 0)
            {
                for (int i = 0; i < _conditionalSortBuffer.Count; i++)
                {
                    var ord = _conditionalSortBuffer[i];
                    if (ord.Status == OrderStatus.Pending && ord.Request.OcoGroupId > 0 && _triggeredOcoGroupsBuffer.Contains(ord.Request.OcoGroupId))
                    {
                        ord.Status = OrderStatus.Canceled;
                        ord.RejectReason = "Canceled by OCO bracket trigger.";
                        _pendingConditionals.Remove(ord.OrderId);
                        _orderEventsBuffer.Add(new OrderEvent(
                            currentTime,
                            ord.OrderId,
                            ord.Request.Symbol,
                            ord.Request.Direction,
                            ord.Request.Type,
                            ord.Request.Price,
                            ord.Request.Quantity,
                            OrderEventType.Canceled,
                            OrderStatus.Canceled,
                            Reason: ord.RejectReason,
                            OrderTag: ord.Request.OrderTag));
                    }
                }
            }
        }

        // 锁外派发拒绝与撤单事件（Open Calls 模式，杜绝死锁）
        for (int i = 0; i < _orderEventsBuffer.Count; i++)
        {
            var evt = _orderEventsBuffer[i];
            OnOrderStatusChanged?.Invoke(evt.OrderId, evt.Status);
            OnOrderEvent?.Invoke(evt);
        }

        // 锁外向网关发送触发后的报单
        for (int i = 0; i < _triggeredOrdersBuffer.Count; i++)
        {
            var (ord, execPrice) = _triggeredOrdersBuffer[i];
            var triggeredReq = ord.Request with { Price = execPrice };
            _gateway.SendOrder(ord.OrderId, triggeredReq);
        }
    }

    private static bool IsConditional(OrderType type) =>
        type is OrderType.Stop or OrderType.TakeProfit or OrderType.StopLimit or OrderType.TakeProfitLimit;

    private static bool CheckTrigger(OrderType type, OrderDirection dir, double stopPrice, in BarSnapshot bar)
    {
        return type switch
        {
            OrderType.Stop or OrderType.StopLimit =>
                dir == OrderDirection.Sell ? bar.LowOr(bar.Close) <= stopPrice : bar.HighOr(bar.Close) >= stopPrice,
            OrderType.TakeProfit or OrderType.TakeProfitLimit =>
                dir == OrderDirection.Sell ? bar.HighOr(bar.Close) >= stopPrice : bar.LowOr(bar.Close) <= stopPrice,
            _ => false
        };
    }

    private static double CalculateTriggerExecutionPrice(OrderType type, OrderDirection dir, double stopPrice, in BarSnapshot bar)
    {
        bool hasOpen = bar.HasOpen && bar.Open > 0.0;
        double open = bar.Open;

        switch (type)
        {
            case OrderType.Stop:
                if (dir == OrderDirection.Sell)
                {
                    return (hasOpen && open <= stopPrice) ? open : stopPrice;
                }
                else
                {
                    return (hasOpen && open >= stopPrice) ? open : stopPrice;
                }

            case OrderType.TakeProfit:
                if (dir == OrderDirection.Sell)
                {
                    return (hasOpen && open >= stopPrice) ? open : stopPrice;
                }
                else
                {
                    return (hasOpen && open <= stopPrice) ? open : stopPrice;
                }

            case OrderType.StopLimit:
            case OrderType.TakeProfitLimit:
                return stopPrice;

            default:
                return stopPrice;
        }
    }

    /// <inheritdoc />
    public void SyncState(double cash, IReadOnlyDictionary<string, decimal> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        List<long>? canceledOrderIds = null;
        List<OrderEvent>? canceledEvents = null;

        lock (_syncLock)
        {
            _ledger.SyncState(cash, positions);

            var eventTime = _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch;

            // 将所有未终结在途订单标记为 Canceled
            foreach (var kvp in _orders)
            {
                var ord = kvp.Value;
                if (ord.Status == OrderStatus.Submitted || ord.Status == OrderStatus.Pending || ord.Status == OrderStatus.PartialFilled)
                {
                    ord.Status = OrderStatus.Canceled;
                    ord.RemainingFrozenCash = 0.0;
                    ord.RemainingFrozenPosition = 0m;
                    canceledOrderIds ??= new List<long>();
                    canceledOrderIds.Add(ord.OrderId);

                    canceledEvents ??= new List<OrderEvent>();
                    canceledEvents.Add(new OrderEvent(
                        eventTime,
                        ord.OrderId,
                        ord.Request.Symbol,
                        ord.Request.Direction,
                        ord.Request.Type,
                        ord.Request.Price,
                        ord.Request.Quantity,
                        OrderEventType.Canceled,
                        OrderStatus.Canceled,
                        FillQuantity: 0m,
                        CumQuantity: ord.FilledQuantity,
                        LeavesQuantity: Math.Max(0m, ord.Request.Quantity - ord.FilledQuantity),
                        Reason: "Order canceled due to account state synchronization.",
                        OrderTag: ord.Request.OrderTag));
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
        if (canceledEvents != null)
        {
            for (int i = 0; i < canceledEvents.Count; i++)
            {
                OnOrderEvent?.Invoke(canceledEvents[i]);
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
        OrderEvent? orderEvt = null;

        lock (_syncLock)
        {
            if (!_orders.TryGetValue(report.OrderId, out var ord))
            {
                // 未知订单或外部自成交，以无订单模式直接落账
                return;
            }

            // 若外部回报未显式填充分笔进度凭证，依据订单当前成交状态自适应推导补全
            if (report.CumQuantity == 0m && report.LeavesQuantity == 0m)
            {
                decimal cum = ord.FilledQuantity + report.FillQuantity;
                decimal leaves = Math.Max(0m, ord.Request.Quantity - cum);
                report = report with { CumQuantity = cum, LeavesQuantity = leaves };
            }

            // 校验分笔成交不变量（CumQty + LeavesQty == OrderQty），硬断言杜绝生产 Release 下穿透
            if (report.CumQuantity + report.LeavesQuantity != ord.Request.Quantity)
            {
                throw new InvalidOperationException(
                    $"分笔成交守恒不变量被破坏: 订单 '{report.OrderId}' CumQuantity({report.CumQuantity}) + LeavesQuantity({report.LeavesQuantity}) != OrderQuantity({ord.Request.Quantity})");
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

            // 全额成交时吸收释放浮点尾差（解决循环小数除法导致的微小预冻结残留）
            if (report.LeavesQuantity == 0m || ord.FilledQuantity >= ord.Request.Quantity)
            {
                if (ord.RemainingFrozenCash > 0.0)
                {
                    _ledger.Unfreeze(ord.Request.Symbol, ord.RemainingFrozenCash, 0m, ord.Request.IsBuy);
                    ord.RemainingFrozenCash = 0.0;
                }
                if (ord.RemainingFrozenPosition > 0m)
                {
                    _ledger.Unfreeze(ord.Request.Symbol, 0.0, ord.RemainingFrozenPosition, ord.Request.IsBuy);
                    ord.RemainingFrozenPosition = 0m;
                }
            }

            details = new TradeExecutionDetails(report, ord.Request.Direction, ord.Multiplier, ord.Request.OrderTag);

            orderEvt = new OrderEvent(
                report.Time,
                report.OrderId,
                report.Symbol,
                ord.Request.Direction,
                ord.Request.Type,
                ord.Request.Price,
                ord.Request.Quantity,
                OrderEventType.Fill,
                newStatus,
                FillQuantity: report.FillQuantity,
                CumQuantity: ord.FilledQuantity,
                LeavesQuantity: report.LeavesQuantity,
                FillPrice: report.FillPrice,
                Role: report.Role,
                OrderTag: ord.Request.OrderTag);
        }

        // 锁外派发状态、成交与持仓变更事件（Open Calls）
        if (shouldDispatchState)
        {
            OnOrderStatusChanged?.Invoke(report.OrderId, newStatus);
        }
        if (orderEvt != null)
        {
            OnOrderEvent?.Invoke(orderEvt);
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

    private void HandleOrderStateChanged(OrderStateReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        bool shouldDispatch = false;
        OrderEvent? orderEvt = null;

        lock (_syncLock)
        {
            if (_orders.TryGetValue(report.OrderId, out var ord))
            {
                // 终态保护：一旦订单已处于终结状态，严禁任何逆向状态跃迁或重复终结
                if (ord.Status == OrderStatus.Filled ||
                    ord.Status == OrderStatus.Canceled ||
                    ord.Status == OrderStatus.Rejected)
                {
                    return;
                }

                // 幂等保护：若状态未发生改变，忽略重复通知
                if (ord.Status == report.Status)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(report.Reason))
                {
                    ord.RejectReason = report.Reason;
                }

                ord.Status = report.Status;

                // 终结性状态（撤单、拒绝）释放未成交的剩余预冻结资产
                if (report.Status == OrderStatus.Canceled || report.Status == OrderStatus.Rejected)
                {
                    _ledger.Unfreeze(ord.Request.Symbol, ord.RemainingFrozenCash, ord.RemainingFrozenPosition, ord.Request.IsBuy);
                    ord.RemainingFrozenCash = 0.0;
                    ord.RemainingFrozenPosition = 0m;
                }

                shouldDispatch = true;

                var eventType = report.Status switch
                {
                    OrderStatus.Canceled => OrderEventType.Canceled,
                    OrderStatus.Rejected => OrderEventType.Rejected,
                    OrderStatus.Submitted => OrderEventType.Submitted,
                    _ => throw new ArgumentOutOfRangeException(nameof(report), report.Status, $"不受支持的网关订单生命周期状态变更: {report.Status}")
                };

                // 优先使用网关权威回报的物理时间，缺失时使用当前时间步
                var eventTime = report.Time != MarketTime.MinValue
                    ? report.Time
                    : (_currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch);

                decimal leavesQty = report.LeavesQuantity > 0m
                    ? report.LeavesQuantity
                    : Math.Max(0m, ord.Request.Quantity - ord.FilledQuantity);

                orderEvt = new OrderEvent(
                    eventTime,
                    report.OrderId,
                    ord.Request.Symbol,
                    ord.Request.Direction,
                    ord.Request.Type,
                    ord.Request.Price,
                    ord.Request.Quantity,
                    eventType,
                    report.Status,
                    FillQuantity: 0m,
                    CumQuantity: ord.FilledQuantity,
                    LeavesQuantity: leavesQty,
                    Reason: !string.IsNullOrWhiteSpace(report.Reason) ? report.Reason : (ord.RejectReason ?? string.Empty),
                    OrderTag: ord.Request.OrderTag);
            }
        }

        if (shouldDispatch)
        {
            OnOrderStatusChanged?.Invoke(report.OrderId, report.Status);
            if (orderEvt != null)
            {
                OnOrderEvent?.Invoke(orderEvt);
            }
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

        // 市价单（Price <= 0.0）：尝试从绑定的网关中查询当前标的公允参考市价（时态自适应）
        double marketPrice = 0.0;
        if (_gateway is ISimulatedExchange sim)
        {
            var bar = sim.GetBar(request.Symbol);
            marketPrice = _config.DefaultExecutionPrice switch
            {
                ExecutionPriceType.Open => bar.OpenOr(bar.CloseOr(0.0)),
                ExecutionPriceType.Vwap => (bar.Vwap.HasValue && bar.Vwap.Value > 0.0)
                    ? bar.Vwap.Value
                    : bar.CloseOr(bar.OpenOr(0.0)),
                ExecutionPriceType.Average => (bar.HasOpen && bar.HasHigh && bar.HasLow && bar.HasClose)
                    ? (bar.Open + bar.High + bar.Low + bar.Close) / 4.0
                    : bar.CloseOr(bar.OpenOr(0.0)),
                _ => bar.CloseOr(0.0)
            };
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
            _pendingConditionals.Clear();
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
                    if (record.Status == OrderStatus.Pending && IsConditional(record.Request.Type))
                    {
                        _pendingConditionals[s.OrderId] = record;
                    }
                }
            }
        }
    }
}

/// <summary>
/// 经纪商内部在途订单生命周期记录。
/// </summary>
internal sealed class BrokerOrderRecord
{
    public long OrderId { get; }
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

    public BrokerOrderRecord(long orderId, OrderRequest request)
    {
        OrderId = orderId;
        Request = request;
    }
}
