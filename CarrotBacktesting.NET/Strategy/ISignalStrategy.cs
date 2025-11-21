using CarrotBacktesting.NET.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Strategy
{
    /// <summary>
    /// 用于【信号生成模式】的策略接口。
    /// </summary>
    public interface ISignalStrategy : IStrategy
    {
        /// <summary>
        /// 在每个K线上检查是否应生成一个【买入信号】。
        /// </summary>
        /// <returns>如果触发，返回一个描述原因的字符串；否则返回 null。</returns>
        SignalResult? CheckSignal(SignalStrategyContext context);
    }
}
