using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
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
    private readonly Dictionary<string, double> _marketPrices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IDataRow> _currentBars = new(StringComparer.Ordinal);
    private readonly object _syncLock = new();

    private MarketTime _currentTime = MarketTime.MinValue;
    private int _currentIndex = -1;

    // 热路径物理矩阵引用绑定（在 timeseries 引用不变时实现零字典寻址、零字符串哈希）
    private ITimeseriesRegistry? _boundTimeseries;
    private IMarketMetadata? _boundMetadata;
    private IReadOnlyMatrix2D<double>? _closeMatrix;
    private IReadOnlyMatrix2D<double>? _openMatrix;
    private IReadOnlyMatrix2D<double>? _highMatrix;
    private IReadOnlyMatrix2D<double>? _lowMatrix;
    private IReadOnlyMatrix2D<double>? _volumeMatrix;
    private IReadOnlyMatrix2D<double>? _amountMatrix;
    private IReadOnlyMatrix2D<double>? _vwapMatrix;

    public event Action<ExecutionReport>? OnExecution;
    public event Action<OrderStateReport>? OnOrderStateChanged;

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
        string? stateReason = null;

        lock (_syncLock)
        {
            // 停牌 / 未上市标的守卫拦截：
            // 若处于时序推进中（矩阵已绑定且时步有效），但标的在当前时间步无合法公允行情（NaN、<= 0 或 Infinity）
            // 且外部未提供当期显式 Bar，说明标的处于停牌、未上市或退市状态，立即拒单，严禁入簿挂单
            if (_closeMatrix != null && _boundMetadata != null && _currentIndex >= 0 && !_currentBars.ContainsKey(request.Symbol))
            {
                int col = _boundMetadata.GetSymbolIndex(request.Symbol);
                double rawClose = (col >= 0 && _currentIndex < _closeMatrix.RowCount) ? _closeMatrix[_currentIndex, col] : double.NaN;
                if (double.IsNaN(rawClose) || rawClose <= 0.0 || double.IsInfinity(rawClose))
                {
                    stateChanged = OrderStatus.Rejected;
                    stateReason = $"Symbol '{request.Symbol}' is suspended, unlisted, or lacks valid market data.";
                }
            }

            if (!stateChanged.HasValue)
            {
                double multiplier = GetMultiplier(request.Symbol);
                MarketTime tradeTime = _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch;

                // 市价单处理：若处于回测时钟推进中且非 Close 即时成交模式，统一压入 OrderBook 挂单簿，留待对应相位撮合
                bool isPendingMarketOrder = request.Type == OrderType.Market && _boundTimeseries != null && _currentIndex >= 0 && _config.DefaultExecutionPrice != ExecutionPriceType.Close;
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
                    BarSnapshot currentBar = BarSnapshot.Empty;
                    if (_currentBars.TryGetValue(request.Symbol, out var explicitBar))
                    {
                        currentBar = BarSnapshot.FromDataRow(explicitBar, basePrice);
                    }
                    else if (_closeMatrix != null && _currentIndex >= 0)
                    {
                        currentBar = GetBarSnapshot(request.Symbol, _currentIndex);
                    }

                    var matchResult = _matcher.EvaluateImmediateOrder(request, basePrice, in currentBar, multiplier, tradeTime);

                    if (matchResult.IsFilled)
                    {
                        // 即时成交（市价单或已穿透限价的限价单）：
                        // 若公允市价未知，记录未含滑点的基准市价 basePrice，严禁将带滑点的 FillPrice 写入 _marketPrices
                        if (!_marketPrices.ContainsKey(request.Symbol) && basePrice > 0.0)
                        {
                            _marketPrices[request.Symbol] = basePrice;
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
                            Role: matchResult.Role);
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
                request.ClientOrderId,
                LeavesQuantity: leaves);
            OnOrderStateChanged?.Invoke(stateReport);
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
                removedOrder.Request.ClientOrderId,
                LeavesQuantity: Math.Max(0m, removedOrder.Request.Quantity - removedOrder.FilledQuantity));
            OnOrderStateChanged?.Invoke(cancelReport);
        }
    }

    /// <inheritdoc />
    public void Step(MarketTime currentTime, ITimeseriesRegistry timeseries, int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(timeseries);

        List<ExecutionReport>? fillReports = null;
        List<OrderStateReport>? stateReports = null;

        lock (_syncLock)
        {
            bool isNewDay = _currentTime != MarketTime.MinValue && currentTime.Date != _currentTime.Date;
            _currentTime = currentTime;
            _currentIndex = currentIndex;
            EnsureTimeseriesBound(timeseries);

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
                            ord.Request.ClientOrderId,
                            leaves));
                    }
                }
            }

            if (_orderBook.Count > 0)
            {
                if (_closeMatrix == null)
                {
                    throw new InvalidOperationException("时序注册表中缺少必选矩阵 'close'，无法获取基准行情与执行仿真撮合。");
                }

                var activeSymbols = new List<string>(_orderBook.ActiveSymbols);
                for (int i = 0; i < activeSymbols.Count; i++)
                {
                    string symbol = activeSymbols[i];
                    double marketPrice = 0.0;
                    int colIndex = _boundMetadata != null ? _boundMetadata.GetSymbolIndex(symbol) : -1;

                    double close = (colIndex >= 0 && currentIndex < _closeMatrix.RowCount) ? _closeMatrix[currentIndex, colIndex] : double.NaN;
                    if (close > 0.0 && !double.IsNaN(close) && !double.IsInfinity(close))
                    {
                        marketPrice = close;
                        _marketPrices[symbol] = close;
                    }

                    double open = (colIndex >= 0 && _openMatrix != null && currentIndex < _openMatrix.RowCount) ? _openMatrix[currentIndex, colIndex] : double.NaN;
                    double high = (colIndex >= 0 && _highMatrix != null && currentIndex < _highMatrix.RowCount) ? _highMatrix[currentIndex, colIndex] : double.NaN;
                    double low = (colIndex >= 0 && _lowMatrix != null && currentIndex < _lowMatrix.RowCount) ? _lowMatrix[currentIndex, colIndex] : double.NaN;
                    double volume = (colIndex >= 0 && _volumeMatrix != null && currentIndex < _volumeMatrix.RowCount) ? _volumeMatrix[currentIndex, colIndex] : double.NaN;
                    double amount = (colIndex >= 0 && _amountMatrix != null && currentIndex < _amountMatrix.RowCount) ? _amountMatrix[currentIndex, colIndex] : double.NaN;

                    // 1. 标的突发停牌熔断检查：
                    // 若标的在当前时间步没有合法行情（open 与 close 均为 NaN 或 <= 0），判定为停牌！
                    // 挂单簿中该标的订单必须全部立即熔断剔除，派发 Rejected 报告，解冻资金！
                    bool isSuspended = (double.IsNaN(open) || open <= 0.0) && (double.IsNaN(close) || close <= 0.0);
                    if (isSuspended)
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
                                    $"Symbol '{symbol}' is suspended on step {currentIndex} (market data is NaN).",
                                    ord.Request.ClientOrderId,
                                    leaves));
                            }
                        }
                        continue;
                    }

                    if (marketPrice <= 0.0)
                    {
                        continue;
                    }

                    BarSnapshot bar = new(open, high, low, marketPrice, volume, amount);
                    MatchSymbolOrders(symbol, marketPrice, in bar, currentTime, ref fillReports, ref stateReports, colIndex, currentIndex);
                }
            }
        }

        // 锁外派发成交回报、拒绝及跨日自动撤单事件
        DispatchReports(fillReports, stateReports);
    }

    /// <inheritdoc />
    public double GetMarketPrice(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return 0.0;
        lock (_syncLock)
        {
            // 1. 若处于回测时间步进驱动中，优先直接从绑定的收盘价矩阵读取当期最新收盘价（零哈希寻址热路径）
            if (_closeMatrix != null && _boundMetadata != null && _currentIndex >= 0 && _currentIndex < _closeMatrix.RowCount)
            {
                int colIndex = _boundMetadata.GetSymbolIndex(symbol);
                if (colIndex >= 0)
                {
                    double cp = _closeMatrix[_currentIndex, colIndex];
                    if (cp > 0.0 && !double.IsNaN(cp) && !double.IsInfinity(cp))
                    {
                        return cp;
                    }
                }
            }

            // 2. 离散行情注入或无全局时序步进时，回退到局部行情字典
            if (_marketPrices.TryGetValue(symbol, out var p) && p > 0.0)
            {
                return p;
            }

            return 0.0;
        }
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
    public void OnMarketData(string symbol, double price, MarketTime time, IDataRow? bar = null)
    {
        if (string.IsNullOrWhiteSpace(symbol) || price <= 0.0 || double.IsNaN(price) || double.IsInfinity(price))
        {
            return;
        }

        List<ExecutionReport>? fillReports = null;
        List<OrderStateReport>? stateReports = null;

        lock (_syncLock)
        {
            _marketPrices[symbol] = price;
            MarketTime tradeTime = time != MarketTime.MinValue
                ? time
                : (_currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch);
            _currentTime = tradeTime;

            if (bar != null)
            {
                _currentBars[symbol] = bar;
            }
            else
            {
                _currentBars.Remove(symbol);
            }

            BarSnapshot snapshot = BarSnapshot.FromDataRow(bar, price);
            MatchSymbolOrders(symbol, price, in snapshot, tradeTime, ref fillReports, ref stateReports);
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
                        if (_vwapMatrix != null && colIndex >= 0 && currentIndex >= 0 && currentIndex < _vwapMatrix.RowCount)
                        {
                            double v = _vwapMatrix[currentIndex, colIndex];
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
                    Role: matchResult.Role));
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
                    order.Request.ClientOrderId,
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
                            ord.Request.ClientOrderId,
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
        double currentMarketPrice = GetMarketPrice(request.Symbol);

        if (request.Type == OrderType.Limit)
        {
            if (currentMarketPrice > 0.0)
            {
                return currentMarketPrice;
            }
            return request.Price > 0.0 ? request.Price : 0.0;
        }

        // 市价单：若显式指定了基准价格（例如在行情 Bar 上下单），优先使用显式价格
        if (request.Price > 0.0 && !double.IsNaN(request.Price) && !double.IsInfinity(request.Price))
        {
            return request.Price;
        }

        if (currentMarketPrice > 0.0)
        {
            return currentMarketPrice;
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

    private void EnsureTimeseriesBound(ITimeseriesRegistry timeseries)
    {
        if (ReferenceEquals(_boundTimeseries, timeseries))
        {
            return;
        }

        _boundTimeseries = timeseries;
        _boundMetadata = timeseries.Metadata;

        IReadOnlyMatrix2D<double>? closeMatrix = null;
        try
        {
            closeMatrix = timeseries.GetMatrix<double>(StandardFields.Close);
        }
        catch
        {
            // 矩阵未注册
        }

        if (_config.OnCheckVolume != null && !timeseries.HasField(StandardFields.Volume))
        {
            throw new InvalidOperationException(
                "BrokerConfig 已挂载自定义 OnCheckVolume 成交量委托，但时序注册表中未找到 'volume' 矩阵。");
        }

        _closeMatrix = closeMatrix;
        _openMatrix = timeseries.HasField(StandardFields.Open) ? timeseries.GetMatrix<double>(StandardFields.Open) : null;
        _highMatrix = timeseries.HasField(StandardFields.High) ? timeseries.GetMatrix<double>(StandardFields.High) : null;
        _lowMatrix = timeseries.HasField(StandardFields.Low) ? timeseries.GetMatrix<double>(StandardFields.Low) : null;
        _volumeMatrix = timeseries.HasField(StandardFields.Volume) ? timeseries.GetMatrix<double>(StandardFields.Volume) : null;
        _amountMatrix = timeseries.HasField(StandardFields.Amount) ? timeseries.GetMatrix<double>(StandardFields.Amount) : null;
        _vwapMatrix = timeseries.HasField("vwap") ? timeseries.GetMatrix<double>("vwap") : null;
    }

    private BarSnapshot GetBarSnapshot(string symbol, int rowIndex)
    {
        if (_closeMatrix == null || _boundMetadata == null || rowIndex < 0 || rowIndex >= _closeMatrix.RowCount)
        {
            return BarSnapshot.Empty;
        }

        int colIndex = _boundMetadata.GetSymbolIndex(symbol);
        if (colIndex < 0) return BarSnapshot.Empty;

        double close = _closeMatrix[rowIndex, colIndex];
        if (close <= 0.0 || double.IsNaN(close) || double.IsInfinity(close)) return BarSnapshot.Empty;

        double open = _openMatrix != null && rowIndex < _openMatrix.RowCount ? _openMatrix[rowIndex, colIndex] : double.NaN;
        double high = _highMatrix != null && rowIndex < _highMatrix.RowCount ? _highMatrix[rowIndex, colIndex] : double.NaN;
        double low = _lowMatrix != null && rowIndex < _lowMatrix.RowCount ? _lowMatrix[rowIndex, colIndex] : double.NaN;
        double volume = _volumeMatrix != null && rowIndex < _volumeMatrix.RowCount ? _volumeMatrix[rowIndex, colIndex] : double.NaN;
        double amount = _amountMatrix != null && rowIndex < _amountMatrix.RowCount ? _amountMatrix[rowIndex, colIndex] : double.NaN;

        return new BarSnapshot(open, high, low, close, volume, amount);
    }

    /// <summary>
    /// 保存仿真交易所状态快照（价格字典与排队挂单）。
    /// </summary>
    public ExchangeSnapshot SaveState()
    {
        lock (_syncLock)
        {
            var prices = new Dictionary<string, double>(_marketPrices, StringComparer.Ordinal);
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
            _marketPrices.Clear();
            foreach (var kvp in snapshot.MarketPrices)
            {
                _marketPrices[kvp.Key] = kvp.Value;
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
}
