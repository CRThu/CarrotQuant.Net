using System;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Exchange;

/// <summary>
/// 纯计算撮合与市场摩擦内核。
/// 专职负责限价触碰判定、价格改善、滑点冲击模型重估与风控合规拦截。
/// 纯函数设计，不持有状态，具备确定性与高可测性。
/// </summary>
internal sealed class MatchingEngine
{
    private readonly BrokerConfig _config;

    public MatchingEngine(BrokerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// 评估新提交订单（市价单或即时触碰限价单）的撮合结果（零 GC 寄存器传参热路径）。
    /// </summary>
    /// <param name="request">订单委托意图。</param>
    /// <param name="basePrice">基准市价（通常为当前最新行情价格或当步价格）。</param>
    /// <param name="bar">当前时间步行情切片（供流动性与风控拦截）。</param>
    /// <param name="multiplier">合约乘数。</param>
    /// <param name="tradeTime">撮合发生的时间戳。</param>
    /// <returns>撮合判定结果。</returns>
    public MatchResult EvaluateImmediateOrder(
        OrderRequest request,
        double basePrice,
        in BarSnapshot bar,
        double multiplier,
        MarketTime tradeTime)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Quantity <= 0m || basePrice <= 0.0 || double.IsNaN(basePrice) || double.IsInfinity(basePrice))
        {
            return MatchResult.Rejected("Quantity or basePrice must be positive and finite.");
        }

        // 1. 成交量流动性硬约束检查 (VolumeLimit)
        if (bar.IsValid && !CheckLiquidity(request, in bar, out var reason))
        {
            return MatchResult.Rejected(reason);
        }

        // 2. 涨跌停封板拦截检查 (CheckLimit)
        if (bar.IsValid && _config.CheckLimit && !CheckPriceLimit(request, in bar, out reason))
        {
            return MatchResult.Rejected(reason);
        }

        // 3. 判断限价单是否未触碰市价（未成交应入挂单簿排队）
        if (request.Type == OrderType.Limit && request.Price > 0.0)
        {
            bool isUnfilled = (request.IsBuy && request.Price < basePrice) ||
                              (!request.IsBuy && request.Price > basePrice);
            if (isUnfilled)
            {
                return MatchResult.PendingQueue();
            }
        }

        // 4. 即时成交（Taker 规则）：计算滑点
        double fillPrice = CalculateTakerFillPrice(request, basePrice, in bar, tradeTime);

        return MatchResult.Filled(fillPrice, request.Quantity, tradeTime);
    }

    /// <summary>
    /// 评估挂单簿中排队的限价单在给定最新行情下的被动撮合结果（Maker 规则，零 GC 寄存器传参热路径）。
    /// </summary>
    /// <param name="order">排队挂单实体。</param>
    /// <param name="marketPrice">标的当前市场价格。</param>
    /// <param name="bar">标的当前行情切片。</param>
    /// <param name="tradeTime">撮合发生的时间戳。</param>
    /// <returns>撮合判定结果。</returns>
    public MatchResult EvaluatePendingLimitOrder(
        SimulatedOrder order,
        double marketPrice,
        in BarSnapshot bar,
        MarketTime tradeTime)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (marketPrice <= 0.0 || double.IsNaN(marketPrice) || double.IsInfinity(marketPrice))
        {
            return MatchResult.NoFill();
        }

        var request = order.Request;
        
        // 触碰判定：买单看 LowOr，卖单看 HighOr；若区间极值缺失，平滑自适应回退至基准价格
        double execPrice = bar.CloseOr(bar.OpenOr(marketPrice));
        bool canFill = request.IsBuy
            ? bar.LowOr(execPrice) <= request.Price
            : bar.HighOr(execPrice) >= request.Price;

        if (!canFill)
        {
            return MatchResult.NoFill();
        }

        // 成交量流动性检查 (VolumeLimit)
        if (bar.IsValid && !CheckLiquidity(request, in bar, out var reason))
        {
            return MatchResult.Rejected(reason);
        }

        // 涨跌停封板拦截检查 (CheckLimit)
        if (bar.IsValid && _config.CheckLimit && !CheckPriceLimit(request, in bar, out reason))
        {
            return MatchResult.Rejected(reason);
        }

        // Maker 限价挂单成交价：
        // 若开盘出现对委托方更有利的跳空缺口（低开买入 / 高开卖出），享受更优开盘价成交（Price Improvement）；
        // 否则严格遵循委托限价（成交价 = 委托限价）。
        double fillPrice = request.Price;
        if (bar.HasOpen && bar.Open > 0.0)
        {
            fillPrice = request.IsBuy
                ? Math.Min(request.Price, bar.Open)
                : Math.Max(request.Price, bar.Open);
        }

        return MatchResult.Filled(fillPrice, request.Quantity, tradeTime);
    }

    private double CalculateTakerFillPrice(
        OrderRequest request,
        double basePrice,
        in BarSnapshot bar,
        MarketTime tradeTime)
    {
        double fillPrice;
        if (_config.OnCalculateSlippage != null && bar.IsValid)
        {
            fillPrice = _config.OnCalculateSlippage(request, basePrice, bar);
        }
        else
        {
            fillPrice = request.IsBuy
                ? basePrice * (1.0 + _config.Slippage)
                : basePrice * (1.0 - _config.Slippage);
        }

        // 限价委托保护：成交价严禁劣于限价（买入成交价 <= LimitPrice，卖出成交价 >= LimitPrice）
        if (request.Type == OrderType.Limit && request.Price > 0.0)
        {
            fillPrice = request.IsBuy
                ? Math.Min(fillPrice, request.Price)
                : Math.Max(fillPrice, request.Price);
        }

        return fillPrice;
    }

    private bool CheckLiquidity(
        OrderRequest request,
        in BarSnapshot bar,
        out string reason)
    {
        reason = string.Empty;
        if (_config.OnCheckVolume != null)
        {
            if (!_config.OnCheckVolume(request, bar))
            {
                reason = "Rejected by OnCheckVolume hook (liquidity constraint).";
                return false;
            }
        }
        else if (_config.VolumeLimit > 0.0 && bar.HasVolume && bar.Volume >= 0.0)
        {
            double maxAllowed = bar.Volume * _config.VolumeLimit;
            if ((double)request.Quantity > maxAllowed)
            {
                reason = $"Exceeded volume limit: {request.Quantity} > {maxAllowed} (BarVolume={bar.Volume} * {_config.VolumeLimit})";
                return false;
            }
        }
        return true;
    }

    private bool CheckPriceLimit(
        OrderRequest request,
        in BarSnapshot bar,
        out string reason)
    {
        reason = string.Empty;
        if (_config.OnCheckLimit != null)
        {
            if (!_config.OnCheckLimit(request, bar))
            {
                reason = "Rejected by OnCheckLimit hook (price limit hit).";
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// 撮合判定执行结果模型。
/// </summary>
internal readonly struct MatchResult
{
    public bool IsFilled { get; }
    public bool IsPending { get; }
    public bool IsRejected { get; }
    public string? RejectReason { get; }

    public double FillPrice { get; }
    public decimal FillQuantity { get; }
    public double Commission { get; }
    public MarketTime Time { get; }

    private MatchResult(
        bool isFilled,
        bool isPending,
        bool isRejected,
        string? rejectReason,
        double fillPrice,
        decimal fillQuantity,
        double commission,
        MarketTime time)
    {
        IsFilled = isFilled;
        IsPending = isPending;
        IsRejected = isRejected;
        RejectReason = rejectReason;
        FillPrice = fillPrice;
        FillQuantity = fillQuantity;
        Commission = commission;
        Time = time;
    }

    public static MatchResult Filled(double fillPrice, decimal fillQuantity, MarketTime time, double commission = 0.0) =>
        new(true, false, false, null, fillPrice, fillQuantity, commission, time);

    public static MatchResult Filled(double fillPrice, decimal fillQuantity, double commission, MarketTime time) =>
        new(true, false, false, null, fillPrice, fillQuantity, commission, time);

    public static MatchResult PendingQueue() =>
        new(false, true, false, null, 0.0, 0m, 0.0, MarketTime.MinValue);

    public static MatchResult NoFill() =>
        new(false, false, false, null, 0.0, 0m, 0.0, MarketTime.MinValue);

    public static MatchResult Rejected(string reason) =>
        new(false, false, true, reason, 0.0, 0m, 0.0, MarketTime.MinValue);
}
