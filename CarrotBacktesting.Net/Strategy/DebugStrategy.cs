using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Strategy
{
    public class DebugStrategy : IStrategy
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public DebugStrategy()
        {

        }

        /// <summary>
        /// 回测引擎开始运行调用方法
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnStart(StrategyContext strategyContext)
        {
            Console.WriteLine("EmptyStrategy.OnStart()");
        }

        /// <summary>
        /// 回测引擎结束运行调用方法
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnEnd(StrategyContext strategyContext)
        {
            Console.WriteLine("EmptyStrategy.OnEnd()");
        }

        /// <summary>
        /// 回测引擎正常工作时Tick更新时在此函数更新k线/指标类等数据
        /// </summary>
        /// <param name="marketFrame"></param>
        /// <param name="marketEventArgs"></param>
        public void OnTick(MarketFrame marketFrame, MarketEventArgs marketEventArgs)
        {
            Console.WriteLine("EmptyStrategy.OnTick(sender, args)");
        }

        /// <summary>
        /// 回测引擎正常工作时Tick更新时在此函数更新本Tick策略
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnNext(StrategyContext strategyContext)
        {
            Console.WriteLine("EmptyStrategy.OnNext()");
        }

        /// <summary>
        /// 回测引擎更新委托单回调方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="operation"></param>
        public void OnOrderUpdate(OrderManager sender, OrderEventArgs operation)
        {
            Console.WriteLine("EmptyStrategy.OnOrderUpdate(sender, args)");
        }

        /// <summary>
        /// 回测引擎更新交易状态回调方法
        /// </summary>
        /// <param name="exchange"></param>
        /// <param name="tradeEventArgs"></param>
        public void OnTradeUpdate(BackTestingExchange exchange, TradeEventArgs tradeEventArgs)
        {
            Console.WriteLine("EmptyStrategy.OnTradeUpdate(sender, args)");
        }
    }
}
