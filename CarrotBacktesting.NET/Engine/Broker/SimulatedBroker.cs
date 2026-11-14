using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Broker
{
    /// <summary>
    /// 统一的高性能虚拟经纪商（SimulatedBroker）。
    /// 单一内核原生统领股票现货、期货、ETF、美股与外盘等全资产交易撮合与账户状态机；
    /// 严格基于 BrokerConfig 纯数值驱动，并通过对称 Hook 支持动态高级定制。
    /// 预留网关解耦与对账契约，方便后续零成本接入真实交易所。
    /// </summary>
    public class SimulatedBroker : IBroker
    {
        private readonly BrokerConfig _config;
        private readonly IFieldRegistry? _fields;
        private readonly TimeZoneInfo _timeZone;
        private readonly object _syncLock = new();

        private double _cash;
        private double _frozenCash;
        private DateTime _currentDate = DateTime.MinValue;
        private DateTime _currentTime = DateTime.MinValue;
        private DateTime _currentExchangeDate = DateTime.MinValue;
        private DateTime _nextRollTimeUtc = DateTime.MinValue;
        private long _orderSequence = 0;
        private IReadOnlyDictionary<string, decimal>? _positionsSnapshot;

        private readonly ConcurrentDictionary<string, PositionData> _positions = new();
        private readonly ConcurrentDictionary<string, OrderRecord> _orders = new();
        private readonly Dictionary<string, List<string>> _pendingOrdersBySymbol = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, double> _marketPrices = new();

        public event Action<string, OrderStatus>? OnOrderStatusChanged;
        public event Action<ExecutionReport>? OnTrade;
        public event Action<PositionEvent>? OnPositionChanged;

        /// <summary>
        /// 构造虚拟经纪商实例。
        /// 依赖严格单向自顶向下：仅依赖维表抽象 IFieldRegistry，彻底解耦运行时 EngineContext。
        /// </summary>
        /// <param name="config">经纪商交易与风控配置（含清算时区）。</param>
        /// <param name="fields">标的静态维表注册表（可选，供动态 Hook 查乘数/保证金率）。</param>
        public SimulatedBroker(BrokerConfig config, IFieldRegistry? fields = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _fields = fields;
            _timeZone = config.TimeZone ?? TimeZoneInfo.Utc;
            _cash = config.InitialCash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private DateTime ToExchangeDate(DateTime time)
        {
            if (_timeZone == TimeZoneInfo.Utc)
            {
                return time.Date;
            }
            // 防御 Unspecified：一律视为系统基准 UTC 并转换为交易所所在时区的当地日期
            DateTime utcTime = time.Kind == DateTimeKind.Utc
                ? time
                : (time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc));
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, _timeZone).Date;
        }

        private void InitializeRollThreshold(DateTime currentTime)
        {
            _currentDate = currentTime;
            _currentExchangeDate = ToExchangeDate(currentTime);
            CalculateNextRollTimeUtc(_currentExchangeDate);
            UnlockPendingLots(_currentExchangeDate);
        }

        private void CalculateNextRollTimeUtc(DateTime exchangeDate)
        {
            // 下一次换日时刻为交易所本地次日 00:00:00 对应的物理时刻
            DateTime nextLocalMidnight = exchangeDate.Date.AddDays(1);
            if (_timeZone == TimeZoneInfo.Utc)
            {
                _nextRollTimeUtc = DateTime.SpecifyKind(nextLocalMidnight, DateTimeKind.Utc);
            }
            else
            {
                _nextRollTimeUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, _timeZone);
            }
        }

        /// <inheritdoc />
        public double GetCash()
        {
            lock (_syncLock)
            {
                return _cash - _frozenCash;
            }
        }

        /// <inheritdoc />
        public decimal GetPosition(string symbol)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
            lock (_syncLock)
            {
                return _positions.TryGetValue(symbol, out var pos) ? pos.TotalQuantity : 0m;
            }
        }

        /// <inheritdoc />
        public decimal GetAvailablePosition(string symbol)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
            lock (_syncLock)
            {
                return _positions.TryGetValue(symbol, out var pos) ? pos.AvailableQuantity : 0m;
            }
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, decimal> GetPositions()
        {
            lock (_syncLock)
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
        }

        /// <inheritdoc />
        public double GetTotalEquity()
        {
            lock (_syncLock)
            {
                double total = _cash;
                foreach (var pos in _positions.Values)
                {
                    if (pos.TotalQuantity == 0m) continue;

                    double mult = GetMultiplier(pos.Symbol);
                    double marginRate = GetMarginRate(pos.Symbol);

                    // 通用资产盯市估值：总资产 = 现金 + 占用保证金 + 浮动盈亏
                    // 当 marginRate = 1.0 且 mult = 1.0（现货）时，数学恒等推导为：
                    // marginOccupied + unrealizedPnL = Qty * AvgCost + (LastPrice - AvgCost) * Qty = Qty * LastPrice（全额市值）
                    double marginOccupied = Math.Abs((double)pos.TotalQuantity) * pos.AverageCost * mult * marginRate;
                    double unrealizedPnL = (pos.LastPrice - pos.AverageCost) * (double)pos.TotalQuantity * mult;
                    total += marginOccupied + unrealizedPnL;
                }
                return total;
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
        public void CancelOrder(string orderId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
            bool shouldDispatch = false;

            lock (_syncLock)
            {
                if (_orders.TryGetValue(orderId, out var ord))
                {
                    if (ord.Status == OrderStatus.Submitted || ord.Status == OrderStatus.Pending)
                    {
                        // 释放挂单冻结资金
                        _frozenCash = Math.Max(0.0, _frozenCash - ord.FrozenCash);
                        ord.FrozenCash = 0.0;

                        // 释放挂单冻结持仓
                        if (_positions.TryGetValue(ord.Request.Symbol, out var pos))
                        {
                            pos.FrozenQuantity = Math.Max(0m, pos.FrozenQuantity - ord.FrozenPosition);
                            if (pos.TotalQuantity > 0m)
                            {
                                pos.AvailableQuantity += ord.FrozenPosition;
                            }
                            else
                            {
                                // 空头撤单：恢复空头可用额度（朝远离零方向恢复，如 -3 - 2 = -5）
                                pos.AvailableQuantity -= ord.FrozenPosition;
                            }
                        }
                        ord.FrozenPosition = 0m;

                        ord.Status = OrderStatus.Canceled;

                        // 从标的挂单桶中精确移除，若桶空则及时清理消除内存泄漏
                        if (_pendingOrdersBySymbol.TryGetValue(ord.Request.Symbol, out var list))
                        {
                            list.Remove(orderId);
                            if (list.Count == 0)
                            {
                                _pendingOrdersBySymbol.Remove(ord.Request.Symbol);
                            }
                        }

                        _positionsSnapshot = null;
                        shouldDispatch = true;
                    }
                }
            }

            // 锁外派发事件，彻底杜绝回调重入引发死锁
            if (shouldDispatch)
            {
                OnOrderStatusChanged?.Invoke(orderId, OrderStatus.Canceled);
            }
        }

        /// <inheritdoc />
        public void Step(DateTime currentTime)
        {
            List<KeyValuePair<string, double>>? pendingSymbolsToMatch = null;

            lock (_syncLock)
            {
                _currentTime = currentTime;

                DateTime utcTime = currentTime.Kind == DateTimeKind.Utc
                    ? currentTime
                    : (currentTime.Kind == DateTimeKind.Local 
                        ? currentTime.ToUniversalTime() 
                        : DateTime.SpecifyKind(currentTime, DateTimeKind.Utc));

                // 首次步进或时间发生跃迁时初始化换日阈值
                if (_nextRollTimeUtc == DateTime.MinValue || currentTime < _currentDate)
                {
                    InitializeRollThreshold(currentTime);
                }

                // 时间戳阈值比对：检测是否到达交易所本地 00:00:00 换日时刻
                if (utcTime >= _nextRollTimeUtc)
                {
                    ExecuteDayRollover(currentTime);
                }

                // 仅针对当前有未成交挂单的标的提取行情快照，准备在锁外推进撮合
                if (_pendingOrdersBySymbol.Count > 0)
                {
                    pendingSymbolsToMatch = new List<KeyValuePair<string, double>>(_pendingOrdersBySymbol.Count);
                    foreach (var symbol in _pendingOrdersBySymbol.Keys)
                    {
                        if (_marketPrices.TryGetValue(symbol, out var price))
                        {
                            pendingSymbolsToMatch.Add(new KeyValuePair<string, double>(symbol, price));
                        }
                    }
                }
            }

            // 锁外执行挂单撮合，确保撮合过程中的事件派发为纯粹的 Open Calls
            if (pendingSymbolsToMatch != null)
            {
                for (int i = 0; i < pendingSymbolsToMatch.Count; i++)
                {
                    var kvp = pendingSymbolsToMatch[i];
                    MatchPendingOrdersForSymbol(kvp.Key, kvp.Value);
                }
            }
        }

        private void ExecuteDayRollover(DateTime currentTime)
        {
            _currentDate = currentTime;
            _currentExchangeDate = ToExchangeDate(currentTime);
            CalculateNextRollTimeUtc(_currentExchangeDate);
            UnlockPendingLots(_currentExchangeDate);
        }

        private void UnlockPendingLots(DateTime exchangeDate)
        {
            // 推进跨日结算：将到期的 T+N 冻结份额划转为今日可用持仓（倒序原地移除，零堆分配）
            foreach (var pos in _positions.Values)
            {
                if (pos.PendingLots.Count == 0) continue;

                decimal unlocked = 0m;
                for (int i = pos.PendingLots.Count - 1; i >= 0; i--)
                {
                    var lot = pos.PendingLots[i];
                    if (exchangeDate >= lot.AvailableDate)
                    {
                        unlocked += lot.Quantity;
                        pos.PendingLots.RemoveAt(i);
                    }
                }

                if (unlocked > 0m)
                {
                    pos.AvailableQuantity += unlocked;
                    _positionsSnapshot = null;
                }
            }
        }

        /// <inheritdoc />
        public void SyncState(double cash, IReadOnlyDictionary<string, decimal> positions)
        {
            ArgumentNullException.ThrowIfNull(positions);
            List<string>? canceledOrderIds = null;

            lock (_syncLock)
            {
                _cash = cash;
                _frozenCash = 0.0;

                // 同步账户状态时，将现有在途挂单显式标记为 Canceled 并清空冻结
                if (_pendingOrdersBySymbol.Count > 0)
                {
                    canceledOrderIds = new List<string>();
                    foreach (var list in _pendingOrdersBySymbol.Values)
                    {
                        foreach (var orderId in list)
                        {
                            if (_orders.TryGetValue(orderId, out var ord))
                            {
                                ord.Status = OrderStatus.Canceled;
                                ord.FrozenCash = 0.0;
                                ord.FrozenPosition = 0m;
                            }
                            canceledOrderIds.Add(orderId);
                        }
                    }
                    _pendingOrdersBySymbol.Clear();
                }

                _positions.Clear();
                _positionsSnapshot = null;

                foreach (var kvp in positions)
                {
                    if (kvp.Value != 0m)
                    {
                        var pos = new PositionData(kvp.Key)
                        {
                            TotalQuantity = kvp.Value,
                            AvailableQuantity = kvp.Value
                        };
                        // 若已存在最新市场行情，同步赋予基准价以确保后续盯市估值自洽
                        if (_marketPrices.TryGetValue(kvp.Key, out var lp))
                        {
                            pos.LastPrice = lp;
                            pos.AverageCost = lp;
                        }
                        _positions[kvp.Key] = pos;
                    }
                }
            }

            if (canceledOrderIds != null)
            {
                foreach (var id in canceledOrderIds)
                {
                    OnOrderStatusChanged?.Invoke(id, OrderStatus.Canceled);
                }
            }
        }

        /// <summary>
        /// 更新标的最新行情价格（用于驱动市价单撮合与盯市估值）。
        /// </summary>
        /// <param name="symbol">标的代码。</param>
        /// <param name="price">最新市场行情价格。</param>
        public void UpdateMarketPrice(string symbol, double price)
        {
            if (string.IsNullOrWhiteSpace(symbol) || price <= 0.0 || double.IsNaN(price) || double.IsInfinity(price))
            {
                return;
            }

            bool hasPending = false;
            lock (_syncLock)
            {
                _marketPrices[symbol] = price;
                if (_positions.TryGetValue(symbol, out var pos))
                {
                    pos.LastPrice = price;
                }

                hasPending = _pendingOrdersBySymbol.ContainsKey(symbol);
            }

            // 收到最新价格时立即在锁外触发该标的未成交限价挂单的撮合检查
            if (hasPending)
            {
                MatchPendingOrdersForSymbol(symbol, price);
            }
        }

        /// <inheritdoc />
        public string SubmitOrder(OrderRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            double basePrice;
            if (request.Type == OrderType.Limit)
            {
                // 限价单：request.Price 是委托限价。撮合基准市价优先从最新市场行情获取
                double marketPrice = 0.0;
                lock (_syncLock)
                {
                    if (_marketPrices.TryGetValue(request.Symbol, out var lp) && lp > 0.0)
                    {
                        marketPrice = lp;
                    }
                }
                basePrice = marketPrice > 0.0 ? marketPrice : request.Price;
            }
            else
            {
                // 市价单：若委托中显式指定了基准价（回测驱动），优先使用；否则从最新市场行情获取
                if (request.Price > 0.0 && !double.IsNaN(request.Price) && !double.IsInfinity(request.Price))
                {
                    basePrice = request.Price;
                }
                else
                {
                    double marketPrice = 0.0;
                    lock (_syncLock)
                    {
                        if (_marketPrices.TryGetValue(request.Symbol, out var lp) && lp > 0.0)
                        {
                            marketPrice = lp;
                        }
                    }
                    basePrice = marketPrice;
                }
            }

            return ExecuteOrder(request, basePrice, null);
        }

        /// <summary>
        /// 执行订单撮合仿真（支持由引擎时间步或外部传入当前 Bar 行情与价格驱动）。
        /// </summary>
        /// <param name="request">订单意图。</param>
        /// <param name="basePrice">基准成交价格（对于市价单传入当前收盘价或开盘价）。</param>
        /// <param name="bar">当前时间步的行情数据行（可选，供流动性与涨跌停 Hook 校验）。</param>
        /// <returns>订单唯一标识符。</returns>
        public string ExecuteOrder(OrderRequest request, double basePrice, IDataRow? bar)
        {
            ArgumentNullException.ThrowIfNull(request);

            string orderId = string.IsNullOrWhiteSpace(request.ClientOrderId)
                ? $"sim_{Interlocked.Increment(ref _orderSequence)}"
                : request.ClientOrderId;

            var record = new OrderRecord(orderId, request);

            // 栈上暂存事件，退出锁后再行开放调用（Open Calls），杜绝死锁与重入，且零 GC 分配
            OrderStatus? status1 = OrderStatus.Submitted;
            OrderStatus? status2 = null;
            ExecutionReport? tradeReport = null;
            PositionEvent? positionReport = null;

            lock (_syncLock)
            {
                // 订单登记置于锁内，彻底消除与并发 CancelOrder 的覆盖时序竞态
                record.Status = OrderStatus.Submitted;
                _orders[orderId] = record;

                // 1. 基础参数有效性校验
                if (request.Quantity <= 0m || basePrice <= 0.0 || double.IsNaN(basePrice) || double.IsInfinity(basePrice))
                {
                    RejectOrder(record, "Quantity or basePrice must be positive and finite.", out status2);
                }
                else
                {
                    var pos = _positions.GetOrAdd(request.Symbol, s => new PositionData(s));

                    // 2. 卖空权限与可用持仓检查
                    if (request.IsSell && !ValidateSellPosition(record, pos, out status2))
                    {
                        // 校验失败已被 RejectOrder 标记
                    }
                    // 3. 成交量流动性检查 (VolumeLimit)
                    else if (bar != null && !ValidateLiquidity(record, bar, out status2))
                    {
                        // 校验失败已被 RejectOrder 标记
                    }
                    // 4. 涨跌停封板检查 (CheckLimit)
                    else if (bar != null && _config.CheckLimit && !ValidatePriceLimit(record, bar, out status2))
                    {
                        // 校验失败已被 RejectOrder 标记
                    }
                    else
                    {
                        // 判断是否应作为未成交限价挂单保留（买单限价低于基准价，或卖单限价高于基准价）
                        bool isUnfilledLimitOrder = false;
                        if (request.Type == OrderType.Limit && request.Price > 0.0)
                        {
                            if (request.IsBuy && request.Price < basePrice)
                            {
                                isUnfilledLimitOrder = true;
                            }
                            else if (!request.IsBuy && request.Price > basePrice)
                            {
                                isUnfilledLimitOrder = true;
                            }
                        }

                        // 未成交挂单按委托限价估算冻结本金；即时成交订单按基准市价计算滑点成交
                        double targetPrice = isUnfilledLimitOrder ? request.Price : basePrice;

                        // 5. 滑点撮合计算
                        double fillPrice;
                        if (_config.OnCalculateSlippage != null && bar != null)
                        {
                            fillPrice = _config.OnCalculateSlippage(request, targetPrice, bar);
                        }
                        else
                        {
                            fillPrice = request.IsBuy
                                ? targetPrice * (1.0 + _config.Slippage)
                                : targetPrice * (1.0 - _config.Slippage);
                        }

                        // Taker 激进限价单保护：成交价绝严禁劣于限价（买入成交价 <= LimitPrice，卖出成交价 >= LimitPrice）
                        if (request.Type == OrderType.Limit && request.Price > 0.0)
                        {
                            fillPrice = request.IsBuy
                                ? Math.Min(fillPrice, request.Price)
                                : Math.Max(fillPrice, request.Price);
                        }

                        // 6. 获取合约规格与保证金率
                        double multiplier = GetMultiplier(request.Symbol);
                        double marginRate = GetMarginRate(request.Symbol);
                        int settleDays = GetSettleDays(request.Symbol);

                        // 7. 计算名义成交额与手续费
                        double turnover = (double)request.Quantity * fillPrice * multiplier;
                        double commission = _config.OnCalculateFee != null
                            ? _config.OnCalculateFee(request, turnover)
                            : Math.Max(_config.MinFee, turnover * _config.FeeRate);

                        // 8. 仓位切分与资金充足性校验（通用单一路径）
                        bool isOpposite = (request.IsBuy && pos.TotalQuantity < 0m) || (!request.IsBuy && pos.TotalQuantity > 0m);
                        decimal closableCapacity = 0m;
                        if (isOpposite)
                        {
                            closableCapacity = pos.TotalQuantity > 0m 
                                ? Math.Max(0m, pos.AvailableQuantity) 
                                : Math.Max(0m, Math.Abs(pos.TotalQuantity) - pos.FrozenQuantity);
                        }
                        decimal closeQty = isOpposite ? Math.Min(request.Quantity, closableCapacity) : 0m;
                        decimal openQty = request.Quantity - closeQty;

                        // 开仓部分所需冻结保证金与手续费（纯平仓仅需支付手续费）
                        double requiredMargin = (double)openQty * fillPrice * multiplier * marginRate;
                        double requiredCash = requiredMargin + commission;

                        double availableCash = _cash - _frozenCash;
                        if (availableCash < requiredCash)
                        {
                            RejectOrder(record, $"Insufficient cash: available={availableCash:F2}, required={requiredCash:F2}", out status2);
                        }
                        else
                        {
                            DateTime tradeTime = _currentTime != DateTime.MinValue 
                                ? _currentTime 
                                : (_currentDate != DateTime.MinValue ? _currentDate : DateTime.UtcNow);

                            if (isUnfilledLimitOrder)
                            {
                                // 两阶段冻结 Phase 1：预冻结资金与持仓，进入待撮合挂单队列
                                _frozenCash += requiredCash;
                                record.FrozenCash = requiredCash;

                                if (closeQty > 0m)
                                {
                                    if (pos.TotalQuantity > 0m)
                                    {
                                        pos.AvailableQuantity -= closeQty;
                                    }
                                    else
                                    {
                                        // 空头平仓挂单冻结：可用持仓朝零方向收敛（如 -5 + 2 = -3）
                                        pos.AvailableQuantity += closeQty;
                                    }
                                    pos.FrozenQuantity += closeQty;
                                    record.FrozenPosition = closeQty;
                                }

                                record.CloseQuantity = closeQty;
                                record.OpenQuantity = openQty;
                                record.Multiplier = multiplier;
                                record.MarginRate = marginRate;
                                record.SettleDays = settleDays;
                                record.Status = OrderStatus.Submitted;

                                if (!_pendingOrdersBySymbol.TryGetValue(request.Symbol, out var list))
                                {
                                    list = new List<string>();
                                    _pendingOrdersBySymbol[request.Symbol] = list;
                                }
                                list.Add(orderId);
                                _positionsSnapshot = null;

                                // 保持挂单处于 Submitted 状态，无额外 status2
                                status2 = null;
                            }
                            else
                            {
                                // 立即成交（市价单或已穿透限价的限价单）
                                ApplyOrderFill(record, pos, fillPrice, multiplier, marginRate, settleDays, closeQty, openQty, commission, tradeTime, out tradeReport, out positionReport);
                                status2 = OrderStatus.Filled;
                            }
                        }
                    }
                }
            }

            // 锁外派发事件，彻底杜绝回调重入引发死锁与状态竞争
            if (status1.HasValue)
            {
                OnOrderStatusChanged?.Invoke(orderId, status1.Value);
            }
            if (status2.HasValue)
            {
                OnOrderStatusChanged?.Invoke(orderId, status2.Value);
            }
            if (tradeReport != null)
            {
                OnTrade?.Invoke(tradeReport);
            }
            if (positionReport != null)
            {
                OnPositionChanged?.Invoke(positionReport);
            }

            return orderId;
        }

        private bool ValidateSellPosition(OrderRecord record, PositionData pos, out OrderStatus? status)
        {
            status = null;
            if (!_config.AllowShort)
            {
                if (pos.TotalQuantity <= 0m || record.Request.Quantity > pos.AvailableQuantity)
                {
                    RejectOrder(record, $"Insufficient available position. Total={pos.TotalQuantity}, Available={pos.AvailableQuantity}, Request={record.Request.Quantity}", out status);
                    return false;
                }
            }
            else if (pos.PendingLots.Count > 0 && record.Request.Quantity > pos.AvailableQuantity)
            {
                // 即使全局允许做空（如期货对冲），当前标的若尚有在途未交收份额（T+N），也绝对严禁违规提前平仓
                RejectOrder(record, $"Cannot sell unsettled T+N pending lots. Total={pos.TotalQuantity}, Available={pos.AvailableQuantity}, Request={record.Request.Quantity}", out status);
                return false;
            }
            return true;
        }

        private bool ValidateLiquidity(OrderRecord record, IDataRow bar, out OrderStatus? status)
        {
            status = null;
            if (_config.OnCheckVolume != null)
            {
                if (!_config.OnCheckVolume(record.Request, bar))
                {
                    RejectOrder(record, "Rejected by OnCheckVolume hook (liquidity constraint).", out status);
                    return false;
                }
            }
            else if (_config.VolumeLimit > 0.0)
            {
                double barVolume = bar.GetDouble("volume");
                double maxAllowed = barVolume * _config.VolumeLimit;
                if ((double)record.Request.Quantity > maxAllowed)
                {
                    RejectOrder(record, $"Exceeded volume limit: {record.Request.Quantity} > {maxAllowed} (BarVolume={barVolume} * {_config.VolumeLimit})", out status);
                    return false;
                }
            }
            return true;
        }

        private bool ValidatePriceLimit(OrderRecord record, IDataRow bar, out OrderStatus? status)
        {
            status = null;
            if (_config.OnCheckLimit != null)
            {
                if (!_config.OnCheckLimit(record.Request, bar))
                {
                    RejectOrder(record, "Rejected by OnCheckLimit hook (price limit hit).", out status);
                    return false;
                }
            }
            return true;
        }

        private void ApplyOrderFill(
            OrderRecord record,
            PositionData pos,
            double fillPrice,
            double multiplier,
            double marginRate,
            int settleDays,
            decimal closeQty,
            decimal openQty,
            double commission,
            DateTime tradeTime,
            out ExecutionReport tradeReport,
            out PositionEvent positionReport)
        {
            // 统一扣减手续费
            _cash -= commission;

            // 阶段 A：平仓结算（释放原持仓对应的保证金，并结转平仓已实现盈亏）
            if (closeQty > 0m)
            {
                double pnlPerUnit = pos.TotalQuantity > 0m
                    ? (fillPrice - pos.AverageCost)
                    : (pos.AverageCost - fillPrice);
                double realizedPnL = pnlPerUnit * (double)closeQty * multiplier;
                double marginReleased = (double)closeQty * pos.AverageCost * multiplier * marginRate;

                _cash += (marginReleased + realizedPnL);

                if (pos.TotalQuantity > 0m)
                {
                    pos.TotalQuantity -= closeQty;
                    pos.AvailableQuantity -= closeQty;
                }
                else
                {
                    pos.TotalQuantity += closeQty;
                    pos.AvailableQuantity += closeQty;
                }

                if (pos.TotalQuantity == 0m)
                {
                    pos.AverageCost = 0.0;
                    if (openQty == 0m && pos.PendingLots.Count == 0 && pos.FrozenQuantity == 0m)
                    {
                        _positions.TryRemove(record.Request.Symbol, out _);
                    }
                }
            }

            // 阶段 B：开仓建仓（扣除新开仓所需保证金，更新均价与到期队列）
            if (openQty > 0m)
            {
                double openTurnover = (double)openQty * fillPrice * multiplier;
                double marginAdded = openTurnover * marginRate;
                _cash -= marginAdded;

                if (pos.TotalQuantity == 0m)
                {
                    // 初始建仓（或平仓后反手新开）
                    pos.AverageCost = fillPrice;
                    pos.TotalQuantity = record.Request.IsBuy ? openQty : -openQty;

                    if (settleDays == 0)
                    {
                        pos.AvailableQuantity = pos.TotalQuantity;
                    }
                    else
                    {
                        if (record.Request.IsBuy)
                        {
                            DateTime localTradeDate = ToExchangeDate(tradeTime);
                            pos.PendingLots.Add(new PendingLot(openQty, localTradeDate.AddDays(settleDays)));
                        }
                        else
                        {
                            pos.AvailableQuantity = -openQty;
                        }
                    }
                    _positions[record.Request.Symbol] = pos;
                }
                else
                {
                    // 同向加仓：加权平均成本更新
                    decimal currentAbsQty = Math.Abs(pos.TotalQuantity);
                    decimal newAbsQty = currentAbsQty + openQty;
                    pos.AverageCost = ((double)currentAbsQty * pos.AverageCost + (double)openQty * fillPrice) / (double)newAbsQty;

                    if (record.Request.IsBuy)
                    {
                        pos.TotalQuantity += openQty;
                        if (settleDays == 0)
                        {
                            pos.AvailableQuantity += openQty;
                        }
                        else
                        {
                            DateTime localTradeDate = ToExchangeDate(tradeTime);
                            pos.PendingLots.Add(new PendingLot(openQty, localTradeDate.AddDays(settleDays)));
                        }
                    }
                    else
                    {
                        pos.TotalQuantity -= openQty;
                        pos.AvailableQuantity -= openQty;
                    }
                }
            }

            pos.LastPrice = fillPrice;
            _marketPrices[record.Request.Symbol] = fillPrice;
            _positionsSnapshot = null;

            record.Status = OrderStatus.Filled;

            tradeReport = new ExecutionReport(
                record.OrderId,
                record.Request.Symbol,
                fillPrice,
                record.Request.Quantity,
                commission,
                tradeTime);

            positionReport = new PositionEvent(
                record.Request.Symbol,
                pos.TotalQuantity,
                pos.AverageCost,
                tradeTime);
        }

        private void MatchPendingOrdersForSymbol(string symbol, double marketPrice)
        {
            DateTime tradeTime = _currentTime != DateTime.MinValue 
                ? _currentTime 
                : (_currentDate != DateTime.MinValue ? _currentDate : DateTime.UtcNow);

            FillDispatch[]? rentedDispatches = null;
            int dispatchCount = 0;

            lock (_syncLock)
            {
                // 强制使用锁内最新权威市场行情，防御锁外传参的陈旧时序竞态
                if (_marketPrices.TryGetValue(symbol, out var latestPrice) && latestPrice > 0.0)
                {
                    marketPrice = latestPrice;
                }

                if (!_pendingOrdersBySymbol.TryGetValue(symbol, out var list) || list.Count == 0)
                {
                    return;
                }

                int initialCount = list.Count;
                rentedDispatches = ArrayPool<FillDispatch>.Shared.Rent(initialCount);

                // 正序按照 FIFO 时间优先推进撮合
                for (int i = 0; i < list.Count; )
                {
                    string orderId = list[i];
                    if (!_orders.TryGetValue(orderId, out var ord) || ord.Status != OrderStatus.Submitted)
                    {
                        list.RemoveAt(i);
                        continue;
                    }

                    // 判断限价单是否触碰成交价（买单市价 <= 委托价，卖单市价 >= 委托价）
                    bool canFill = ord.Request.IsBuy 
                        ? marketPrice <= ord.Request.Price 
                        : marketPrice >= ord.Request.Price;

                    if (canFill)
                    {
                        list.RemoveAt(i);

                        // 限价挂单被动撮合（Maker 规则）：成交价严格受委托限价保护（买入成交价 <= 委托价，卖出成交价 >= 委托价）
                        double fillPrice = ord.Request.Price;

                        var pos = _positions.GetOrAdd(symbol, s => new PositionData(s));

                        // 释放挂单阶段预借的持仓与资金，随后由统一的 ApplyOrderFill 权威执行扣减与结转
                        if (pos.TotalQuantity > 0m)
                        {
                            pos.AvailableQuantity += ord.FrozenPosition;
                        }
                        else
                        {
                            pos.AvailableQuantity -= ord.FrozenPosition;
                        }
                        pos.FrozenQuantity = Math.Max(0m, pos.FrozenQuantity - ord.FrozenPosition);
                        _frozenCash = Math.Max(0.0, _frozenCash - ord.FrozenCash);

                        // 重新根据最新成交价计算名义成交额与手续费
                        double turnover = (double)ord.Request.Quantity * fillPrice * ord.Multiplier;
                        double commission = _config.OnCalculateFee != null
                            ? _config.OnCalculateFee(ord.Request, turnover)
                            : Math.Max(_config.MinFee, turnover * _config.FeeRate);

                        ApplyOrderFill(ord, pos, fillPrice, ord.Multiplier, ord.MarginRate, ord.SettleDays, ord.CloseQuantity, ord.OpenQuantity, commission, tradeTime, out var tradeReport, out var posEvent);

                        rentedDispatches[dispatchCount++] = new FillDispatch(ord.OrderId, tradeReport, posEvent);
                    }
                    else
                    {
                        i++;
                    }
                }

                if (list.Count == 0)
                {
                    _pendingOrdersBySymbol.Remove(symbol);
                }
            }

            // 锁外派发所有成交与持仓变更事件，租借 ArrayPool 实现 100% 零 GC 分配
            if (rentedDispatches != null)
            {
                try
                {
                    for (int i = 0; i < dispatchCount; i++)
                    {
                        ref readonly var d = ref rentedDispatches[i];
                        OnOrderStatusChanged?.Invoke(d.OrderId, OrderStatus.Filled);
                        OnTrade?.Invoke(d.Trade);
                        OnPositionChanged?.Invoke(d.Position);
                    }
                }
                finally
                {
                    ArrayPool<FillDispatch>.Shared.Return(rentedDispatches);
                }
            }
        }

        private void RejectOrder(OrderRecord record, string reason, out OrderStatus? status)
        {
            record.Status = OrderStatus.Rejected;
            record.RejectReason = reason;
            status = OrderStatus.Rejected;
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

        private readonly struct FillDispatch
        {
            public readonly string OrderId;
            public readonly ExecutionReport Trade;
            public readonly PositionEvent Position;

            public FillDispatch(string orderId, ExecutionReport trade, PositionEvent position)
            {
                OrderId = orderId;
                Trade = trade;
                Position = position;
            }
        }

        private class PositionData
        {
            public string Symbol { get; }
            public decimal TotalQuantity { get; set; }
            public decimal AvailableQuantity { get; set; }
            public decimal FrozenQuantity { get; set; }
            public double AverageCost { get; set; }
            public double LastPrice { get; set; }
            public List<PendingLot> PendingLots { get; set; } = new();

            public PositionData(string symbol) => Symbol = symbol;
        }

        private record PendingLot(decimal Quantity, DateTime AvailableDate);

        private class OrderRecord
        {
            public string OrderId { get; }
            public OrderRequest Request { get; }
            public OrderStatus Status { get; set; } = OrderStatus.Pending;
            public string? RejectReason { get; set; }

            public double FrozenCash { get; set; }
            public decimal FrozenPosition { get; set; }
            public decimal CloseQuantity { get; set; }
            public decimal OpenQuantity { get; set; }
            public double Multiplier { get; set; }
            public double MarginRate { get; set; }
            public int SettleDays { get; set; }

            public OrderRecord(string orderId, OrderRequest request)
            {
                OrderId = orderId;
                Request = request;
            }
        }
    }
}
