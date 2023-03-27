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
    }
}
