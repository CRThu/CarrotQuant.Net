using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 市场事件参数类
    /// </summary>
    public class MarketEventArgs
    {
        /// <summary>
        /// 市场时间
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// 市场是否开盘
        /// </summary>
        public bool IsMarketOpen { get; set; }


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="time">市场时间</param>
        /// <param name="isMarketOpen">市场是否开盘</param>
        public MarketEventArgs(DateTime time, bool isMarketOpen)
        {
            Time = time;
            IsMarketOpen = isMarketOpen;
        }
    }
}
