using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 数据存储的通用接口
    /// </summary>
    public interface IDataStorage
    {
        /// <summary>
        /// 所有股票代码列表
        /// </summary>
        IReadOnlyList<string> Symbols { get; }

        /// <summary>
        /// 所有交易日列表
        /// </summary>
        IReadOnlyList<DateTime> TradeDates { get; }
    }
}
