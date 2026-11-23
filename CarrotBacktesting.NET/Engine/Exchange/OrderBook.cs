using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Engine.Exchange;

/// <summary>
/// 标的桶化时间价格优先挂单簿（Price-Time Priority OrderBook）。
/// 专职负责在途未成交订单的内存排序存储、检索、撮合切片与快速撤销。
/// 严格遵循物理交易所规则：
/// 1. 买单（Bids）：市价优先 > 价格降序（高价先买） > 时间升序（先来先到）；
/// 2. 卖单（Asks）：市价优先 > 价格升序（低价先卖） > 时间升序（先来先到）；
/// 采用二分查找插入保序，内存连续紧凑，热路径零 GC 分配；桶空自动注销，杜绝内存泄漏。
/// </summary>
internal sealed class OrderBook
{
    private readonly Dictionary<string, SymbolPendingQueue> _pendingBySymbol = new(StringComparer.Ordinal);
    private readonly Dictionary<long, SimulatedOrder> _ordersById = new();

    /// <summary>
    /// 当前挂单簿中的总活跃挂单数。
    /// </summary>
    public int Count => _ordersById.Count;

    /// <summary>
    /// 当前有待撮合挂单的所有标的代码集合。
    /// </summary>
    public IEnumerable<string> ActiveSymbols => _pendingBySymbol.Keys;

    /// <summary>
    /// 将新的挂单按时间价格优先级插入对应标的的队列中。
    /// </summary>
    public void Add(SimulatedOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        _ordersById[order.OrderId] = order;

        if (!_pendingBySymbol.TryGetValue(order.Request.Symbol, out var queue))
        {
            queue = new SymbolPendingQueue();
            _pendingBySymbol[order.Request.Symbol] = queue;
        }
        queue.Add(order);
    }

    /// <summary>
    /// 尝试按订单 ID 获取挂单对象。
    /// </summary>
    public bool TryGet(long orderId, out SimulatedOrder? order)
    {
        return _ordersById.TryGetValue(orderId, out order);
    }

    /// <summary>
    /// 从挂单簿中移除指定订单（成交或撤单）。若对应标的桶清空，则及时注销字典键。
    /// </summary>
    public bool Remove(long orderId, out SimulatedOrder? removedOrder)
    {
        if (_ordersById.Remove(orderId, out removedOrder))
        {
            string symbol = removedOrder.Request.Symbol;
            if (_pendingBySymbol.TryGetValue(symbol, out var queue))
            {
                queue.Remove(removedOrder);
                if (queue.Count == 0)
                {
                    _pendingBySymbol.Remove(symbol);
                }
            }
            return true;
        }

        removedOrder = null;
        return false;
    }

    /// <summary>
    /// 获取指定标的当前按时间价格优先排队的全部活跃挂单（先买盘后卖盘）。
    /// 若无挂单返回 null。
    /// </summary>
    public IReadOnlyList<SimulatedOrder>? GetPendingOrders(string symbol)
    {
        return _pendingBySymbol.TryGetValue(symbol, out var queue) && queue.Count > 0
            ? queue.GetAllSorted()
            : null;
    }

    /// <summary>
    /// 获取指定标的当前按价格降序、时间升序排队的买盘队列。
    /// </summary>
    public IReadOnlyList<SimulatedOrder>? GetPendingBids(string symbol)
    {
        return _pendingBySymbol.TryGetValue(symbol, out var queue) && queue.Bids.Count > 0
            ? queue.Bids
            : null;
    }

    /// <summary>
    /// 获取指定标的当前按价格升序、时间升序排队的卖盘队列。
    /// </summary>
    public IReadOnlyList<SimulatedOrder>? GetPendingAsks(string symbol)
    {
        return _pendingBySymbol.TryGetValue(symbol, out var queue) && queue.Asks.Count > 0
            ? queue.Asks
            : null;
    }

    /// <summary>
    /// 获取当前挂单簿中所有排队挂单。
    /// </summary>
    public IEnumerable<SimulatedOrder> GetAllOrders() => _ordersById.Values;

    /// <summary>
    /// 清空挂单簿中的所有挂单。
    /// </summary>
    public void Clear()
    {
        _ordersById.Clear();
        _pendingBySymbol.Clear();
    }

    /// <summary>
    /// 撤销所有当日有效（TimeInForce.Day）的限价排队挂单并移出挂单簿，保留 GTC 挂单与市价撮合指令。
    /// </summary>
    /// <returns>被撤销的挂单列表。</returns>
    public List<SimulatedOrder> CancelDayOrders()
    {
        if (_ordersById.Count == 0)
        {
            return new List<SimulatedOrder>(0);
        }

        var canceled = new List<SimulatedOrder>();
        foreach (var pair in _ordersById)
        {
            // 依据交易所物理规则，日切清算撤销所有未成交的日内排队限价单（非市价且 TimeInForce.Day，涵盖 Limit 与触发后排队的 StopLimit / TakeProfitLimit）
            if (pair.Value.TimeInForce == TimeInForce.Day && pair.Value.Request.Type != OrderType.Market)
            {
                canceled.Add(pair.Value);
            }
        }

        if (canceled.Count == 0)
        {
            return canceled;
        }

        for (int i = 0; i < canceled.Count; i++)
        {
            _ordersById.Remove(canceled[i].OrderId);
        }

        List<string>? emptySymbols = null;
        foreach (var (symbol, queue) in _pendingBySymbol)
        {
            queue.RemoveAll(o => o.TimeInForce == TimeInForce.Day && o.Request.Type != OrderType.Market);
            if (queue.Count == 0)
            {
                emptySymbols ??= new List<string>();
                emptySymbols.Add(symbol);
            }
        }

        if (emptySymbols != null)
        {
            for (int i = 0; i < emptySymbols.Count; i++)
            {
                _pendingBySymbol.Remove(emptySymbols[i]);
            }
        }

        return canceled;
    }
}

/// <summary>
/// 单标的买卖双侧时间价格优先排队桶。
/// </summary>
internal sealed class SymbolPendingQueue
{
    private readonly List<SimulatedOrder> _bids = new();
    private readonly List<SimulatedOrder> _asks = new();
    private List<SimulatedOrder>? _cachedMerged;

    public IReadOnlyList<SimulatedOrder> Bids => _bids;
    public IReadOnlyList<SimulatedOrder> Asks => _asks;
    public int Count => _bids.Count + _asks.Count;

    public void Add(SimulatedOrder order)
    {
        _cachedMerged = null;
        if (order.Request.IsBuy)
        {
            int idx = _bids.BinarySearch(order, BidComparer.Instance);
            if (idx < 0) idx = ~idx;
            _bids.Insert(idx, order);
        }
        else
        {
            int idx = _asks.BinarySearch(order, AskComparer.Instance);
            if (idx < 0) idx = ~idx;
            _asks.Insert(idx, order);
        }
    }

    public bool Remove(SimulatedOrder order)
    {
        _cachedMerged = null;
        return order.Request.IsBuy ? _bids.Remove(order) : _asks.Remove(order);
    }

    public void RemoveAll(Predicate<SimulatedOrder> match)
    {
        _cachedMerged = null;
        _bids.RemoveAll(match);
        _asks.RemoveAll(match);
    }

    public IReadOnlyList<SimulatedOrder> GetAllSorted()
    {
        if (_cachedMerged != null) return _cachedMerged;
        var list = new List<SimulatedOrder>(_bids.Count + _asks.Count);
        list.AddRange(_bids);
        list.AddRange(_asks);
        _cachedMerged = list;
        return list;
    }
}

/// <summary>
/// 买单价格-时间比较器：市价优先 > 价格降序（出价越高越优先） > 提交时间升序 > OrderId 升序。
/// </summary>
internal sealed class BidComparer : IComparer<SimulatedOrder>
{
    public static readonly BidComparer Instance = new();

    public int Compare(SimulatedOrder? x, SimulatedOrder? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        // 1. 市价单优先于限价单
        bool xMarket = x.Request.Type == OrderType.Market;
        bool yMarket = y.Request.Type == OrderType.Market;
        if (xMarket && !yMarket) return -1;
        if (!xMarket && yMarket) return 1;

        // 2. 价格优先：出价高者优先（降序）
        int priceCmp = y.Request.Price.CompareTo(x.Request.Price);
        if (priceCmp != 0) return priceCmp;

        // 3. 时间优先：时间早优先（升序）
        int timeCmp = x.SubmitTime.CompareTo(y.SubmitTime);
        if (timeCmp != 0) return timeCmp;

        return x.OrderId.CompareTo(y.OrderId);
    }
}

/// <summary>
/// 卖单价格-时间比较器：市价优先 > 价格升序（售价越低越优先） > 提交时间升序 > OrderId 升序。
/// </summary>
internal sealed class AskComparer : IComparer<SimulatedOrder>
{
    public static readonly AskComparer Instance = new();

    public int Compare(SimulatedOrder? x, SimulatedOrder? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        // 1. 市价单优先于限价单
        bool xMarket = x.Request.Type == OrderType.Market;
        bool yMarket = y.Request.Type == OrderType.Market;
        if (xMarket && !yMarket) return -1;
        if (!xMarket && yMarket) return 1;

        // 2. 价格优先：售价低者优先（升序）
        int priceCmp = x.Request.Price.CompareTo(y.Request.Price);
        if (priceCmp != 0) return priceCmp;

        // 3. 时间优先：时间早优先（升序）
        int timeCmp = x.SubmitTime.CompareTo(y.SubmitTime);
        if (timeCmp != 0) return timeCmp;

        return x.OrderId.CompareTo(y.OrderId);
    }
}

/// <summary>
/// 挂单实体模型。承载订单意图与仿真撮合计算所需参数。
/// </summary>
internal sealed class SimulatedOrder
{
    public long OrderId { get; }
    public OrderRequest Request { get; }
    public TimeInForce TimeInForce => Request.TimeInForce ?? TimeInForce.Day;
    public MarketTime SubmitTime { get; }
    public OrderStatus Status { get; set; } = OrderStatus.Submitted;

    public double FrozenCash { get; set; }
    public decimal FrozenPosition { get; set; }
    public decimal CloseQuantity { get; set; }
    public decimal OpenQuantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public double Multiplier { get; set; }
    public double MarginRate { get; set; }
    public int SettleDays { get; set; }

    public SimulatedOrder(long orderId, OrderRequest request, MarketTime submitTime)
    {
        OrderId = orderId;
        Request = request;
        SubmitTime = submitTime;
    }
}
