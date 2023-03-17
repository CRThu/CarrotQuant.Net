using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio.Analyzer;
using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Portfolio.Position;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio
{
    /// <summary>
    /// 投资组合管理类
    /// </summary>
    public class PortfolioManager
    {
        /// <summary>
        /// 委托单管理器
        /// </summary>
        public OrderManager OrderManager { get; set; }

        /// <summary>
        /// 头寸管理器
        /// </summary>
        public PositionManager PositionManager { get; set; }

        /// <summary>
        /// 投资组合分析器
        /// </summary>
        public AnalyzerManager Analyzer { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PortfolioManager(SimulationOptions options)
        {
            OrderManager = new OrderManager();
            PositionManager = new PositionManager(options);
            Analyzer = new AnalyzerManager(options, this);
            //EventRegister();
        }

        //public void EventRegister()
        //{
        //    // 交割单记录器事件
        //    PositionManager.CashUpdateEvent += TransactionLogger.SetCash;
        //    PositionManager.PositionUpdateEvent += TransactionLogger.AddTransaction;
        //}

        /// <summary>
        /// 创建委托单
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="price">委托限价(委托单类型为市价时此属性无效)</param>
        /// <param name="size">头寸大小</param>
        /// <param name="direction">头寸方向(买入/卖出)</param>
        /// <param name="type">头寸类型(限价/市价)</param>
        /// <returns>委托单id号</returns>
        public int CreateOrder(string stockCode, OrderDirection direction, double size, OrderType type, double price = 0)
            => OrderManager.CreateOrder(stockCode, direction, size, type, price);

        /// <summary>
        /// 取消委托单
        /// </summary>
        /// <param name="orderId">委托单id</param>
        /// <returns>返回是否成功取消委托单, 若失败可能已被成交或取消</returns>
        public bool CancelOrder(int orderId)
            => OrderManager.TryCancelOrder(orderId);

        /// <summary>
        /// 市场更新事件回调
        /// </summary>
        /// <param name="data">市场数据更新</param>
        /// <param name="marketEventArgs">市场更新事件参数</param>
        public void OnMarketUpdate(MarketFrame data, MarketEventArgs marketEventArgs)
        {
            Analyzer.OnMarketUpdate(data, marketEventArgs);
        }

        /// <summary>
        /// 交易所成交更新事件回调
        /// </summary>
        /// <param name="exchange">交易所实例</param>
        /// <param name="tradeEventArgs">成交事件参数</param>
        public void OnTradeUpdate(BackTestingExchange exchange, TradeEventArgs tradeEventArgs)
        {
            OrderManager.OnTradeUpdate(exchange, tradeEventArgs);
            PositionManager.OnTradeUpdate(exchange, tradeEventArgs);
            Analyzer.OnTradeUpdate(exchange, tradeEventArgs);
        }
    }
}
