using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Portfolio;
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
            Console.WriteLine(strategyContext.PortfolioManager.PositionManager.ToString());
        }

        public void OnTick(StrategyContext strategyContext)
        {
            Console.WriteLine("BasicStrategy.PreNext()");
        }

        public void OnNext(StrategyContext strategyContext)
        {
            Console.WriteLine("BasicStrategy.Next()");
            Console.WriteLine($"{strategyContext.NowTime}| Price: {strategyContext.NowPrice}.");

            if (strategyContext.NowPrice <= 3)
                strategyContext.PortfolioManager.AddOrder(new GeneralOrder("A", "001", 2.5, 100.0, TradeDirectionEnum.Long));
            if (strategyContext.NowPrice >= 6)
                strategyContext.PortfolioManager.AddOrder(new GeneralOrder("A", "001", 4.5, 100.0, TradeDirectionEnum.Short));
        }
    }
}
