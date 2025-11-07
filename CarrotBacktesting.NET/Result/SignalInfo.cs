using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Result
{
    /// <summary>
    /// 表示一个触发的信号
    /// </summary>
    /// <param name="StockCode">股票代码</param>
    /// <param name="Date">触发日期</param>
    [Obsolete]
    [MessagePackObject]
    public readonly record struct SignalInfo(
        [property: Key(0)] string StockCode,
        [property: Key(1)] DateTime Date);
}
