using System;
using System.Buffers;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Exchange;

/// <summary>
/// 纯粹的回测仿真交易所网关。
/// 实现 IExchangeGateway 与 ISimulatedExchange，专管仿真撮合、挂单簿维护与时间步时钟驱动。
/// 杜绝任何账户、资金、持仓逻辑，纯粹作为外部物理交易所通道的仿真数字孪生。
/// </summary>
public sealed class SimulatedExchangeGateway : ISimulatedExchange
{
    private readonly BrokerConfig _config;
    private readonly IFieldRegistry? _fields;
    private readonly OrderBook _orderBook = new();
    private readonly MatchingEngine _matcher;
    private readonly Dictionary<string, double> _marketPrices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IDataRow> _currentBars = new(StringComparer.Ordinal);
    private readonly object _syncLock = new();

    private DateTime _currentTime = DateTime.MinValue;

    public event Action<ExecutionReport>? OnExecution;
    public event Action<string, OrderStatus>? OnOrderStateChanged;

    public SimulatedExchangeGateway(BrokerConfig config, IFieldRegistry? fields = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _fields = fields;
        _matcher = new MatchingEngine(config);
    }

    /// <inheritdoc />
    public void SendOrder(string orderId, OrderRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(request);

        ExecutionReport? fillReport = null;
        OrderStatus? stateChanged = null;

        lock (_syncLock)
        {
            double basePrice = GetBasePriceForOrder(request);
            double multiplier = GetMultiplier(request.Symbol);
            DateTime tradeTime = _currentTime != DateTime.MinValue ? _currentTime : DateTime.UtcNow;

            _currentBars.TryGetValue(request.Symbol, out var currentBar);
            var matchResult = _matcher.EvaluateImmediateOrder(request, basePrice, currentBar, multiplier, tradeTime);

            if (matchResult.IsFilled)
            {
                // 即时成交（市价单或已穿透限价的限价单）
                _marketPrices[request.Symbol] = matchResult.FillPrice;
                fillReport = new ExecutionReport(
                    orderId,
                    request.Symbol,
                    matchResult.FillPrice,
                    matchResult.FillQuantity,
                    matchResult.Commission,
                    tradeTime);
            }
            else if (matchResult.IsPending)
            {
                // 未触碰价格的限价单，压入 OrderBook 排队
                var simOrder = new SimulatedOrder(orderId, request, tradeTime)
                {
                    Multiplier = multiplier,
                    MarginRate = GetMarginRate(request.Symbol),
                    SettleDays = GetSettleDays(request.Symbol)
                };
                _orderBook.Add(simOrder);
                stateChanged = OrderStatus.Submitted;
            }
            else if (matchResult.IsRejected)
            {
                stateChanged = OrderStatus.Rejected;
            }
        }

        // 严格在锁外派发物理回报事件
        if (stateChanged.HasValue)
        {
            OnOrderStateChanged?.Invoke(orderId, stateChanged.Value);
        }
        if (fillReport != null)
        {
            OnExecution?.Invoke(fillReport);
        }
    }

    /// <inheritdoc />
    public void CancelOrder(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        bool removed;

        lock (_syncLock)
        {
            removed = _orderBook.Remove(orderId, out _);
        }

        if (removed)
        {
            // 锁外派发已撤销事件
            OnOrderStateChanged?.Invoke(orderId, OrderStatus.Canceled);
        }
    }

    /// <inheritdoc />
    public void Step(DateTime currentTime, ITimeseriesRegistry timeseries, int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(timeseries);

        List<ExecutionReport>? fillReports = null;
        List<string>? rejectedOrders = null;

        lock (_syncLock)
        {
            _currentTime = currentTime;

            if (_orderBook.Count == 0)
            {
                return;
            }

            IReadOnlyMatrix2D<double>? closeMatrix = null;
            try
            {
                closeMatrix = timeseries.GetMatrix<double>("close");
            }
            catch
            {
                // 时序中可能不存在 close 字段
            }

            var activeSymbols = new List<string>(_orderBook.ActiveSymbols);
            foreach (var symbol in activeSymbols)
            {
                double marketPrice = 0.0;
                int colIndex = timeseries.Metadata.GetSymbolIndex(symbol);

                if (closeMatrix != null && colIndex >= 0 && currentIndex < closeMatrix.RowCount)
                {
                    double p = closeMatrix[currentIndex, colIndex];
                    if (p > 0.0 && !double.IsNaN(p) && !double.IsInfinity(p))
                    {
                        marketPrice = p;
                        _marketPrices[symbol] = p;
                    }
                }

                if (marketPrice <= 0.0 && _marketPrices.TryGetValue(symbol, out var cachedPrice))
                {
                    marketPrice = cachedPrice;
                }

                if (marketPrice <= 0.0)
                {
                    continue;
                }

                var pendingList = _orderBook.GetPendingOrders(symbol);
                if (pendingList == null || pendingList.Count == 0)
                {
                    continue;
                }

                // 复制一份快照避免遍历时从 OrderBook 移除发生集合修改冲突
                var ordersToMatch = new List<SimulatedOrder>(pendingList);
                for (int i = 0; i < ordersToMatch.Count; i++)
                {
                    var order = ordersToMatch[i];
                    var matchResult = _matcher.EvaluatePendingLimitOrder(order, marketPrice, null, currentTime);

                    if (matchResult.IsFilled)
                    {
                        _orderBook.Remove(order.OrderId, out _);
                        fillReports ??= new List<ExecutionReport>();
                        fillReports.Add(new ExecutionReport(
                            order.OrderId,
                            order.Request.Symbol,
                            matchResult.FillPrice,
                            matchResult.FillQuantity,
                            matchResult.Commission,
                            currentTime));
                    }
                    else if (matchResult.IsRejected)
                    {
                        _orderBook.Remove(order.OrderId, out _);
                        rejectedOrders ??= new List<string>();
                        rejectedOrders.Add(order.OrderId);
                    }
                }
            }
        }

        // 锁外派发成交回报与拒绝事件
        if (fillReports != null)
        {
            for (int i = 0; i < fillReports.Count; i++)
            {
                OnExecution?.Invoke(fillReports[i]);
            }
        }
        if (rejectedOrders != null)
        {
            for (int i = 0; i < rejectedOrders.Count; i++)
            {
                OnOrderStateChanged?.Invoke(rejectedOrders[i], OrderStatus.Rejected);
            }
        }
    }

    /// <inheritdoc />
    public double GetMarketPrice(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return 0.0;
        lock (_syncLock)
        {
            return _marketPrices.TryGetValue(symbol, out var p) ? p : 0.0;
        }
    }

    /// <inheritdoc />
    public void SetCurrentTime(DateTime currentTime)
    {
        lock (_syncLock)
        {
            _currentTime = currentTime;
        }
    }

    /// <inheritdoc />
    public void UpdateMarketPrice(string symbol, double price, IDataRow? bar = null)
    {
        if (string.IsNullOrWhiteSpace(symbol) || price <= 0.0 || double.IsNaN(price) || double.IsInfinity(price))
        {
            return;
        }

        List<ExecutionReport>? fillReports = null;
        DateTime tradeTime;

        lock (_syncLock)
        {
            _marketPrices[symbol] = price;
            if (bar != null)
            {
                _currentBars[symbol] = bar;
            }
            else
            {
                _currentBars.Remove(symbol);
            }

            tradeTime = _currentTime != DateTime.MinValue ? _currentTime : DateTime.UtcNow;

            var pendingList = _orderBook.GetPendingOrders(symbol);
            if (pendingList != null && pendingList.Count > 0)
            {
                var ordersToMatch = new List<SimulatedOrder>(pendingList);
                for (int i = 0; i < ordersToMatch.Count; i++)
                {
                    var order = ordersToMatch[i];
                    var matchResult = _matcher.EvaluatePendingLimitOrder(order, price, bar, tradeTime);

                    if (matchResult.IsFilled)
                    {
                        _orderBook.Remove(order.OrderId, out _);
                        fillReports ??= new List<ExecutionReport>();
                        fillReports.Add(new ExecutionReport(
                            order.OrderId,
                            order.Request.Symbol,
                            matchResult.FillPrice,
                            matchResult.FillQuantity,
                            matchResult.Commission,
                            tradeTime));
                    }
                }
            }
        }

        if (fillReports != null)
        {
            for (int i = 0; i < fillReports.Count; i++)
            {
                OnExecution?.Invoke(fillReports[i]);
            }
        }
    }

    private double GetBasePriceForOrder(OrderRequest request)
    {
        if (request.Type == OrderType.Limit)
        {
            if (_marketPrices.TryGetValue(request.Symbol, out var lp) && lp > 0.0)
            {
                return lp;
            }
            return request.Price > 0.0 ? request.Price : 0.0;
        }

        // 市价单：若显式指定了基准价格（例如在行情 Bar 上下单），优先使用显式价格
        if (request.Price > 0.0 && !double.IsNaN(request.Price) && !double.IsInfinity(request.Price))
        {
            return request.Price;
        }

        // 未显式指定价格时，查询行情缓存
        if (_marketPrices.TryGetValue(request.Symbol, out var p) && p > 0.0)
        {
            return p;
        }

        return 0.0;
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
