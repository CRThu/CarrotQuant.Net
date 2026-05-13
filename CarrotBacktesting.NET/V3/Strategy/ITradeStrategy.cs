using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Strategy
{
    /// <summary>
    /// 用于【交易模拟模式】的策略接口，包含开仓和平仓逻辑。
    /// </summary>
    public interface ITradeStrategy : IStrategy
    {
        /// <summary>
        /// 检查是否应该【开仓】(Entry)。
        /// 只在当前【空仓】时被引擎调用。
        /// </summary>
        /// <returns>如果应该开仓，返回一个描述原因的字符串；否则返回 null。</returns>
        SignalResult? CheckEntry(SignalStrategyContext context);

        /// <summary>
        /// 检查是否应该【平仓】(Exit)。
        /// 只在当前【持仓】时被引擎调用。
        /// </summary>
        /// <returns>如果应该平仓，返回一个描述原因的字符串；否则返回 null。</returns>
        SignalResult? CheckExit(SignalStrategyContext context, Trade trade);
    }
}
