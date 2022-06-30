using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataFeed
{
    /// <summary>
    /// 市场信息帧类
    /// </summary>
    public struct NewMarketFrame
    {
        /// <summary>
        /// 帧日期
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// 存放一帧市场信息
        /// key: 股票代码
        /// value: 股票帧
        /// </summary>
        public Dictionary<string, NewShareFrame> MarketFrame { get; set; }
    }
}
