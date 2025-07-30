using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        /// 所有股票代码的有序列表
        /// 数组的索引是这只股票的全局唯一ID
        /// </summary>
        public IReadOnlyList<string> Symbols { get; }

        /// <summary>
        /// 从股票代码到其在股票索引的映射
        /// </summary>
        public ImmutableDictionary<string, int> SymbolsToIndexMap { get; }

        /// <summary>
        /// 本次加载的所有交易日(已排序)
        /// </summary>
        public IReadOnlyList<DateTime> TradeDates => _marketFrames.Keys.ToList();

        /// <summary>
        /// 构造函数
        /// </summary>
        public MarketStorage(SortedDictionary<DateTime, MarketFrame> marketFrames, List<string> symbols)
        {
            _marketFrames = marketFrames ?? new SortedDictionary<DateTime, MarketFrame>();
            Symbols = symbols;

            // 一次性构建映射字典，提高查询效率
            var map = new Dictionary<string, int>(Symbols.Count);
            for (int i = 0; i < Symbols.Count; i++)
            {
                map[Symbols[i]] = i;
            }
            SymbolsToIndexMap = map.ToImmutableDictionary();
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
    }
}