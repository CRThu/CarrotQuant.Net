using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Portfolio
{
    public class GeneralOrder
    {
        /// <summary>
        /// 交易所名称
        /// </summary>
        public string ExchangeName { get; set; }
        /// <summary>
        /// 股票名称
        /// </summary>
        public string ShareName { get; set; }
        /// <summary>
        /// 头寸大小
        /// </summary>
        public double Size { get; set; }
        /// <summary>
        /// 头寸方向
        /// </summary>
        public PositionEnum Position { get; set; }

        /// <summary>
        /// 委托单状态
        /// </summary>
        public OrderStatus Status { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="exchangeName"></param>
        /// <param name="shareName"></param>
        /// <param name="size"></param>
        /// <param name="position"></param>
        public GeneralOrder(string exchangeName, string shareName, double size, PositionEnum position)
        {
            ExchangeName = exchangeName;
            ShareName = shareName;
            Size = size;
            Position = position;
        }
    }
}
