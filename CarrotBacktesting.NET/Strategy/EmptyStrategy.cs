using CarrotBacktesting.NET.Engine;
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
    public class EmptyStrategy : ISignalStrategy
    {
        public string Name => nameof(EmptyStrategy);

        public bool CheckSignal(SignalStrategyContext context)
        {
            return false;
        }
    }
}
