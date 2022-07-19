using CarrotBacktesting.Net.Portfolio;
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
        public StrategyContext StrategyContext { get; set; }

        /// <summary>
        /// 回测交易所
        /// </summary>
        public BackTestingExchange Exchange { get; set; }

        /// <summary>
        /// 回测模拟器
        /// </summary>
        public BackTestingSimulation Simulation { get; set; }

        /// <summary>
        /// 策略
        /// </summary>
        public IStrategy Strategy { get; set; }

        public BackTestingEngine(IStrategy strategy, SimulationOptions options)
        {
            // Console.WriteLine($"初始化数据库.");
            Simulation = new(options);
            // Console.WriteLine($"模拟时间共{Simulation.SimulationDuration.TotalDays}天.");

            throw new NotImplementedException();
            //StrategyContext = new(Simulation.SimulationMarketFrame);

            Strategy = strategy;
            throw new NotImplementedException();
            //Exchange = new(StrategyContext.PortfolioManager, Simulation.SimulationMarketFrame);
        }

        public void Run()
        {
            // Stopwatch stopwatch = new();
            // stopwatch.Start();
            // int loop = 0;

            Strategy.Start(StrategyContext);
            while (Simulation.IsSimulating)
            {
                // 市场价格更新
                Simulation.UpdateFrame();

                // 交易所订单更新
                Exchange.OnPriceUpdate();

                // 投资组合PNL信息更新
                StrategyContext.PortfolioManager.OnPriceUpdate();

                // 时间片更新(用于更新Tick生成指标等数据)
                Strategy.OnTick(StrategyContext);

                // 策略更新(更新策略, 挂单)
                Strategy.OnNext(StrategyContext);

                // loop++;
            }
            Strategy.End(StrategyContext);

            // stopwatch.Stop();
            // Console.WriteLine($"回测已完成, 共测试{loop}帧, 耗时{stopwatch.ElapsedMilliseconds / 1000.0}秒, 回测速度{(double)loop / stopwatch.ElapsedMilliseconds * 1000:F3}帧/秒.");
        }
    }
}
