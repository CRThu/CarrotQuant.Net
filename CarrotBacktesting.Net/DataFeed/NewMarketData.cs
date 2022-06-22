using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataFeed
{
    /// <summary>
    /// 市场信息类
    /// </summary>
    public class NewMarketData
    {
        /// <summary>
        /// key: 市场信息帧日期
        /// value: 市场信息帧数据
        /// </summary>
        public Dictionary<DateTime, NewMarketFrame> MarketFrames { get; set; } = new();

        // TODO
    }
}
