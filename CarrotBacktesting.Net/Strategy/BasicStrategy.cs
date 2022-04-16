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
            Console.WriteLine($"{strategyContext.MarketFrame.NowTime:d}" +
                $"| Market {(strategyContext.MarketFrame["sz.000422"].IsActive ? "Open" : "Close")} " +
                $"| Price: {strategyContext.MarketFrame["sz.000422"].NowPrice:F3} " +
                $"| Status: {strategyContext.MarketFrame["sz.000422"].StringData["交易状态"]} " +
                $"| IsST: {strategyContext.MarketFrame["sz.000422"].StringData["是否ST"]} " +
                $"| PE: {strategyContext.MarketFrame["sz.000422"].FloatData["滚动市盈率"]:F3}.");
            Console.WriteLine($"{strategyContext.MarketFrame.NowTime:d}" +
                $"| Market {(strategyContext.MarketFrame["sz.000423"].IsActive ? "Open" : "Close")} " +
                $"| Price: {strategyContext.MarketFrame["sz.000423"].NowPrice:F3} " +
                $"| Status: {strategyContext.MarketFrame["sz.000423"].StringData["交易状态"]} " +
                $"| IsST: {strategyContext.MarketFrame["sz.000423"].StringData["是否ST"]} " +
                $"| PE: {strategyContext.MarketFrame["sz.000423"].FloatData["滚动市盈率"]:F3}.");
#endif

            if (strategyContext.MarketFrame["sz.000422"].IsActive)
            {
                if (strategyContext.MarketFrame["sz.000422"].NowPrice <= 220)
                    strategyContext.PortfolioManager.AddOrder("sz.000422", 230, 100.0, OrderDirection.Long);
                if (strategyContext.MarketFrame["sz.000422"].NowPrice >= 350)
                    strategyContext.PortfolioManager.AddOrder("sz.000422", 320, 100.0, OrderDirection.Short);
            }
        }
    }
}
