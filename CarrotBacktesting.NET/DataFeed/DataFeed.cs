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
        private Dictionary<string, Dictionary<string, double[]>> MarketCache { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public DataFeed()
        {
            MarketCache = new();
        }

        /// <summary>
        /// 设置一支股票的行情数据
        /// </summary>
        public void SetShareData(string shareCode, List<double[]> shareData)
        {
            if (!MarketCache.ContainsKey(shareCode))
                MarketCache[shareCode] = shareData;
        }
    }
}
