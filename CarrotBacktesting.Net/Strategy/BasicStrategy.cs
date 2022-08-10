using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Portfolio.Analyzer;
using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Shared;
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
            //context.PortfolioManager.SetCash(100000);
            throw new NotImplementedException();
        }

        public void End(StrategyContext strategyContext)
        {
#if DEBUG
            Console.WriteLine("BasicStrategy.End()");
            Console.WriteLine();

            Console.WriteLine("PositionManager:");
            //Console.WriteLine(strategyContext.PositionManager.ToString());
            throw new NotImplementedException();

            Console.WriteLine("TransactionLogger:");
            Console.WriteLine(strategyContext.TransactionLogger.ToString());
            Console.WriteLine("PnlLogger:");
            Console.WriteLine(strategyContext.PnlLogger.ToString());

            Console.WriteLine("Analyzer:");
            Console.WriteLine(ClassFormatter.Formatter(strategyContext.Analyzer.Analyze()));

            Console.WriteLine("MonthlyReturn:");
            Console.WriteLine(ClassFormatter.Formatter(strategyContext.Analyzer.GetReturn(DateSpan.Month)));
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
            Console.WriteLine($"{strategyContext.MarketFrame.DateTime:d}" +
                $"| Market {(strategyContext.MarketFrame["sz.000422"].IsTrading ? "Open" : "Close")} " +
                $"| Price: {strategyContext.MarketFrame["sz.000422"].ClosePrice:F3} " +
                $"| Status: {strategyContext.MarketFrame["sz.000422"]["交易状态"]} " +
                $"| IsST: {strategyContext.MarketFrame["sz.000422"]["是否ST"]} " +
                $"| PE: {strategyContext.MarketFrame["sz.000422"]["滚动市盈率"]:F3}.");
            Console.WriteLine($"{strategyContext.MarketFrame.DateTime:d}" +
                $"| Market {(strategyContext.MarketFrame["sz.000423"].IsTrading ? "Open" : "Close")} " +
                $"| Price: {strategyContext.MarketFrame["sz.000423"].ClosePrice:F3} " +
                $"| Status: {strategyContext.MarketFrame["sz.000423"]["交易状态"]} " +
                $"| IsST: {strategyContext.MarketFrame["sz.000423"]["是否ST"]} " +
                $"| PE: {strategyContext.MarketFrame["sz.000423"]["滚动市盈率"]:F3}.");
#endif

            if (strategyContext.MarketFrame["sz.000422"].IsTrading)
            {
                if (strategyContext.MarketFrame["sz.000422"].ClosePrice <= 220)
                    strategyContext.PortfolioManager.AddOrder("sz.000422", 230, 100.0, OrderDirection.Buy);
                if (strategyContext.MarketFrame["sz.000422"].ClosePrice >= 350)
                    strategyContext.PortfolioManager.AddOrder("sz.000422", 320, 300.0, OrderDirection.Sell);
            }

            if (strategyContext.MarketFrame["sz.000423"].IsTrading)
            {
                if (strategyContext.MarketFrame["sz.000423"].ClosePrice <= 270)
                    strategyContext.PortfolioManager.AddOrder("sz.000423", 280, 100.0, OrderDirection.Buy);
            }
        }
    }
}
