using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Common
{
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
}
