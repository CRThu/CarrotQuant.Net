using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 市场信息类
    /// </summary>
    public class MarketData
    {
        /// <summary>
        /// 市场帧集合
        /// </summary>
        public List<MarketFrame> MarketFrames { get; set; } = new();
    }
}
