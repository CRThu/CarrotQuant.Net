using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 成交事件参数类
    /// </summary>
    public class TradeEventArgs
    {
        /// <summary>
        /// 委托单id
        /// </summary>
        public int OrderId { get; set; }

        /// <summary>
        /// 成交时间
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// 股票代码
        /// </summary>
        public string StockCode { get; set; }

        /// <summary>
        /// 头寸方向(买入/卖出)
        /// </summary>
        public OrderDirection Direction { get; set; }

        /// <summary>
        /// 成交价格
        /// </summary>
        public double Price { get; set; }

        /// <summary>
        /// 成交数量
        /// </summary>
        public double Volume { get; set; }


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="orderId">委托单id</param>
        /// <param name="time">成交时间</param>
        /// <param name="stockCode">股票代码</param>
        /// <param name="direction">头寸方向</param>
        /// <param name="price">成交价格</param>
        /// <param name="volume">成交数量</param>
        public TradeEventArgs(int orderId, DateTime time, string stockCode, OrderDirection direction, double price, double volume)
        {
            OrderId = orderId;
            Time = time;
            StockCode = stockCode;
            Direction = direction;
            Price = price;
            Volume = volume;
        }
    }
}
