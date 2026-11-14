using System;
using System.Collections.Generic;
using System.Threading;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Broker;

/// <summary>
/// 客户端权威经纪商（Broker）。
/// 面向策略层的统一入口，统领本地资产账本、多空持仓均价、两阶段预冻结与订单生命周期状态机。
/// 严格基于依赖倒置：通过通用接口 IExchangeGateway 路由订单，回测与实盘 100% 共享完全一致的单一内核代码。
/// </summary>
public sealed class Broker : IBroker
{
    private readonly IExchangeGateway _gateway;
    private readonly BrokerConfig _config;
    private readonly IFieldRegistry? _fields;
    private readonly TimeZoneInfo _timeZone;
    private readonly AccountLedger _ledger;
    private readonly object _syncLock = new();

    private readonly Dictionary<string, BrokerOrderRecord> _orders = new(StringComparer.Ordinal);
    private long _orderSequence = 0;
    private DateTime _currentTime = DateTime.MinValue;

    public event Action<string, OrderStatus>? OnOrderStatusChanged;
    public event Action<ExecutionReport>? OnTrade;
    public event Action<PositionEvent>? OnPositionChanged;

    /// <summary>
    /// 获取当前挂载的交易所网关实例。
    /// </summary>
    public IExchangeGateway Gateway => _gateway;

    /// <summary>
    /// 获取经纪商当前推进的全局时间戳。
    /// </summary>
    public DateTime CurrentTime => _currentTime;

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
            return _ledger.GetTotalEquity(sym =>
            {
                double p = priceResolver?.Invoke(sym) ?? 0.0;
                if (p > 0.0 && !double.IsNaN(p) && !double.IsInfinity(p)) return p;
                if (_gateway is ISimulatedExchange sim)
                {
                    p = sim.GetMarketPrice(sym);
                    if (p > 0.0 && !double.IsNaN(p) && !double.IsInfinity(p)) return p;
                }
                return 0.0;
            });
        }
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

        string orderId = string.IsNullOrWhiteSpace(request.ClientOrderId)
            ? $"ord_{Interlocked.Increment(ref _orderSequence)}"
            : request.ClientOrderId;

        double multiplier = GetMultiplier(request.Symbol);
        double marginRate = GetMarginRate(request.Symbol);
        int settleDays = GetSettleDays(request.Symbol);

        bool freezeOk;
        string? rejectReason;

        lock (_syncLock)
        {
            double estimatedPrice = request.Price > 0.0 ? request.Price : 0.0;
            freezeOk = _ledger.TryFreeze(
                request,
                estimatedPrice,
                multiplier,
                marginRate,
                out var closeQty,
                out var openQty,
                out var frozenCash,
                out var frozenPosition,
                out rejectReason);

            var record = new BrokerOrderRecord(orderId, request)
            {
                Multiplier = multiplier,
                MarginRate = marginRate,
                SettleDays = settleDays,
                CloseQuantity = closeQty,
                OpenQuantity = openQty,
                RemainingCloseQuantity = closeQty,
                RemainingOpenQuantity = openQty,
                InitialFrozenCash = frozenCash,
                RemainingFrozenCash = frozenCash,
                InitialFrozenPosition = frozenPosition,
                RemainingFrozenPosition = frozenPosition,
                Status = freezeOk ? OrderStatus.Submitted : OrderStatus.Rejected,
                RejectReason = rejectReason
            };

            _orders[orderId] = record;
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
    public void CancelOrder(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        // 严格在锁外向网关发送撤单请求（撤单成功后由 OnOrderStateChanged 回调驱动解冻）
        _gateway.CancelOrder(orderId);
    }

    /// <inheritdoc />
    public void Step(DateTime currentTime)
    {
        lock (_syncLock)
        {
            _currentTime = currentTime;
            if (_gateway is ISimulatedExchange sim)
            {
                sim.SetCurrentTime(currentTime);
            }
            DateTime exchangeDate = ToExchangeDate(currentTime);
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

            DateTime exchangeDate = ToExchangeDate(report.Time);
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
                out posEvent);
        }

        // 锁外派发状态、成交与持仓变更事件（Open Calls）
        if (shouldDispatchState)
        {
            OnOrderStatusChanged?.Invoke(report.OrderId, newStatus);
        }
        OnTrade?.Invoke(report);
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

    private DateTime ToExchangeDate(DateTime time)
    {
        if (_timeZone == TimeZoneInfo.Utc)
        {
            return time.Date;
        }
        DateTime utcTime = time.Kind == DateTimeKind.Utc
            ? time
            : (time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc));
        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, _timeZone).Date;
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
