using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Strategy
{
    /// <summary>
    /// 基础测试策略
    /// </summary>
    public class BasicStrategy : IStrategy
    {
        public void Start(StrategyContext context)
        {
            Console.WriteLine("BasicStrategy.Start()");
            Console.WriteLine("SetCash:100000.");
            context.PortfolioManager.SetCash(100000);
        }

        public void End(StrategyContext strategyContext)
        {
            Console.WriteLine("BasicStrategy.End()");
            Console.WriteLine("PositionManager:");
            Console.WriteLine(strategyContext.PortfolioManager.PositionManager.ToString());
            Console.WriteLine("TransactionLogger:");
            Console.WriteLine(strategyContext.PortfolioManager.TransactionLogger.ToString());
        }

        public void OnTick(StrategyContext strategyContext)
        {
            Console.WriteLine("BasicStrategy.PreNext()");
        }

        public void OnNext(StrategyContext strategyContext)
        {
            Console.WriteLine("BasicStrategy.Next()");
            Console.WriteLine($"{strategyContext.MarketFrame.NowTime}| Price: {strategyContext.MarketFrame.NowPrice}.");

            if (strategyContext.MarketFrame.NowPrice <= 3)
                strategyContext.PortfolioManager.AddOrder("A", "001", 2.5, 100.0, OrderDirection.Long);
            if (strategyContext.MarketFrame.NowPrice >= 6)
                strategyContext.PortfolioManager.AddOrder("A", "001", 4.5, 100.0, OrderDirection.Short);
        }
    }
}
