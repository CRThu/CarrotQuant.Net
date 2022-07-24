using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Order
{
    /// <summary>
    /// 常规委托
    /// </summary>
    public class GeneralOrder
    {
        /// <summary>
        /// 委托单自增id
        /// </summary>
        public int OrderId { get; set; }

        /// <summary>
        /// 委托单状态
        /// </summary>
        public GeneralOrderStatus Status { get; set; }

        /// <summary>
        /// 头寸方向(买入/卖出)
        /// </summary>
        public OrderDirection Direction { get; set; }

        /// <summary>
        /// 委托单类型(限价/市价)
        /// </summary>
        public OrderType Type { get; set; }

        /// <summary>
        /// 股票代码
        /// </summary>
        public string StockCode { get; set; }

        /// <summary>
        /// 头寸大小
        /// </summary>
        public double Size { get; set; }

        /// <summary>
        /// 委托限价/市价
        /// </summary>
        public double Price { get; set; }
    }

    /// <summary>
    /// 委托单状态
    /// </summary>
    public enum GeneralOrderStatus
    {
        Pending,    // 待成交/部分成交
        Deal,       // 已成交
        Cancelled   // 已取消
    }

    /// <summary>
    /// 委托单方向(买入/卖出)
    /// </summary>
    public enum OrderDirection
    {
        Buy,
        Sell,
    }

    /// <summary>
    /// 委托单类型(限价/市价)
    /// </summary>
    public enum OrderType
    {
        LimitOrder,
        MarketOrder,
    }
}
