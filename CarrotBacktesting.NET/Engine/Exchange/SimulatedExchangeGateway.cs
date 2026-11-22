using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Exchange;

/// <summary>
/// 纯粹的回测仿真交易所网关。
/// 实现 IExchangeGateway 与 ISimulatedExchange，专管仿真撮合、挂单簿维护与时间步时钟驱动。
/// 杜绝任何账户、资金、持仓逻辑，纯粹作为外部物理交易所通道的仿真数字孪生。
/// </summary>
public sealed class SimulatedExchangeGateway : ISimulatedExchange, ICheckpointable<ExchangeSnapshot>
{
    private readonly BrokerConfig _config;
    private readonly IFieldRegistry? _fields;
    private readonly OrderBook _orderBook = new();
    private readonly MatchingEngine _matcher;
    private readonly Dictionary<string, BarSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly object _syncLock = new();

    private MarketTime _currentTime = MarketTime.MinValue;
    private int _currentIndex = -1;

    private IMarketDataProvider? _marketData;

    public event Action<ExecutionReport>? OnExecution;
    public event Action<OrderStateReport>? OnOrderStateChanged;

    public SimulatedExchangeGateway(BrokerConfig config, IFieldRegistry? fields = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _fields = fields;
        _matcher = new MatchingEngine(config);
    }

    /// <inheritdoc />
    public void SendOrder(long orderId, OrderRequest request)
    {
        if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), orderId, "OrderId 必须为正整数。");
        ArgumentNullException.ThrowIfNull(request);

        ExecutionReport? fillReport = null;
        OrderStatus? stateChanged = null;
        string? stateReason = null;

        lock (_syncLock)
        {
            // 无行情/未上市标的守卫拦截（第一性原理：当前时间步无合法价格数据，不接受新申报）：
            if (_marketData != null && _marketData.Timeseries.HasField(StandardFields.Close) && _currentIndex >= 0 && !_snapshots.ContainsKey(request.Symbol))
            {
                var bar = _marketData.GetBar(request.Symbol, _currentIndex);
                if (!bar.IsValid)
                {
                    stateChanged = OrderStatus.Rejected;
                    stateReason = $"Symbol '{request.Symbol}' has no valid market data on current step {_currentIndex}.";
                }
            }

            if (!stateChanged.HasValue)
            {
                double multiplier = GetMultiplier(request.Symbol);
                MarketTime tradeTime = _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch;

                // 市价单处理：若处于回测时钟推进中且非 Close 即时成交模式，统一压入 OrderBook 挂单簿，留待对应相位撮合
                bool isPendingMarketOrder = request.Type == OrderType.Market && _marketData != null && _currentIndex >= 0 && _config.DefaultExecutionPrice != ExecutionPriceType.Close;
                if (isPendingMarketOrder)
                {
                    var simOrder = new SimulatedOrder(orderId, request, tradeTime)
                    {
                        Multiplier = multiplier,
                        MarginRate = GetMarginRate(request.Symbol),
                        SettleDays = GetSettleDays(request.Symbol)
                    };
                    _orderBook.Add(simOrder);
                    stateChanged = OrderStatus.Submitted;
                }
                else
                {
                    double basePrice = GetBasePriceForOrder(request);
                    BarSnapshot currentBar = GetBar(request.Symbol);

                    var matchResult = _matcher.EvaluateImmediateOrder(request, basePrice, in currentBar, multiplier, tradeTime);

                    if (matchResult.IsFilled)
                    {
                        // 即时成交（市价单或已穿透限价的限价单）：
                        // 若公允市价未知，记录未含滑点的基准市价 basePrice，严禁将带滑点的 FillPrice 写入快照
                        if (!_snapshots.ContainsKey(request.Symbol) && basePrice > 0.0)
                        {
                            _snapshots[request.Symbol] = new BarSnapshot(close: basePrice);
                        }

                        fillReport = new ExecutionReport(
                            orderId,
                            request.Symbol,
                            matchResult.FillPrice,
                            matchResult.FillQuantity,
                            matchResult.Commission,
                            tradeTime,
                            CumQuantity: matchResult.FillQuantity,
                            LeavesQuantity: 0m,
                            Role: matchResult.Role,
                            Slippage: matchResult.Slippage);
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
                        stateReason = matchResult.RejectReason;
                    }
                }
            }
        }

        // 严格在锁外派发物理回报事件
        if (stateChanged.HasValue)
        {
            decimal leaves = stateChanged.Value == OrderStatus.Submitted ? request.Quantity : 0m;
            var stateReport = new OrderStateReport(
                orderId,
                stateChanged.Value,
                _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch,
                request.Symbol,
                stateReason ?? string.Empty,
                LeavesQuantity: leaves);
            OnOrderStateChanged?.Invoke(stateReport);
        }
        if (fillReport != null)
        {
            OnExecution?.Invoke(fillReport);
        }
    }

    /// <inheritdoc />
    public void CancelOrder(long orderId)
    {
        if (orderId <= 0) return;
        bool removed;
        SimulatedOrder? removedOrder;

        lock (_syncLock)
        {
            removed = _orderBook.Remove(orderId, out removedOrder);
        }

        if (removed && removedOrder != null)
        {
            var eventTime = _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch;
            var cancelReport = new OrderStateReport(
                orderId,
                OrderStatus.Canceled,
                eventTime,
                removedOrder.Request.Symbol,
                "Order canceled by user request.",
                LeavesQuantity: Math.Max(0m, removedOrder.Request.Quantity - removedOrder.FilledQuantity));
            OnOrderStateChanged?.Invoke(cancelReport);
        }
    }

    /// <inheritdoc />
    public void Step(MarketTime currentTime, IMarketDataProvider marketData, int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(marketData);
        EnsureMarketDataBound(marketData);

        List<ExecutionReport>? fillReports = null;
        List<OrderStateReport>? stateReports = null;

        lock (_syncLock)
        {
            bool isNewDay = _currentTime != MarketTime.MinValue && currentTime.Date != _currentTime.Date;
            _currentTime = currentTime;
            _currentIndex = currentIndex;

            // 跨日时自动撤销所有当日有效（Day）的挂单，保留 GTC 挂单
            if (isNewDay && _orderBook.Count > 0)
            {
                var dayOrders = _orderBook.CancelDayOrders();
                if (dayOrders.Count > 0)
                {
                    stateReports = new List<OrderStateReport>(dayOrders.Count);
                    for (int i = 0; i < dayOrders.Count; i++)
                    {
                        var ord = dayOrders[i];
                        decimal leaves = Math.Max(0m, ord.Request.Quantity - ord.FilledQuantity);
                        stateReports.Add(new OrderStateReport(
                            ord.OrderId,
                            OrderStatus.Canceled,
                            currentTime,
                            ord.Request.Symbol,
                            "Day order canceled on daily rollover.",
                            leaves));
                    }
                }
            }

            if (_orderBook.Count > 0)
            {
                var activeSymbols = new List<string>(_orderBook.ActiveSymbols);
                for (int i = 0; i < activeSymbols.Count; i++)
                {
                    string symbol = activeSymbols[i];
                    BarSnapshot bar = _marketData!.GetBar(symbol, currentIndex);

                    // 1. 单一事实来源：当前 Bar 无任何合法行情数据（物理缺失 NaN），触发挂单清理与资金解冻
                    if (!bar.IsValid)
                    {
                        var pending = _orderBook.GetPendingOrders(symbol);
                        if (pending != null && pending.Count > 0)
                        {
                            var suspendedOrders = new List<SimulatedOrder>(pending);
                            for (int s = 0; s < suspendedOrders.Count; s++)
                            {
                                var ord = suspendedOrders[s];
                                _orderBook.Remove(ord.OrderId, out _);
                                decimal leaves = Math.Max(0m, ord.Request.Quantity - ord.FilledQuantity);
                                stateReports ??= new List<OrderStateReport>();
                                stateReports.Add(new OrderStateReport(
                                    ord.OrderId,
                                    OrderStatus.Rejected,
                                    currentTime,
                                    ord.Request.Symbol,
                                    $"Symbol '{symbol}' lacks valid market data on step {currentIndex}.",
                                    leaves));
                            }
                        }
                        continue;
                    }

                    // 2. 撮合基准价根据当前执行模式与 Bar 状态自洽供给，绝不偷看未来 Close，也绝不隐式兜底 0.0：
                    double benchmarkPrice = _config.DefaultExecutionPrice switch
                    {
                        ExecutionPriceType.Open => bar.OpenOr(bar.CloseOr(double.NaN)),
                        ExecutionPriceType.Close => bar.CloseOr(bar.OpenOr(double.NaN)),
                        _ => bar.CloseOr(bar.OpenOr(double.NaN))
                    };

                    if (double.IsNaN(benchmarkPrice) || benchmarkPrice <= 0.0 || double.IsInfinity(benchmarkPrice))
                    {
                        continue;
                    }

                    int colIndex = _marketData.Metadata.GetSymbolIndex(symbol);
                    MatchSymbolOrders(symbol, benchmarkPrice, in bar, currentTime, ref fillReports, ref stateReports, colIndex, currentIndex);
                }
            }
        }

        // 锁外派发成交回报、拒绝及跨日自动撤单事件
        DispatchReports(fillReports, stateReports);
    }

    /// <inheritdoc />
    public void Step(MarketTime currentTime, ITimeseriesRegistry timeseries, int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(timeseries);
        if (_marketData == null || !ReferenceEquals(_marketData.Timeseries, timeseries))
        {
            _marketData = new CarrotBacktesting.NET.Data.MarketDataProvider(timeseries);
        }
        Step(currentTime, _marketData, currentIndex);
    }

    /// <inheritdoc />
    public void SetCurrentTime(MarketTime currentTime)
    {
        lock (_syncLock)
        {
            _currentTime = currentTime;
        }
    }

    /// <inheritdoc />
    public void OnMarketData(string symbol, MarketTime time, in BarSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !snapshot.IsValid)
        {
            return;
        }

        List<ExecutionReport>? fillReports = null;
        List<OrderStateReport>? stateReports = null;

        lock (_syncLock)
        {
            MarketTime tradeTime = time != MarketTime.MinValue
                ? time
                : (_currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch);
            _currentTime = tradeTime;

            _snapshots[symbol] = snapshot;

            double matchPrice = snapshot.CloseOr(snapshot.OpenOr(double.NaN));
            if (!double.IsNaN(matchPrice) && matchPrice > 0.0)
            {
                MatchSymbolOrders(symbol, matchPrice, in snapshot, tradeTime, ref fillReports, ref stateReports);
            }
        }

        // 锁外派发成交回报与拒绝事件
        DispatchReports(fillReports, stateReports);
    }

    private void MatchSymbolOrders(
        string symbol,
        double marketPrice,
        in BarSnapshot bar,
        MarketTime tradeTime,
        ref List<ExecutionReport>? fillReports,
        ref List<OrderStateReport>? stateReports,
        int colIndex = -1,
        int currentIndex = -1)
    {
        var pendingList = _orderBook.GetPendingOrders(symbol);
        if (pendingList == null || pendingList.Count == 0)
        {
            return;
        }

        // 复制一份快照避免遍历时从 OrderBook 移除发生集合修改冲突
        var ordersToMatch = new List<SimulatedOrder>(pendingList);
        for (int i = 0; i < ordersToMatch.Count; i++)
        {
            var order = ordersToMatch[i];
            MatchResult matchResult;

            if (order.Request.Type == OrderType.Market)
            {
                // 市价跨步单按 ExecutionPriceType 撮合
                double execPrice;
                switch (_config.DefaultExecutionPrice)
                {
                    case ExecutionPriceType.Open:
                        execPrice = bar.HasOpen && bar.Open > 0.0 ? bar.Open : marketPrice;
                        break;
                    case ExecutionPriceType.Vwap:
                        if (_marketData != null && currentIndex >= 0)
                        {
                            double v = colIndex >= 0 ? _marketData.GetField(colIndex, "vwap", currentIndex) : _marketData.GetField(symbol, "vwap", currentIndex);
                            if (v > 0.0 && !double.IsNaN(v) && !double.IsInfinity(v))
                            {
                                execPrice = v;
                                break;
                            }
                        }
                        if (bar.Vwap.HasValue && bar.Vwap.Value > 0.0)
                        {
                            execPrice = bar.Vwap.Value;
                            break;
                        }
                        throw new InvalidDataException(
                            $"标的 '{symbol}' 在时间步 {currentIndex} 配置为 Vwap 撮合，但时序数据源缺失 'vwap' 列且无法通过 amount/volume 计算。");

                    case ExecutionPriceType.Average:
                        double o = bar.OpenOr(marketPrice);
                        double h = bar.HighOr(marketPrice);
                        double l = bar.LowOr(marketPrice);
                        double c = bar.CloseOr(marketPrice);
                        execPrice = (o + h + l + c) / 4.0;
                        break;

                    case ExecutionPriceType.Close:
                    default:
                        execPrice = marketPrice;
                        break;
                }

                matchResult = _matcher.EvaluateImmediateOrder(order.Request, execPrice, in bar, order.Multiplier, tradeTime);
            }
            else
            {
                // 限价单在挂单簿被动撮合（享受开盘跳空价格改善）
                matchResult = _matcher.EvaluatePendingLimitOrder(order, marketPrice, in bar, tradeTime);
            }

            if (matchResult.IsFilled)
            {
                order.FilledQuantity += matchResult.FillQuantity;
                decimal leavesQuantity = Math.Max(0m, order.Request.Quantity - order.FilledQuantity);
                if (leavesQuantity == 0m)
                {
                    _orderBook.Remove(order.OrderId, out _);
                }

                fillReports ??= new List<ExecutionReport>();
                fillReports.Add(new ExecutionReport(
                    order.OrderId,
                    order.Request.Symbol,
                    matchResult.FillPrice,
                    matchResult.FillQuantity,
                    matchResult.Commission,
                    tradeTime,
                    CumQuantity: order.FilledQuantity,
                    LeavesQuantity: leavesQuantity,
                    Role: matchResult.Role,
                    Slippage: matchResult.Slippage));
            }
            else if (matchResult.IsRejected)
            {
                _orderBook.Remove(order.OrderId, out _);
                decimal leavesQuantity = Math.Max(0m, order.Request.Quantity - order.FilledQuantity);
                stateReports ??= new List<OrderStateReport>();
                stateReports.Add(new OrderStateReport(
                    order.OrderId,
                    OrderStatus.Rejected,
                    tradeTime,
                    order.Request.Symbol,
                    matchResult.RejectReason ?? "Order rejected by matching engine.",
                    leavesQuantity));
            }
        }
    }

    /// <inheritdoc />
    public void CloseGateway(MarketTime terminalTime)
    {
        List<OrderStateReport>? stateReports = null;
        lock (_syncLock)
        {
            var allOrders = new List<SimulatedOrder>(_orderBook.GetAllOrders());
            if (allOrders.Count > 0)
            {
                stateReports = new List<OrderStateReport>();
                for (int i = 0; i < allOrders.Count; i++)
                {
                    var ord = allOrders[i];
                    // 终局边界安全清理：撤销所有未完成市价单与当日到期 Day 订单；保留 GTC 限价单排队状态以支持检查点断点续跑
                    bool shouldCancel = ord.Request.Type == OrderType.Market || ord.Request.TimeInForce == TimeInForce.Day;
                    if (shouldCancel)
                    {
                        _orderBook.Remove(ord.OrderId, out _);
                        decimal leaves = Math.Max(0m, ord.Request.Quantity - ord.FilledQuantity);
                        stateReports.Add(new OrderStateReport(
                            ord.OrderId,
                            OrderStatus.Canceled,
                            terminalTime != MarketTime.MinValue ? terminalTime : MarketTime.UnixEpoch,
                            ord.Request.Symbol,
                            "Terminal cancellation on backtest completion.",
                            leaves));
                    }
                }
            }
        }
        DispatchReports(null, stateReports);
    }

    private void DispatchReports(
        List<ExecutionReport>? fillReports,
        List<OrderStateReport>? stateReports)
    {
        if (fillReports != null)
        {
            for (int i = 0; i < fillReports.Count; i++)
            {
                OnExecution?.Invoke(fillReports[i]);
            }
        }
        if (stateReports != null)
        {
            for (int i = 0; i < stateReports.Count; i++)
            {
                OnOrderStateChanged?.Invoke(stateReports[i]);
            }
        }
    }

    private double GetBasePriceForOrder(OrderRequest request)
    {
        var bar = GetBar(request.Symbol);
        double currentMarketPrice = bar.CloseOr(bar.OpenOr(double.NaN));

        if (request.Type is OrderType.Limit or OrderType.StopLimit or OrderType.TakeProfitLimit)
        {
            if (!double.IsNaN(currentMarketPrice) && currentMarketPrice > 0.0)
            {
                return currentMarketPrice;
            }
            return request.Price > 0.0 ? request.Price : double.NaN;
        }

        // 市价单：若显式指定了基准价格（例如在行情 Bar 上下单），优先使用显式价格
        if (request.Price > 0.0 && !double.IsNaN(request.Price) && !double.IsInfinity(request.Price))
        {
            return request.Price;
        }

        if (!double.IsNaN(currentMarketPrice) && currentMarketPrice > 0.0)
        {
            return currentMarketPrice;
        }

        return double.NaN;
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

    /// <inheritdoc />
    public BarSnapshot GetBar(string symbol)
    {
        lock (_syncLock)
        {
            if (_marketData != null && _currentIndex >= 0)
            {
                return _marketData.GetBar(symbol, _currentIndex);
            }

            return _snapshots.TryGetValue(symbol, out var snapshot) ? snapshot : BarSnapshot.Empty;
        }
    }

    /// <summary>
    /// 保存仿真交易所状态快照（价格字典与排队挂单）。
    /// </summary>
    public ExchangeSnapshot SaveState()
    {
        lock (_syncLock)
        {
            var prices = new Dictionary<string, double>(_snapshots.Count, StringComparer.Ordinal);
            foreach (var kvp in _snapshots)
            {
                if (kvp.Value.HasClose)
                {
                    prices[kvp.Key] = kvp.Value.Close;
                }
            }

            var orders = new List<SimulatedOrderSnapshot>();
            foreach (var ord in _orderBook.GetAllOrders())
            {
                orders.Add(new SimulatedOrderSnapshot(
                    ord.OrderId,
                    ord.Request,
                    ord.SubmitTime,
                    ord.Multiplier,
                    ord.MarginRate,
                    ord.SettleDays,
                    ord.FilledQuantity
                ));
            }
            return new ExchangeSnapshot(prices, orders);
        }
    }

    /// <summary>
    /// 基于不可变快照恢复仿真交易所状态。
    /// </summary>
    public void RestoreState(ExchangeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_syncLock)
        {
            _snapshots.Clear();
            foreach (var kvp in snapshot.MarketPrices)
            {
                _snapshots[kvp.Key] = new BarSnapshot(close: kvp.Value);
            }

            _orderBook.Clear();
            foreach (var snap in snapshot.PendingOrders)
            {
                var ord = new SimulatedOrder(snap.OrderId, snap.Request, snap.CreateTime)
                {
                    Multiplier = snap.Multiplier,
                    MarginRate = snap.MarginRate,
                    SettleDays = snap.SettleDays,
                    FilledQuantity = snap.FilledQuantity
                };
                _orderBook.Add(ord);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureMarketDataBound(IMarketDataProvider marketData)
    {
        if (ReferenceEquals(_marketData, marketData)) return;

        _marketData = marketData;

        // 强类型前置校验：若配置了自定义流动性 Hook，但行情源无 volume 矩阵，及早 Fail-Fast 阻断
        if (_config.OnCheckVolume != null && !marketData.Timeseries.HasField(StandardFields.Volume))
        {
            throw new InvalidOperationException(
                "BrokerConfig 已挂载自定义 OnCheckVolume 成交量委托，但时序注册表中未找到 'volume' 矩阵。");
        }
    }
}
