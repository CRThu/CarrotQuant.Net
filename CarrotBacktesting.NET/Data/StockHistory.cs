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
    /// 存储单支股票的完整时间序列数据
    /// </summary>
    [MessagePackObject]
    public class StockHistory
    {
        [Key(0)]
        public string StockCode { get; }

        [Key(1)]
        public IReadOnlyList<DateTime> Dates { get; }

        [Key(2)]
        public IReadOnlyList<StockFrame> Data { get; }

        [JsonConstructor]
        public StockHistory(string stockCode, IReadOnlyList<DateTime> dates, IReadOnlyList<StockFrame> data)
        {
            StockCode = stockCode;
            Dates = dates;
            Data = data;
        }
    }
}
