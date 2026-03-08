using CarrotBacktesting.NET.Engine;

namespace CarrotBacktesting.NET.Strategy
{
    /// <summary>
    /// 默认极简市场策略接口
    /// </summary>
    public interface IMarketStrategy : IStrategy
    {
        /// <summary>
        /// 检查市场状态
        /// </summary>
        /// <param name="context">扫描上下文</param>
        /// <returns>市场决策结果</returns>
        MarketResult? CheckMarket(MarketStrategyContext context);
    }

    /// <summary>
    /// 泛型专业市场策略接口，支持强类型状态输出
    /// </summary>
    /// <typeparam name="TMetrics">自定义指标类型</typeparam>
    public interface IMarketStrategy<TMetrics> : IMarketStrategy where TMetrics : class, new()
    {
        /// <summary>
        /// 检查市场状态
        /// </summary>
        /// <param name="context">扫描上下文</param>
        /// <returns>泛型市场决策结果</returns>
        new MarketResult<TMetrics> CheckMarket(MarketStrategyContext context);

        /// <summary>
        /// 显式实现基础接口方法，将其路由至泛型方法
        /// </summary>
        MarketResult? IMarketStrategy.CheckMarket(MarketStrategyContext context) => CheckMarket(context);
    }
}
