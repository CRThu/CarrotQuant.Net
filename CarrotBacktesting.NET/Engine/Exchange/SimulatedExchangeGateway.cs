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
                }
            }

            if (!stateChanged.HasValue)
            {
                double basePrice = GetBasePriceForOrder(request);
                double multiplier = GetMultiplier(request.Symbol);
                MarketTime tradeTime = _currentTime != MarketTime.MinValue ? _currentTime : MarketTime.UnixEpoch;

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
    public void Step(MarketTime currentTime, ITimeseriesRegistry timeseries, int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(timeseries);

        List<ExecutionReport>? fillReports = null;
        List<string>? rejectedOrders = null;
        List<string>? canceledOrders = null;

        lock (_syncLock)
        {
            bool isNewDay = _currentTime != MarketTime.MinValue && currentTime.Date != _currentTime.Date;
            _currentTime = currentTime;
            _currentIndex = currentIndex;
            EnsureTimeseriesBound(timeseries);

            // 若开启了当日有效（Day Order）收盘撤单规则且检测到跨日，集中撤销前一交易日未成交的排队订单
            if (_config.CancelPendingOrdersOnDailyClose && isNewDay && _orderBook.Count > 0)
            {
                var allPending = new List<SimulatedOrder>(_orderBook.GetAllOrders());
                _orderBook.Clear();
                canceledOrders = new List<string>(allPending.Count);
                for (int i = 0; i < allPending.Count; i++)
                {
                    canceledOrders.Add(allPending[i].OrderId);
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

                    if (colIndex >= 0 && currentIndex < _closeMatrix.RowCount)
                    {
                        double p = _closeMatrix[currentIndex, colIndex];
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

                    double open = (colIndex >= 0 && _openMatrix != null && currentIndex < _openMatrix.RowCount) ? _openMatrix[currentIndex, colIndex] : double.NaN;
                    double high = (colIndex >= 0 && _highMatrix != null && currentIndex < _highMatrix.RowCount) ? _highMatrix[currentIndex, colIndex] : double.NaN;
                    double low = (colIndex >= 0 && _lowMatrix != null && currentIndex < _lowMatrix.RowCount) ? _lowMatrix[currentIndex, colIndex] : double.NaN;
                    double volume = (colIndex >= 0 && _volumeMatrix != null && currentIndex < _volumeMatrix.RowCount) ? _volumeMatrix[currentIndex, colIndex] : double.NaN;
                    double amount = (colIndex >= 0 && _amountMatrix != null && currentIndex < _amountMatrix.RowCount) ? _amountMatrix[currentIndex, colIndex] : double.NaN;

                    BarSnapshot bar = new(open, high, low, marketPrice, volume, amount);
                    MatchSymbolOrders(symbol, marketPrice, in bar, currentTime, ref fillReports, ref rejectedOrders);
                }
            }
        }

        // 锁外派发成交回报、拒绝及跨日自动撤单事件
        DispatchReports(fillReports, rejectedOrders, canceledOrders);
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
        List<string>? rejectedOrders = null;

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
            MatchSymbolOrders(symbol, price, in snapshot, tradeTime, ref fillReports, ref rejectedOrders);
        }

        // 锁外派发成交回报与拒绝事件
        DispatchReports(fillReports, rejectedOrders);
    }

    private void MatchSymbolOrders(
        string symbol,
        double marketPrice,
        in BarSnapshot bar,
        MarketTime tradeTime,
        ref List<ExecutionReport>? fillReports,
        ref List<string>? rejectedOrders)
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
            var matchResult = _matcher.EvaluatePendingLimitOrder(order, marketPrice, in bar, tradeTime);

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
            else if (matchResult.IsRejected)
            {
                _orderBook.Remove(order.OrderId, out _);
                rejectedOrders ??= new List<string>();
                rejectedOrders.Add(order.OrderId);
            }
        }
    }

    private void DispatchReports(List<ExecutionReport>? fillReports, List<string>? rejectedOrders, List<string>? canceledOrders = null)
    {
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
        if (canceledOrders != null)
        {
            for (int i = 0; i < canceledOrders.Count; i++)
            {
                OnOrderStateChanged?.Invoke(canceledOrders[i], OrderStatus.Canceled);
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
                    ord.SettleDays
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
                    SettleDays = snap.SettleDays
                };
                _orderBook.Add(ord);
            }
        }
    }
}
