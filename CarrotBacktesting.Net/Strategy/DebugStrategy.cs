using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            Debug.WriteLine("EmptyStrategy.OnStart()");
        }

        /// <summary>
        /// 回测引擎结束运行调用方法
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnEnd(StrategyContext strategyContext)
        {
            Debug.WriteLine("EmptyStrategy.OnEnd()");
        }

        /// <summary>
        /// 回测引擎正常工作时Tick更新时在此函数更新k线/指标类等数据
        /// </summary>
        /// <param name="marketFrame"></param>
        /// <param name="marketEventArgs"></param>
        public void OnTick(MarketFrame marketFrame, MarketEventArgs marketEventArgs)
        {
            Debug.WriteLine("EmptyStrategy.OnTick(sender, args)");
            Debug.WriteLine($"Strategy: Tick: {marketFrame.Time}");
            // Debug.WriteLine($"Strategy: StockCount: {marketFrame.Count}");
            // Debug.WriteLine($"Strategy: Stock: {string.Join(',', marketFrame.Codes)}");
        }

        /// <summary>
        /// 回测引擎正常工作时Tick更新时在此函数更新本Tick策略
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnNext(StrategyContext strategyContext)
        {
            Debug.WriteLine("EmptyStrategy.OnNext()");
        }

        /// <summary>
        /// 回测引擎更新委托单回调方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="operation"></param>
        public void OnOrderUpdate(OrderManager sender, OrderEventArgs operation)
        {
            Debug.WriteLine("EmptyStrategy.OnOrderUpdate(sender, args)");
        }

        /// <summary>
        /// 回测引擎更新交易状态回调方法
        /// </summary>
        /// <param name="exchange"></param>
        /// <param name="tradeEventArgs"></param>
        public void OnTradeUpdate(BackTestingExchange exchange, TradeEventArgs tradeEventArgs)
        {
            Debug.WriteLine("EmptyStrategy.OnTradeUpdate(sender, args)");
        }
    }
}
