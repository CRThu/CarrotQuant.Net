using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 市场信息帧, 包含时间片内的价格信息
    /// 目前只能存放一个时间帧内的单个股票信息
    /// TODO待重构
    /// </summary>
    public class MarketFrame
    {
        /// <summary>
        /// 当前日期
        /// </summary>
        public DateTime NowTime { get; set; }

        public Dictionary<string, ShareFrame> MarketFrameCache { get; set; } = new();

        public ShareFrame this[string shareName]
        {
            get
            {
                return MarketFrameCache[shareName];
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="shareNames"></param>
        public MarketFrame(string[] shareNames)
        {
            foreach (var shareName in shareNames)
                MarketFrameCache.Add(shareName, new ShareFrame());
        }

        public void UpdateTime(DateTime time)
        {
            NowTime = time;
        }
    }
}
