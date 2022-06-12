using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataFeed
{
    /// <summary>
    /// Temp for ShareFrame
    /// </summary>
    public struct NewShareFrame
    {
        /// <summary>
        /// 日期/时间
        /// </summary>
        public DateTime DateTime { get; set; }
        /// <summary>
        /// 开盘价
        /// </summary>
        public double OpenPrice { get; set; }

        /// <summary>
        /// 最高价
        /// </summary>
        public double HighPrice { get; set; }

        /// <summary>
        /// 最低价
        /// </summary>
        public double LowPrice { get; set; }

        /// <summary>
        /// 收盘价
        /// </summary>
        public double ClosePrice { get; set; }

        /// <summary>
        /// 成交量
        /// </summary>
        public double Volume { get; set; }

        /// <summary>
        /// 是否正常交易
        /// </summary>
        public bool IsTrading { get; set; }

        /// <summary>
        /// 其他数据
        /// </summary>
        public Dictionary<string, dynamic> Data { get; set; }
    }
}
