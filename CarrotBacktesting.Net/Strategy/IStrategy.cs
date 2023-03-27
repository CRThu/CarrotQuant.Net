using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio.Order;

namespace CarrotBacktesting.Net.Strategy
{
    /// <summary>
    /// 策略接口
    /// </summary>
    public interface IStrategy
    {
        /// <summary>
        /// 回测引擎开始运行调用方法
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnStart(StrategyContext strategyContext);

        /// <summary>
        /// 回测引擎结束运行调用方法
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnEnd(StrategyContext strategyContext);

        /// <summary>
        /// 回测引擎正常工作时Tick更新时在此函数更新k线/指标类等数据
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnTick(StrategyContext strategyContext);

        /// <summary>
        /// 回测引擎正常工作时Tick更新时在此函数更新本Tick策略
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnNext(StrategyContext strategyContext);

        /// <summary>
        /// 回测引擎更新委托单回调方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="operation"></param>
        public void OnOrderUpdate(OrderManager sender, OrderEventArgs operation);

        /// <summary>
        /// 回测引擎更新交易状态回调方法
        /// </summary>
        /// <param name="exchange"></param>
        /// <param name="tradeEventArgs"></param>
        public void OnTradeUpdate(BackTestingExchange exchange, TradeEventArgs tradeEventArgs);
    }
}