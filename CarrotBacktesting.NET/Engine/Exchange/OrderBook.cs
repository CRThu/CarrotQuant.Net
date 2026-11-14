using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Engine.Exchange;

/// <summary>
/// 标的桶化 FIFO 挂单簿。
/// 专职负责在途未成交限价单的内存存储、检索与快速撤销。
/// 内部按 Symbol 分桶保持先来后到的正序队列，桶空自动注销，杜绝内存泄漏。
/// </summary>
internal sealed class OrderBook
{
    private readonly Dictionary<string, List<SimulatedOrder>> _pendingBySymbol = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SimulatedOrder> _ordersById = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前挂单簿中的总活跃挂单数。
    /// </summary>
    public int Count => _ordersById.Count;

    /// <summary>
    /// 当前有待撮合挂单的所有标的代码集合。
    /// </summary>
    public IEnumerable<string> ActiveSymbols => _pendingBySymbol.Keys;

    /// <summary>
    /// 将新的限价挂单压入对应标的的 FIFO 队列尾部。
    /// </summary>
    public void Add(SimulatedOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        _ordersById[order.OrderId] = order;

        if (!_pendingBySymbol.TryGetValue(order.Request.Symbol, out var list))
        {
            list = new List<SimulatedOrder>();
            _pendingBySymbol[order.Request.Symbol] = list;
        }
        list.Add(order);
    }

    /// <summary>
    /// 尝试按订单 ID 获取挂单对象。
    /// </summary>
    public bool TryGet(string orderId, out SimulatedOrder? order)
    {
        return _ordersById.TryGetValue(orderId, out order);
    }

    /// <summary>
    /// 从挂单簿中移除指定订单（成交或撤单）。若对应标的桶清空，则及时注销字典键。
    /// </summary>
    public bool Remove(string orderId, out SimulatedOrder? removedOrder)
    {
        if (_ordersById.Remove(orderId, out removedOrder))
        {
            string symbol = removedOrder.Request.Symbol;
            if (_pendingBySymbol.TryGetValue(symbol, out var list))
            {
                list.Remove(removedOrder);
                if (list.Count == 0)
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
    /// 获取指定标的当前按 FIFO 排队的在途挂单列表快照。
    /// 若无挂单返回 null。
    /// </summary>
    public IReadOnlyList<SimulatedOrder>? GetPendingOrders(string symbol)
    {
        return _pendingBySymbol.TryGetValue(symbol, out var list) && list.Count > 0 ? list : null;
    }

    /// <summary>
    /// 清空挂单簿中的所有挂单。
    /// </summary>
    public void Clear()
    {
        _ordersById.Clear();
        _pendingBySymbol.Clear();
    }
}

/// <summary>
/// 挂单实体模型。承载订单意图与仿真撮合计算所需参数。
/// </summary>
internal sealed class SimulatedOrder
{
    public string OrderId { get; }
    public OrderRequest Request { get; }
    public DateTime SubmitTime { get; }
    public OrderStatus Status { get; set; } = OrderStatus.Submitted;

    public double FrozenCash { get; set; }
    public decimal FrozenPosition { get; set; }
    public decimal CloseQuantity { get; set; }
    public decimal OpenQuantity { get; set; }
    public double Multiplier { get; set; }
    public double MarginRate { get; set; }
    public int SettleDays { get; set; }

    public SimulatedOrder(string orderId, OrderRequest request, DateTime submitTime)
    {
        OrderId = orderId;
        Request = request;
        SubmitTime = submitTime;
    }
}
