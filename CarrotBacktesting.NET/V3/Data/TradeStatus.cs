using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 交易状态枚举
    /// </summary>
    public enum TradeStatus
    {
        /// <summary>
        /// 未知或未设置
        /// </summary>
        Unknown,
        /// <summary>
        /// 正常交易
        /// </summary>
        Active,
        /// <summary>
        /// 停牌
        /// </summary>
        Halted,
        /// <summary>
        /// 已退市
        /// </summary>
        Delisted
    }
}
