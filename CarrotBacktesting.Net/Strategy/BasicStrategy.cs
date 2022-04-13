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
#if DEBUG
            Console.WriteLine("BasicStrategy.Start()");
            Console.WriteLine("SetCash:100000.");
#endif
            context.PortfolioManager.SetCash(100000);
        }

        public void End(StrategyContext strategyContext)
        {
#if DEBUG
            Console.WriteLine("BasicStrategy.End()");
            Console.WriteLine("PositionManager:");
            Console.WriteLine(strategyContext.PortfolioManager.PositionManager.ToString());
            Console.WriteLine("TransactionLogger:");
            Console.WriteLine(strategyContext.PortfolioManager.TransactionLogger.ToString());
#endif
        }

        public void OnTick(StrategyContext strategyContext)
        {
#if DEBUG
            Console.WriteLine("BasicStrategy.PreNext()");
#endif
        }

        public void OnNext(StrategyContext strategyContext)
        {
#if DEBUG
            Console.WriteLine("BasicStrategy.Next()");
            Console.WriteLine($"{strategyContext.MarketFrame.NowTime.ToShortDateString()}| Market {(strategyContext.MarketFrame.IsActive ? "Open" : "Close")} | Price: {strategyContext.MarketFrame.NowPrice}.");
#endif
            if (strategyContext.MarketFrame.IsActive) 
            {
                if (strategyContext.MarketFrame.NowPrice <= 220)
                    strategyContext.PortfolioManager.AddOrder("A", "001", 230, 100.0, OrderDirection.Long);
                if (strategyContext.MarketFrame.NowPrice >= 330)
                    strategyContext.PortfolioManager.AddOrder("A", "001", 320, 100.0, OrderDirection.Short);
            }
        }
    }
}
