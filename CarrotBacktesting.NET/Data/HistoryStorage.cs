using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 核心历史数据存储容器，以纵向（按股票时间序列）方式组织
    /// </summary>
    [MessagePackObject]
    public class HistoryStorage : IDataStorage
    {
        /// <summary>
        /// 存储所有股票的时间序列数据
        /// Key: 股票代码
        /// Value: 该股票的历史数据
        /// </summary>
        [Key(0)]
        public IReadOnlyDictionary<string, StockHistory> StockHistories { get; }

        /// <summary>
        /// 本次加载的所有交易日 (已排序且唯一)
        /// </summary>
        [Key(1)]
        public IReadOnlyList<DateTime> TradeDates { get; }

        [JsonIgnore]
        [IgnoreMember]
        public IReadOnlyList<string> Symbols => StockHistories.Keys.ToList();

        [JsonConstructor]
        public HistoryStorage(IReadOnlyDictionary<string, StockHistory> stockHistories, IReadOnlyList<DateTime> tradeDates)
        {
            StockHistories = stockHistories;
            TradeDates = tradeDates;
        }
    }
}
