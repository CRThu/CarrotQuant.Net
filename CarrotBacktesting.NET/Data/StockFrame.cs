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
    public readonly struct StockFrame
    {
        /// <summary>
        /// 开盘价
        /// </summary>
        public readonly double Open;

        /// <summary>
        /// 最高价
        /// </summary>
        public readonly double High;

        /// <summary>
        /// 最低价
        /// </summary>
        public readonly double Low;

        /// <summary>
        /// 收盘价
        /// </summary>
        public readonly double Close;

        /// <summary>
        /// 成交量
        /// </summary>
        public readonly double Volume;

        /// <summary>
        /// 交易状态
        /// </summary>
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
