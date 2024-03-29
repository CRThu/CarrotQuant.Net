﻿using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Portfolio.Analyzer;
using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CarrotBacktesting.Net.Common.Enums;

namespace CarrotBacktesting.Net.Strategy
{
    /// <summary>
    /// 基础测试策略
    /// </summary>
    public class BasicStrategy : IStrategy
    {
        public void OnStart(StrategyContext context)
        {
#if DEBUG
            Console.WriteLine("BasicStrategy.Start()");
            Console.WriteLine("SetCash:100000.");
#endif
            //context.PortfolioManager.SetCash(100000);
            throw new NotImplementedException();
        }

        public void OnEnd(StrategyContext strategyContext)
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
            //Console.WriteLine(ClassFormatter.Formatter(strategyContext.Analyzer.Analyze()));

            Console.WriteLine("MonthlyReturn:");
            //Console.WriteLine(ClassFormatter.Formatter(strategyContext.Analyzer.GetReturn(DateSpan.Month)));
#endif
        }

        public void OnTick(MarketFrame marketFrame, MarketEventArgs marketEventArgs)
        {
#if DEBUG
            Console.WriteLine("BasicStrategy.PreNext()");
#endif
        }

        public void OnNext(StrategyContext strategyContext)
        {
#if DEBUG
            Console.WriteLine("BasicStrategy.Next()");
            Console.WriteLine($"{strategyContext.Simulator.CurrentTime:d}" +
                $"| Market {(strategyContext.Simulator.CurrentMarket["sz.000422"].Status ? "Open" : "Close")} " +
                $"| Price: {strategyContext.Simulator.CurrentMarket["sz.000422"].Close:F3} " +
                $"| Status: {strategyContext.Simulator.CurrentMarket["sz.000422"]["交易状态"]} " +
                $"| IsST: {strategyContext.Simulator.CurrentMarket["sz.000422"]["是否ST"]} " +
                $"| PE: {strategyContext.Simulator.CurrentMarket["sz.000422"]["滚动市盈率"]:F3}.");
            Console.WriteLine($"{strategyContext.Simulator.CurrentTime:d}" +
                $"| Market {(strategyContext.Simulator.CurrentMarket["sz.000423"].Status ? "Open" : "Close")} " +
                $"| Price: {strategyContext.Simulator.CurrentMarket["sz.000423"].Close:F3} " +
                $"| Status: {strategyContext.Simulator.CurrentMarket["sz.000423"]["交易状态"]} " +
                $"| IsST: {strategyContext.Simulator.CurrentMarket["sz.000423"]["是否ST"]} " +
                $"| PE: {strategyContext.Simulator.CurrentMarket["sz.000423"]["滚动市盈率"]:F3}.");
#endif

            if (strategyContext.Simulator.CurrentMarket["sz.000422"].Status)
            {
                if (strategyContext.Simulator.CurrentMarket["sz.000422"].Close <= 220)
                    strategyContext.PortfolioManager.CreateOrder("sz.000422", OrderDirection.Buy, 100.0, OrderType.LimitOrder, 230);
                if (strategyContext.Simulator.CurrentMarket["sz.000422"].Close >= 350)
                    strategyContext.PortfolioManager.CreateOrder("sz.000422", OrderDirection.Sell, 300.0, OrderType.LimitOrder, 320);
            }

            if (strategyContext.Simulator.CurrentMarket["sz.000423"].Status)
            {
                if (strategyContext.Simulator.CurrentMarket["sz.000423"].Close <= 270)
                    strategyContext.PortfolioManager.CreateOrder("sz.000423", OrderDirection.Buy, 100.0, OrderType.LimitOrder, 280);
            }
        }

        public void OnOrderUpdate(OrderManager sender, OrderEventArgs operation)
        {
            throw new NotImplementedException();
        }

        public void OnTradeUpdate(BackTestingExchange exchange, TradeEventArgs tradeEventArgs)
        {
            throw new NotImplementedException();
        }
    }
}
