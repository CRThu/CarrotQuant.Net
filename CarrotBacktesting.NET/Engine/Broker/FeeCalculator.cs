using System;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Engine.Broker;

/// <summary>
/// 券商交易手续费计算工具。
/// 统一计算两阶段挂单预冻结估算手续费与成交落账实际手续费，确保预估费率与实际扣费算法对齐。
/// </summary>
internal static class FeeCalculator
{
    /// <summary>
    /// 计算指定交易金额对应的券商手续费。
    /// </summary>
    /// <param name="turnover">成交/委托名义金额（数量 * 价格 * 合约乘数）。</param>
    /// <param name="request">原始委托请求（供自定义手续费委托分析）。</param>
    /// <param name="config">经纪商配置。若为 null 则返回 0.0。</param>
    /// <returns>计算出的手续费金额（非负）。</returns>
    public static double CalculateFee(double turnover, OrderRequest? request, BrokerConfig? config)
    {
        if (turnover <= 0.0 || config == null)
        {
            return 0.0;
        }

        if (config.OnCalculateFee != null)
        {
            return Math.Max(0.0, config.OnCalculateFee(request!, turnover));
        }

        double fee = turnover * config.FeeRate;
        return config.MinFee > 0.0 && fee < config.MinFee ? config.MinFee : fee;
    }
}
