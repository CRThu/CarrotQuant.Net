using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Storage;
using CarrotBacktesting.Net.Strategy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测引擎
    /// </summary>
    public class BackTestingEngine : IEngine
    {
        /// <summary>
        /// 策略上下文
        /// </summary>
        public StrategyContext StrategyContext { get; set; }

        /// <summary>
        /// 数据源
        /// </summary>
        public DataFeed DataFeed { get; set; }

        /// <summary>
        /// 回测模拟交易所
        /// </summary>
        public BackTestingExchange Exchange { get; set; }

        /// <summary>
        /// 回测Tick模拟器
        /// </summary>
        public TickSimulator Simulator { get; set; }

        /// <summary>
        /// 投资组合管理器
        /// </summary>
        public PortfolioManager Portfolio { get; set; }

        /// <summary>
        /// 回测配置
        /// </summary>
        public SimulationOptions Options { get; set; }

        /// <summary>
        /// 策略接口
        /// </summary>
        public IStrategy Strategy { get; set; }


        /// <summary>
        /// 构造函数
        /// </summary>
        public BackTestingEngine()
        {

        }

        public void RunTickForBenchmark()
        {
            Simulator.OnRaiseMarketUpdateEvent();
        }

        public void Run()
        {
            if (Simulator.IsRunning)
                throw new InvalidOperationException();

            Strategy.OnStart(StrategyContext);

            while (Simulator.UpdateTick())
            {
                Strategy.OnNext(StrategyContext);
            }

            Strategy.OnEnd(StrategyContext);
        }

        /// <summary>
        /// 通过配置文件路径创建回测引擎
        /// </summary>
        /// <param name="strategy">策略</param>
        /// <param name="optionsJsonPath">配置文件路径</param>
        /// <returns>回测引擎类</returns>
        public static BackTestingEngine Create(IStrategy strategy, string optionsJsonPath)
        {
            string baseDir = Path.GetDirectoryName(optionsJsonPath)!;
            SimulationOptions options = SimulationOptions.CreateFromJson(optionsJsonPath);
            return Create(strategy, options, baseDir);
        }

        /// <summary>
        /// 通过配置创建回测引擎
        /// </summary>
        /// <param name="strategy">策略</param>
        /// <param name="options">配置类</param>
        /// <param name="baseDir">默认根目录</param>
        /// <returns>回测引擎类</returns>
        public static BackTestingEngine Create(IStrategy strategy, SimulationOptions options, string baseDir = ".")
        {
            // 类初始化
            options.Parse(baseDir);
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
            simulator.OnMarketUpdate += strategy.OnTick;
            exchange.OnTradeUpdate += portfolio.OnTradeUpdate;
            exchange.OnTradeUpdate += strategy.OnTradeUpdate;
            portfolio.OrderManager.OnOrderUpdate += exchange.OnOrderUpdate;
            portfolio.OrderManager.OnOrderUpdate += strategy.OnOrderUpdate;

            return engine;
        }
    }
}
