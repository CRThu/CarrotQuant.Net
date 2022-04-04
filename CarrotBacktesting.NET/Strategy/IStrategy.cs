using CarrotBacktesting.NET.Engine;

namespace CarrotBacktesting.NET.Strategy
{
    /// <summary>
    /// 策略接口
    /// </summary>
    public interface IStrategy
    {
        /// <summary>
        /// 策略进入运行方法
        /// </summary>
        public void Start(StrategyContext strategyContext);
        /// <summary>
        /// 策略结束运行方法
        /// </summary>
        public void End(StrategyContext strategyContext);

        /// <summary>
        /// 策略正常工作时下一时间Next()前更新指标类等数据
        /// </summary>
        public void OnTick(StrategyContext strategyContext);
        /// <summary>
        /// 策略正常工作时下一时间更新(模拟时间重复运行)
        /// </summary>
        public void OnNext(StrategyContext strategyContext);
    }
}