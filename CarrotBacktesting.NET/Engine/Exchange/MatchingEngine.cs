using System;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Exchange;

/// <summary>
/// 纯计算撮合与市场摩擦内核。
/// 专职负责限价触碰判定、价格改善、滑点冲击模型重估、交易费计算与涨跌停/流动性风控合规拦截。
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
    /// 评估新提交订单（市价单或即时触碰限价单）的撮合结果。
    /// </summary>
    /// <param name="request">订单委托意图。</param>
    /// <param name="basePrice">基准市价（通常为当前最新行情价格或当步价格）。</param>
    /// <param name="bar">当前时间步行情数据（供流动性与涨跌停拦截）。</param>
    /// <param name="multiplier">合约乘数。</param>
    /// <param name="tradeTime">撮合发生的时间戳。</param>
    /// <returns>撮合判定结果。</returns>
    public MatchResult EvaluateImmediateOrder(
        OrderRequest request,
        double basePrice,
        IDataRow? bar,
        double multiplier,
        DateTime tradeTime)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Quantity <= 0m || basePrice <= 0.0 || double.IsNaN(basePrice) || double.IsInfinity(basePrice))
        {
            return MatchResult.Rejected("Quantity or basePrice must be positive and finite.");
        }

        // 1. 成交量流动性硬约束检查 (VolumeLimit)
        if (bar != null && !CheckLiquidity(request, bar, out var reason))
        {
            return MatchResult.Rejected(reason);
        }

        // 2. 涨跌停封板拦截检查 (CheckLimit)
        if (bar != null && _config.CheckLimit && !CheckPriceLimit(request, bar, out reason))
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
        double fillPrice = CalculateTakerFillPrice(request, basePrice, bar);

        // 5. 计算手续费
        double turnover = (double)request.Quantity * fillPrice * multiplier;
        double commission = CalculateCommission(request, turnover);

        return MatchResult.Filled(fillPrice, request.Quantity, commission, tradeTime);
    }

    /// <summary>
    /// 评估挂单簿中排队的限价单在给定最新行情下的被动撮合结果（Maker 规则）。
    /// </summary>
    /// <param name="order">排队挂单实体。</param>
    /// <param name="marketPrice">标的当前市场价格。</param>
    /// <param name="bar">标的当前行情行（可选）。</param>
    /// <param name="tradeTime">撮合发生的时间戳。</param>
    /// <returns>撮合判定结果。</returns>
    public MatchResult EvaluatePendingLimitOrder(
        SimulatedOrder order,
        double marketPrice,
        IDataRow? bar,
        DateTime tradeTime)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (marketPrice <= 0.0 || double.IsNaN(marketPrice) || double.IsInfinity(marketPrice))
        {
            return MatchResult.NoFill();
        }

        var request = order.Request;

        // 限价触碰判定（买单市价 <= 委托限价；卖单市价 >= 委托限价）
        bool canFill = request.IsBuy
            ? marketPrice <= request.Price
            : marketPrice >= request.Price;

        if (!canFill)
        {
            return MatchResult.NoFill();
        }

        // 成交量流动性检查 (VolumeLimit)
        if (bar != null && !CheckLiquidity(request, bar, out var reason))
        {
            return MatchResult.Rejected(reason);
        }

        // 涨跌停封板拦截检查 (CheckLimit)
        if (bar != null && _config.CheckLimit && !CheckPriceLimit(request, bar, out reason))
        {
            return MatchResult.Rejected(reason);
        }

        // Maker 限价挂单成交价：严格遵循委托限价保护（成交价 = 委托限价）
        double fillPrice = request.Price;

        double turnover = (double)request.Quantity * fillPrice * order.Multiplier;
        double commission = CalculateCommission(request, turnover);

        return MatchResult.Filled(fillPrice, request.Quantity, commission, tradeTime);
    }

    private double CalculateTakerFillPrice(OrderRequest request, double basePrice, IDataRow? bar)
    {
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

        // 限价委托保护：成交价绝严禁劣于限价（买入成交价 <= LimitPrice，卖出成交价 >= LimitPrice）
        if (request.Type == OrderType.Limit && request.Price > 0.0)
        {
            fillPrice = request.IsBuy
                ? Math.Min(fillPrice, request.Price)
                : Math.Max(fillPrice, request.Price);
        }

        return fillPrice;
    }

    private double CalculateCommission(OrderRequest request, double turnover)
    {
        if (_config.OnCalculateFee != null)
        {
            return _config.OnCalculateFee(request, turnover);
        }
        return Math.Max(_config.MinFee, turnover * _config.FeeRate);
    }

    private bool CheckLiquidity(OrderRequest request, IDataRow bar, out string reason)
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
        else if (_config.VolumeLimit > 0.0)
        {
            double barVolume = bar.GetDouble("volume");
            double maxAllowed = barVolume * _config.VolumeLimit;
            if ((double)request.Quantity > maxAllowed)
            {
                reason = $"Exceeded volume limit: {request.Quantity} > {maxAllowed} (BarVolume={barVolume} * {_config.VolumeLimit})";
                return false;
            }
        }
        return true;
    }

    private bool CheckPriceLimit(OrderRequest request, IDataRow bar, out string reason)
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
    public DateTime Time { get; }

    private MatchResult(
        bool isFilled,
        bool isPending,
        bool isRejected,
        string? rejectReason,
        double fillPrice,
        decimal fillQuantity,
        double commission,
        DateTime time)
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

    public static MatchResult Filled(double fillPrice, decimal fillQuantity, double commission, DateTime time) =>
        new(true, false, false, null, fillPrice, fillQuantity, commission, time);

    public static MatchResult PendingQueue() =>
        new(false, true, false, null, 0.0, 0m, 0.0, DateTime.MinValue);

    public static MatchResult NoFill() =>
        new(false, false, false, null, 0.0, 0m, 0.0, DateTime.MinValue);

    public static MatchResult Rejected(string reason) =>
        new(false, false, true, reason, 0.0, 0m, 0.0, DateTime.MinValue);
}
