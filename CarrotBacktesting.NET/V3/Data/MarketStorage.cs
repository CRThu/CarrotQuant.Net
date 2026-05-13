using MessagePack;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 核心市场数据存储容器
    /// </summary>
    [MessagePackObject]
    public class MarketStorage : IDataStorage
    {
        /// <summary>
        /// 市场数据字典, 按时间排序
        /// Key: 交易日
        /// Value: 当天的市场数据
        /// </summary>
        [Key(0)]
        public Dictionary<DateTime, MarketFrame> MarketFrames;

        /// <summary>
        /// 从股票代码到其在股票索引的映射
        /// </summary>
        [Key(1)]
        public Dictionary<string, int> SymbolsMap;

        /// <summary>
        /// 所有股票代码的有序列表
        /// 数组的索引是这只股票的全局唯一ID
        /// </summary>
        [IgnoreMember]
        [JsonIgnore]
        public IReadOnlyList<string> Symbols => SymbolsMap.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();

        /// <summary>
        /// 本次加载的所有交易日(已排序)
        /// </summary>
        [IgnoreMember]
        [JsonIgnore]
        public IReadOnlyList<DateTime> TradeDates => MarketFrames.Keys.ToList();

        /// <summary>
        /// 构造函数
        /// </summary>
        [SerializationConstructor]
        [JsonConstructor]
        public MarketStorage(Dictionary<DateTime, MarketFrame> marketFrames, Dictionary<string, int> symbolsMap)
        {
            MarketFrames = marketFrames;
            SymbolsMap = symbolsMap;
        }

        /// <summary>
        /// 获取指定日期的市场横截面数据
        /// </summary>
        public bool TryGetFrame(DateTime date, out MarketFrame? frame)
        {
            return MarketFrames.TryGetValue(date.Date, out frame);
        }

        /// <summary>
        /// 获取用于回测的帧枚举器
        /// </summary>
        public IEnumerable<MarketFrame> GetFramesEnumerator()
        {
            return MarketFrames.Values;
        }
    }
}