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
        public StrategyContext StrategyContext;
        public BackTestingExchange Exchange;
        public BackTestingSimulation Simulation;
        public IStrategy Strategy;

        public BackTestingEngine(IStrategy strategy) : this(strategy, new BackTestingSimulationOptions())
        {
        }

        public BackTestingEngine(IStrategy strategy, BackTestingSimulationOptions options)
        {
#if DEBUG
            Console.WriteLine($"初始化数据库.");
#endif
            Simulation = new(options);
#if DEBUG
            Console.WriteLine($"模拟时间共{Simulation.SimulationDuration.TotalDays}天.");
#endif

            StrategyContext = new(Simulation.SimulationMarketFrame);
            Strategy = strategy;
            Exchange = new(StrategyContext.PortfolioManager, Simulation.SimulationMarketFrame);
        }

        public void Run()
        {
            Stopwatch stopwatch = new();
            stopwatch.Start();
            int loop = 0;

            Strategy.Start(StrategyContext);
            while (!Simulation.IsSimulationEnd)
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

                loop++;
            }
            Strategy.End(StrategyContext);

            stopwatch.Stop();
            Console.WriteLine($"回测已完成, 共测试{loop}帧, 耗时{stopwatch.ElapsedMilliseconds / 1000.0}秒, 回测速度{(double)loop / stopwatch.ElapsedMilliseconds * 1000:F3}帧/秒.");
        }
    }
}
