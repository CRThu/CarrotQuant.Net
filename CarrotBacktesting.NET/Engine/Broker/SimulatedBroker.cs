using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        private DateTime _currentDate = DateTime.MinValue;
        private DateTime _currentTime = DateTime.MinValue;
        private DateTime _currentExchangeDate = DateTime.MinValue;
        private DateTime _nextRollTimeUtc = DateTime.MinValue;
        private long _orderSequence = 0;
        private IReadOnlyDictionary<string, decimal>? _positionsSnapshot;

        private readonly ConcurrentDictionary<string, PositionData> _positions = new();
        private readonly ConcurrentDictionary<string, OrderRecord> _orders = new();

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
                : DateTime.SpecifyKind(time, DateTimeKind.Utc);
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
                return _cash;
            }
        }

        /// <inheritdoc />
        public decimal GetPosition(string symbol)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
            return _positions.TryGetValue(symbol, out var pos) ? pos.TotalQuantity : 0m;
        }

        /// <inheritdoc />
        public decimal GetAvailablePosition(string symbol)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
            return _positions.TryGetValue(symbol, out var pos) ? pos.AvailableQuantity : 0m;
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

                    if (Math.Abs(marginRate - 1.0) < 1e-6 && !_config.AllowShort)
                    {
                        // 现货全额模式：总资产 = 现金 + 持仓最新市值
                        total += (double)pos.TotalQuantity * pos.LastPrice * mult;
                    }
                    else
                    {
                        // 保证金衍生品模式：总资产 = 现金 + 占用保证金(纯计算) + 浮动盈亏
                        double marginOccupied = Math.Abs((double)pos.TotalQuantity) * pos.AverageCost * mult * marginRate;
                        double unrealizedPnL = (pos.LastPrice - pos.AverageCost) * (double)pos.TotalQuantity * mult;
                        total += marginOccupied + unrealizedPnL;
                    }
                }
                return total;
            }
        }

        /// <inheritdoc />
        public OrderStatus GetOrderStatus(string orderId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
            return _orders.TryGetValue(orderId, out var ord) ? ord.Status : OrderStatus.Rejected;
        }

        /// <inheritdoc />
        public void CancelOrder(string orderId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
            lock (_syncLock)
            {
                if (_orders.TryGetValue(orderId, out var ord))
                {
                    if (ord.Status == OrderStatus.Submitted || ord.Status == OrderStatus.Pending)
                    {
                        ord.Status = OrderStatus.Canceled;
                        if (OnOrderStatusChanged != null)
                        {
                            OnOrderStatusChanged.Invoke(orderId, OrderStatus.Canceled);
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public void Step(DateTime currentTime)
        {
            lock (_syncLock)
            {
                _currentTime = currentTime;

                // 首次步进或时间发生跃迁时初始化换日阈值
                if (_nextRollTimeUtc == DateTime.MinValue || currentTime < _currentDate)
                {
                    InitializeRollThreshold(currentTime);
                }

                // 时间戳阈值比对：检测是否到达交易所本地 00:00:00 换日时刻
                if (currentTime >= _nextRollTimeUtc)
                {
                    ExecuteDayRollover(currentTime);
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
            lock (_syncLock)
            {
                _cash = cash;
                _positions.Clear();
                _positionsSnapshot = null;
                foreach (var kvp in positions)
                {
                    if (kvp.Value != 0m)
                    {
                        _positions[kvp.Key] = new PositionData(kvp.Key)
                        {
                            TotalQuantity = kvp.Value,
                            AvailableQuantity = kvp.Value
                        };
                    }
                }
            }
        }

        /// <inheritdoc />
        public string SubmitOrder(OrderRequest request)
        {
            // 默认使用委托中指定的价格作为基准价立即执行回测撮合
            return ExecuteOrder(request, request.Price, null);
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
            _orders[orderId] = record;

            lock (_syncLock)
            {
                record.Status = OrderStatus.Submitted;
                if (OnOrderStatusChanged != null)
                {
                    OnOrderStatusChanged.Invoke(orderId, OrderStatus.Submitted);
                }

                // 1. 基础参数有效性校验
                if (request.Quantity <= 0m || basePrice <= 0.0)
                {
                    RejectOrder(record, "Quantity or basePrice must be positive.");
                    return orderId;
                }

                var pos = _positions.GetOrAdd(request.Symbol, s => new PositionData(s));

                // 2. 卖空权限与可用持仓检查
                if (!_config.AllowShort && request.IsSell)
                {
                    if (pos.TotalQuantity <= 0m || request.Quantity > pos.AvailableQuantity)
                    {
                        RejectOrder(record, $"Insufficient available position. Total={pos.TotalQuantity}, Available={pos.AvailableQuantity}, Request={request.Quantity}");
                        return orderId;
                    }
                }

                // 3. 成交量流动性检查 (VolumeLimit)
                if (bar != null)
                {
                    if (_config.OnCheckVolume != null)
                    {
                        if (!_config.OnCheckVolume(request, bar))
                        {
                            RejectOrder(record, "Rejected by OnCheckVolume hook (liquidity constraint).");
                            return orderId;
                        }
                    }
                    else if (_config.VolumeLimit > 0.0)
                    {
                        double barVolume = bar.GetDouble("volume");
                        double maxAllowed = barVolume * _config.VolumeLimit;
                        if ((double)request.Quantity > maxAllowed)
                        {
                            RejectOrder(record, $"Exceeded volume limit: {request.Quantity} > {maxAllowed} (BarVolume={barVolume} * {_config.VolumeLimit})");
                            return orderId;
                        }
                    }
                }

                // 4. 涨跌停封板检查 (CheckLimit)
                if (_config.CheckLimit && bar != null)
                {
                    if (_config.OnCheckLimit != null)
                    {
                        if (!_config.OnCheckLimit(request, bar))
                        {
                            RejectOrder(record, "Rejected by OnCheckLimit hook (price limit hit).");
                            return orderId;
                        }
                    }
                }

                // 5. 滑点撮合计算
                double fillPrice;
                if (_config.OnCalculateSlippage != null && bar != null)
                {
                    fillPrice = _config.OnCalculateSlippage(request, basePrice, bar);
                }
                else
                {
                    fillPrice = request.IsBuy
                        ? basePrice * (1.0 + _config.Slippage)
                        : basePrice * (1.0 - _config.Slippage);
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

                // 8. 资金充足性校验
                bool isSpotStock = Math.Abs(marginRate - 1.0) < 1e-6 && !_config.AllowShort;
                double requiredCash;

                if (isSpotStock)
                {
                    requiredCash = request.IsBuy ? (turnover + commission) : commission;
                }
                else
                {
                    // 衍生品/期货：开仓占用保证金+费用；平仓仅需支付手续费
                    bool isOpening = (request.IsBuy && pos.TotalQuantity >= 0m) || (request.IsSell && pos.TotalQuantity <= 0m);
                    requiredCash = isOpening ? (turnover * marginRate + commission) : commission;
                }

                if (_cash < requiredCash)
                {
                    RejectOrder(record, $"Insufficient cash: available={_cash:F2}, required={requiredCash:F2}");
                    return orderId;
                }

                // 9. 状态跃迁：扣款与更新持仓
                DateTime tradeTime = _currentTime != DateTime.MinValue 
                    ? _currentTime 
                    : (_currentDate != DateTime.MinValue ? _currentDate : DateTime.UtcNow);

                if (isSpotStock)
                {
                    // 现货全额模式状态跃迁
                    if (request.IsBuy)
                    {
                        _cash -= (turnover + commission);

                        decimal newTotal = pos.TotalQuantity + request.Quantity;
                        pos.AverageCost = ((double)pos.TotalQuantity * pos.AverageCost + (double)request.Quantity * fillPrice) / (double)newTotal;
                        pos.TotalQuantity = newTotal;

                        if (settleDays == 0)
                        {
                            pos.AvailableQuantity += request.Quantity;
                        }
                        else
                        {
                            DateTime localTradeDate = ToExchangeDate(tradeTime);
                            pos.PendingLots.Add(new PendingLot(request.Quantity, localTradeDate.AddDays(settleDays)));
                        }
                    }
                    else
                    {
                        _cash += (turnover - commission);
                        pos.TotalQuantity -= request.Quantity;
                        pos.AvailableQuantity -= request.Quantity;
                        if (pos.TotalQuantity == 0m)
                        {
                            pos.AverageCost = 0.0;
                            if (pos.PendingLots.Count == 0)
                            {
                                _positions.TryRemove(request.Symbol, out _);
                            }
                        }
                    }
                }
                else
                {
                    // 期货 / 衍生品双向保证金模式状态跃迁
                    _cash -= commission;

                    if (request.IsBuy)
                    {
                        if (pos.TotalQuantity >= 0m)
                        {
                            // 多头加仓
                            double margin = turnover * marginRate;
                            _cash -= margin;

                            decimal newTotal = pos.TotalQuantity + request.Quantity;
                            pos.AverageCost = ((double)pos.TotalQuantity * pos.AverageCost + (double)request.Quantity * fillPrice) / (double)newTotal;
                            pos.TotalQuantity = newTotal;
                            pos.AvailableQuantity += request.Quantity;
                        }
                        else
                        {
                            // 空头平仓
                            decimal closeQty = Math.Min(request.Quantity, Math.Abs(pos.TotalQuantity));
                            double realizedPnL = (pos.AverageCost - fillPrice) * (double)closeQty * multiplier;
                            double marginReleased = (double)closeQty * pos.AverageCost * multiplier * marginRate;

                            _cash += (marginReleased + realizedPnL);
                            pos.TotalQuantity += closeQty;
                            pos.AvailableQuantity += closeQty;

                            if (pos.TotalQuantity == 0m)
                            {
                                pos.AverageCost = 0.0;
                                _positions.TryRemove(request.Symbol, out _);
                            }
                        }
                    }
                    else
                    {
                        if (pos.TotalQuantity <= 0m)
                        {
                            // 空头加仓
                            double margin = turnover * marginRate;
                            _cash -= margin;

                            decimal newTotal = Math.Abs(pos.TotalQuantity) + request.Quantity;
                            pos.AverageCost = ((double)Math.Abs(pos.TotalQuantity) * pos.AverageCost + (double)request.Quantity * fillPrice) / (double)newTotal;
                            pos.TotalQuantity -= request.Quantity;
                            pos.AvailableQuantity += request.Quantity;
                        }
                        else
                        {
                            // 多头平仓
                            decimal closeQty = Math.Min(request.Quantity, pos.TotalQuantity);
                            double realizedPnL = (fillPrice - pos.AverageCost) * (double)closeQty * multiplier;
                            double marginReleased = (double)closeQty * pos.AverageCost * multiplier * marginRate;

                            _cash += (marginReleased + realizedPnL);
                            pos.TotalQuantity -= closeQty;
                            pos.AvailableQuantity -= closeQty;

                            if (pos.TotalQuantity == 0m)
                            {
                                pos.AverageCost = 0.0;
                                _positions.TryRemove(request.Symbol, out _);
                            }
                        }
                    }
                }

                pos.LastPrice = fillPrice;
                _positionsSnapshot = null;

                // 10. 触发成交与状态变更事件（热路径短路防无谓堆分配）
                record.Status = OrderStatus.Filled;
                if (OnOrderStatusChanged != null)
                {
                    OnOrderStatusChanged.Invoke(orderId, OrderStatus.Filled);
                }

                if (OnTrade != null)
                {
                    OnTrade.Invoke(new ExecutionReport(
                        orderId,
                        request.Symbol,
                        fillPrice,
                        request.Quantity,
                        commission,
                        tradeTime));
                }

                if (OnPositionChanged != null)
                {
                    OnPositionChanged.Invoke(new PositionEvent(
                        request.Symbol,
                        pos.TotalQuantity,
                        pos.AverageCost,
                        tradeTime));
                }

                return orderId;
            }
        }

        private void RejectOrder(OrderRecord record, string reason)
        {
            record.Status = OrderStatus.Rejected;
            record.RejectReason = reason;
            if (OnOrderStatusChanged != null)
            {
                OnOrderStatusChanged.Invoke(record.OrderId, OrderStatus.Rejected);
            }
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

        private class PositionData
        {
            public string Symbol { get; }
            public decimal TotalQuantity { get; set; }
            public decimal AvailableQuantity { get; set; }
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

            public OrderRecord(string orderId, OrderRequest request)
            {
                OrderId = orderId;
                Request = request;
            }
        }
    }
}
