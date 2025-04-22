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
    public class EmptyStrategy : IStrategy
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public EmptyStrategy()
        {

        }

        /// <summary>
        /// 回测引擎开始运行调用方法
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnStart(StrategyContext strategyContext)
        {

        }

        /// <summary>
        /// 回测引擎结束运行调用方法
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnEnd(StrategyContext strategyContext)
        {

        }

        /// <summary>
        /// 回测引擎正常工作时Tick更新时在此函数更新k线/指标类等数据
        /// </summary>
        /// <param name="marketFrame"></param>
        /// <param name="marketEventArgs"></param>
        public void OnTick(MarketFrame marketFrame, MarketEventArgs marketEventArgs)
        {

        }

        /// <summary>
        /// 回测引擎正常工作时Tick更新时在此函数更新本Tick策略
        /// </summary>
        /// <param name="strategyContext"></param>
        public void OnNext(StrategyContext strategyContext)
        {

        }

        /// <summary>
        /// 回测引擎更新委托单回调方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="operation"></param>
        public void OnOrderUpdate(OrderManager sender, OrderEventArgs operation)
        {

        }

        /// <summary>
        /// 回测引擎更新交易状态回调方法
        /// </summary>
        /// <param name="exchange"></param>
        /// <param name="tradeEventArgs"></param>
        public void OnTradeUpdate(BackTestingExchange exchange, TradeEventArgs tradeEventArgs)
        {

        }
    }
}
