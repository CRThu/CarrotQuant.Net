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
    public struct GeneralOrder
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
        /// 初始委托头寸大小
        /// </summary>
        public double OrderSize { get; set; }

        /// <summary>
        /// 成交头寸大小
        /// </summary>
        public double DealSize { get; set; }

        /// <summary>
        /// 剩余未成交头寸大小
        /// </summary>
        public double PendingSize => OrderSize - DealSize;

        /// <summary>
        /// 委托限价(委托单类型为市价时此属性无效)
        /// </summary>
        public double Price { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="orderId">委托单id</param>
        /// <param name="stockCode">股票代码</param>
        /// <param name="direction">头寸方向</param>
        /// <param name="size">头寸大小</param>
        /// <param name="type">委托单类型(限价/市价)</param>
        /// <param name="price">委托限价(委托单类型为市价时此属性无效)</param>
        public GeneralOrder(int orderId, string stockCode, OrderDirection direction, double size, OrderType type, double price = 0)
        {
            OrderId = orderId;
            StockCode = stockCode;
            Direction = direction;
            OrderSize = size;
            Type = type;
            Price = price;
            Status = GeneralOrderStatus.Pending;
            DealSize = 0;
        }

        /// <summary>
        /// 成交委托单
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Trade()
        {
            if (Status == GeneralOrderStatus.Cancelled)
            {
                throw new InvalidOperationException("委托单已被取消.");
            }
            DealSize = PendingSize;
            Status = GeneralOrderStatus.Deal;
        }

        /// <summary>
        /// 成交委托单
        /// </summary>
        /// <param name="size">成交头寸大小</param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Trade(double size)
        {
            if (PendingSize < size)
            {
                throw new InvalidOperationException("委托单头寸成交大小大于委托大小.");
            }
            if (Status == GeneralOrderStatus.Cancelled)
            {
                throw new InvalidOperationException("委托单已被取消.");
            }
            DealSize += size;
            if (PendingSize == 0)
            {
                Status = GeneralOrderStatus.Deal;
            }
        }
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
