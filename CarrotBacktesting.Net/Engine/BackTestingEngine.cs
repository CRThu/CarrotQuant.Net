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
        // TODO
        //public StrategyContext StrategyContext { get; set; }

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

        /*
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="strategy">策略接口</param>
        /// <param name="options">回测模拟器选项</param>
        public BackTestingEngine(IStrategy strategy, SimulationOptions options)
        {
            // Console.WriteLine($"初始化数据库.");
            //Simulator = new(options);
            // Console.WriteLine($"模拟时间共{Simulation.SimulationDuration.TotalDays}天.");

            throw new NotImplementedException();
            //StrategyContext = new(Simulation.SimulationMarketFrame);

            Strategy = strategy;
            throw new NotImplementedException();
            //Exchange = new(StrategyContext.PortfolioManager, Simulation.SimulationMarketFrame);
        }
        */
        public void Run()
        {
        /*
            // Stopwatch stopwatch = new();
            // stopwatch.Start();
            // int loop = 0;

            Strategy.Start(StrategyContext);
            while (Simulator.IsSimulating)
            {
                // 市场价格更新
                Simulator.UpdateFrame();

                // 交易所订单更新
                throw new NotImplementedException();
                //Exchange.OnPriceUpdate();

                // 投资组合PNL信息更新
                // TODO
                //StrategyContext.PortfolioManager.OnPriceUpdate();

                // 时间片更新(用于更新Tick生成指标等数据)
                Strategy.OnTick(StrategyContext);

                // 策略更新(更新策略, 挂单)
                Strategy.OnNext(StrategyContext);

                // loop++;
            }
            Strategy.End(StrategyContext);

            // stopwatch.Stop();
            // Console.WriteLine($"回测已完成, 共测试{loop}帧, 耗时{stopwatch.ElapsedMilliseconds / 1000.0}秒, 回测速度{(double)loop / stopwatch.ElapsedMilliseconds * 1000:F3}帧/秒.");
        */
        }
    }
}
