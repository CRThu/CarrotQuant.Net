using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Portfolio
{
    /// <summary>
    /// 常规委托
    /// </summary>
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
        /// 委托限价
        /// </summary>
        public double LimitPrice { get; set; }

        /// <summary>
        /// 头寸方向
        /// </summary>
        public TradeDirectionEnum Direction { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="exchangeName"></param>
        /// <param name="shareName"></param>
        /// <param name="size"></param>
        /// <param name="position"></param>
        public GeneralOrder(string exchangeName, string shareName, double price, double size, TradeDirectionEnum position)
        {
            ExchangeName = exchangeName;
            ShareName = shareName;
            LimitPrice = price;
            Size = size;
            Direction = position;

            if (Size < 0)
            {
                Size = -Size;
                Direction = Direction == TradeDirectionEnum.Long ? TradeDirectionEnum.Short : TradeDirectionEnum.Long;
            }
        }
    }
}
