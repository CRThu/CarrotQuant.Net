using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.DataModel
{
    /// <summary>
    /// 市场数据构造器
    /// </summary>
    public class MarketDataBuilder
    {
        /// <summary>
        /// 市场数据实例
        /// </summary>
        private MarketData marketData { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketDataBuilder()
        {
            marketData = new();
        }

        /// <summary>
        /// 添加一帧股票数据
        /// </summary>
        /// <param name="shareFrame">股票帧</param>
        public void Add(ShareFrame shareFrame)
        {
            // 若Data不包含此日期的市场信息帧则新建一帧
            if (!marketData.Contains(shareFrame.DateTime))
                marketData.Add(new MarketFrame(shareFrame.DateTime));

            marketData[shareFrame.DateTime].Add(shareFrame);
        }

        /// <summary>
        /// 添加股票数据字典集合
        /// </summary>
        /// <param name="shareFramesData"></param>
        public void AddRange(string stockCode, IEnumerable<IDictionary<string, object>> shareFramesData)
        {
            foreach (var shareFrameData in shareFramesData)
            {
                ShareFrame frame = new(shareFrameData, stockCode);
                Add(frame);
            }
        }

        /// <summary>
        /// 返回市场数据实例
        /// </summary>
        /// <returns>市场数据实例</returns>
        public MarketData ToMarketData()
        {
            return marketData;
        }
    }
}
