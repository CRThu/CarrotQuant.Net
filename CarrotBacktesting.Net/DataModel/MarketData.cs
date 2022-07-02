using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 市场信息帧集合类
    /// </summary>
    public class MarketData
    {
        /// <summary>
        /// 包含多个时间与多支股票信息的市场帧集合
        /// key: 时间
        /// value: 市场帧
        /// </summary>
        public Dictionary<DateTime, MarketFrame> MarketFrames { get; set; } = new();
    }
}
