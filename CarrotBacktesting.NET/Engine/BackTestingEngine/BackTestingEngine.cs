using CarrotBacktesting.NET.Strategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Engine.BackTestingEngine
{
    public class BackTestingEngine : IEngine
    {
        public StrategyContext StrategyContext;
        public IStrategy Strategy;

        public BackTestingEngine(IStrategy strategy)
        {
            StrategyContext = new();
            SetStrategy(strategy);
        }

        public void SetStrategy(IStrategy strategy)
        {
            Strategy = strategy;
        }

        public void Run()
        {
            Strategy.Start(StrategyContext);
            for (int i = 0; i < 5; i++)
            {
                Console.WriteLine($"Run {i + 1}:");
                Strategy.PreNext(StrategyContext);
                Strategy.Next(StrategyContext);
            }
            Strategy.End(StrategyContext);
        }
    }
}
