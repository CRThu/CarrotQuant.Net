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
        /// 股票名称
        /// </summary>
        public string ShareName { get; set; }
        /// <summary>
        /// 头寸大小
        /// </summary>
        public double Size { get; set; }

        /// <summary>
        /// 委托限价
        /// </summary>
        public double LimitPrice { get; set; }

        /// <summary>
        /// 头寸方向
        /// </summary>
        public OrderDirection Direction { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="shareName"></param>
        /// <param name="size"></param>
        /// <param name="position"></param>
        public GeneralOrder(string shareName, double price, double size, OrderDirection position)
        {
            ShareName = shareName;
            LimitPrice = price;
            Size = size;
            Direction = position;

            if (Size < 0)
            {
                Size = -Size;
                Direction = Direction == OrderDirection.Long ? OrderDirection.Short : OrderDirection.Long;
            }
        }
    }
}
