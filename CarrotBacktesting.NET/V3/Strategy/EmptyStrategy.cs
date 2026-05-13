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
    /// 空策略
    /// </summary>
    public class EmptyStrategy : ITradeStrategy
    {
        public string Name => nameof(EmptyStrategy);

        public SignalResult? CheckEntry(SignalStrategyContext context)
        {
            return null;
        }

        public SignalResult? CheckExit(SignalStrategyContext context, Trade trade)
        {
            return null;
        }
    }
}
