using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Position
{
    /// <summary>
    /// 头寸事件参数类
    /// </summary>
    public class PositionEventArgs
    {

        /// <summary>
        /// 股票代码
        /// </summary>
        public string StockCode { get; set; }

        /// <summary>
        /// 更新头寸大小
        /// </summary>
        public double Size { get; set; }

        /// <summary>
        /// 更新持仓成本
        /// </summary>
        public double Price { get; set; }


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="stockCode"></param>
        /// <param name="price"></param>
        /// <param name="size"></param>
        public PositionEventArgs(string stockCode, double price, double size)
        {
            StockCode = stockCode;
            Size = size;
            Price = price;
        }
    }
}
