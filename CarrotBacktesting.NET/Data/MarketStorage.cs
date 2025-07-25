using System;
using System.Collections.Generic;
using System.Linq;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 核心市场数据存储容器
    /// </summary>
    public class MarketStorage
    {
        /// <summary>
        /// 市场数据字典, 按时间排序
        /// Key: 交易日
        /// Value: 当天的市场数据
        /// </summary>
        private readonly SortedDictionary<DateTime, MarketFrame> _marketFrames;

        /// <summary>
        /// 本次加载的所有股票代码(去重)
        /// </summary>
        public IReadOnlyList<string> StockCodes { get; }

        /// <summary>
        /// 本次加载的所有交易日(已排序)
        /// </summary>
        public IReadOnlyList<DateTime> TradeDates => _marketFrames.Keys.ToList();

        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketStorage(SortedDictionary<DateTime, MarketFrame> marketFrames)
        {
            _marketFrames = marketFrames ?? new SortedDictionary<DateTime, MarketFrame>();
            StockCodes = GetAllStockCodes();
        }

        /// <summary>
        /// 获取指定日期的市场横截面数据
        /// </summary>
        public bool TryGetFrame(DateTime date, out MarketFrame? frame)
        {
            return _marketFrames.TryGetValue(date.Date, out frame);
        }

        /// <summary>
        /// 获取用于回测的帧枚举器
        /// </summary>
        public IEnumerable<MarketFrame> GetFramesEnumerator()
        {
            return _marketFrames.Values;
        }

        private IReadOnlyList<string> GetAllStockCodes()
        {
            if (_marketFrames.Count == 0)
                return Array.Empty<string>();

            var stockSet = new HashSet<string>();
            foreach (var frame in _marketFrames.Values)
            {
                // 从基础数据中收集
                foreach (var stockCode in frame.PrimaryData.Keys)
                {
                    stockSet.Add(stockCode);
                }
            }
            return stockSet.ToList();
        }
    }
}