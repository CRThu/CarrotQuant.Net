using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
    public static class Enums
    {
        public enum DataFeedSource
        {
            Sqlite,
            Csv
        }

        /// <summary>
        /// 交易所模拟成交价格参考选项
        /// </summary>
        public enum ExchangePriceRef
        {
            /// <summary>
            /// 开盘价
            /// </summary>
            Open,
            /// <summary>
            /// 收盘价
            /// </summary>
            Close,
            /// <summary>
            /// 平均成交价(本K线成交额/成交量)
            /// </summary>
            TradeAverage
        }

        /// <summary>
        /// 日期跨度枚举
        /// </summary>
        public enum DateSpan
        {
            /// <summary>
            /// Tick级跨度
            /// </summary>
            Tick,
            /// <summary>
            /// 每日
            /// </summary>
            Day,
            /// <summary>
            /// 每月
            /// </summary>
            Month,
            /// <summary>
            /// 每年
            /// </summary>
            Year,
        }

        /// <summary>
        /// 委托单状态
        /// </summary>
        public enum GeneralOrderStatus
        {
            /// <summary>
            /// 待成交/部分成交
            /// </summary>
            Pending,
            /// <summary>
            /// 已成交
            /// </summary>
            Executed,
            /// <summary>
            /// 已取消
            /// </summary>
            Cancelled
        }

        /// <summary>
        /// 委托单方向(买入/卖出)
        /// </summary>
        public enum OrderDirection
        {
            /// <summary>
            /// 买入
            /// </summary>
            Buy,
            /// <summary>
            /// 卖出
            /// </summary>
            Sell
        }

        /// <summary>
        /// 委托单类型(限价/市价)
        /// </summary>
        public enum OrderType
        {
            /// <summary>
            /// 限价委托
            /// </summary>
            LimitOrder,
            /// <summary>
            /// 市价委托
            /// </summary>
            MarketOrder
        }

        /// <summary>
        /// 委托单操作
        /// </summary>
        public enum OrderUpdatedEventOperation
        {
            /// <summary>
            /// 创建委托单
            /// </summary>
            CreateOrder,
            /// <summary>
            /// 更新委托单
            /// </summary>
            UpdateOrder,
            /// <summary>
            /// 取消委托单
            /// </summary>
            CancelOrder
        }
    }
}
