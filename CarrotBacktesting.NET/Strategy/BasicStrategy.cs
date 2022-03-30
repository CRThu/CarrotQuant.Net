using CarrotBacktesting.NET.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Strategy
{
    /// <summary>
    /// 基础测试策略
    /// </summary>
    public class BasicStrategy : IStrategy
    {
        public void Start(StrategyContext strategyContext)
        {
            Console.WriteLine("BasicStrategy.Start()");
        }

        public void End(StrategyContext strategyContext)
        {
            Console.WriteLine("BasicStrategy.End()");
        }

        public void PreNext(StrategyContext strategyContext)
        {
            Console.WriteLine("BasicStrategy.PreNext()");
        }

        public void Next(StrategyContext strategyContext)
        {
            Console.WriteLine("BasicStrategy.Next()");
        }
    }
}
