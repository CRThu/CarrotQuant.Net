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
    /// 单个资产在单个时间点的核心行情数据(不含时间)
    /// </summary>
    [MessagePackObject]
    public readonly struct StockFrame
    {
        /// <summary>
        /// 开盘价
        /// </summary>
        [Key(0)]
        public readonly double Open;

        /// <summary>
        /// 最高价
        /// </summary>
        [Key(1)]
        public readonly double High;

        /// <summary>
        /// 最低价
        /// </summary>
        [Key(2)]
        public readonly double Low;

        /// <summary>
        /// 收盘价
        /// </summary>
        [Key(3)]
        public readonly double Close;

        /// <summary>
        /// 成交量
        /// </summary>
        [Key(4)]
        public readonly double Volume;

        /// <summary>
        /// 交易状态
        /// </summary>
        [Key(5)]
        public readonly TradeStatus Status;

        [JsonConstructor]
        public StockFrame(double open, double high, double low, double close, double volume, TradeStatus status)
        {
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
            Status = status;
        }
    }
}
