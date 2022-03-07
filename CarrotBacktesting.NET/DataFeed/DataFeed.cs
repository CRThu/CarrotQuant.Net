using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.DataFeed
{
    public class DataFeed
    {
        /// <summary>
        /// MarketCache存放所需的股票行情数据
        /// key为stock名, value指向个股数据
        /// value的字典的key为数据列名(例如OHLC), Value为对应数据
        /// </summary>
        private Dictionary<string, ShareData> MarketCache { get; set; } = new();

        /// <summary>
        /// 设置一支股票的行情数据
        /// </summary>
        public void SetShareData(string shareCode, ShareData shareData)
        {
            if (!MarketCache.ContainsKey(shareCode))
                MarketCache[shareCode] = shareData;
        }
    }
}
