using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Storage;
using CarrotBacktesting.Net.Strategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测引擎工厂类
    /// </summary>
    public static class BackTestingEngineFactory
    {
        /// <summary>
        /// 通过配置创建回测引擎
        /// </summary>
        /// <returns></returns>
        public static BackTestingEngine Create(IStrategy strategy, SimulationOptions options)
        {
            // 类初始化
            DataFeed dataFeed = new(options);
            BackTestingExchange exchange = new(options);
            TickSimulator simulator = new(dataFeed, options);
            PortfolioManager portfolio = new(options);
            StrategyContext context = new(portfolio, simulator);

            BackTestingEngine engine = new() {
                DataFeed = dataFeed,
                Exchange = exchange,
                Simulator = simulator,
                Portfolio = portfolio,
                Options = options,
                Strategy = strategy,
                StrategyContext = context
            };

            // 事件订阅
            simulator.OnMarketUpdate += exchange.OnMarketUpdate;
            simulator.OnMarketUpdate += portfolio.OnMarketUpdate;
            exchange.OnTradeUpdate += portfolio.OnTradeUpdate;
            portfolio.OrderManager.OnOrderUpdate += exchange.OnOrderUpdate;

            return engine;
        }
    }
}
