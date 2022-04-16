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

        // 策略通用委托
        public delegate void FuncDelegate();
        public event FuncDelegate? OnTickChanged;
        public event FuncDelegate? OnBarChanged;

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

            EventRegister();
        }

        public void Run()
        {
            Stopwatch stopwatch = new();
            stopwatch.Start();
            int loop = 0;

            Strategy.Start(StrategyContext);
            while (!Simulation.IsSimulationEnd)
            {
                Simulation.UpdateFrame();

                // 时间片更新(更新指标等数据 触发PreNext)
                OnTickChanged?.Invoke();
                // 策略更新(更新策略,挂单 触发Next)
                OnBarChanged?.Invoke();

                loop++;
            }
            Strategy.End(StrategyContext);

            stopwatch.Stop();
            Console.WriteLine($"回测已完成, 共测试{loop}帧, 耗时{stopwatch.ElapsedMilliseconds / 1000.0}秒, 回测速度{(double)loop / stopwatch.ElapsedMilliseconds * 1000:F3}帧/秒.");
        }

        public void EventRegister()
        {
            // 投资组合管理类价格更新
            OnTickChanged += () => StrategyContext.PortfolioManager.OnPriceUpdate();

            // 时间片触发策略更新
            OnTickChanged += () => Strategy.OnTick(StrategyContext);
            OnBarChanged += () => Strategy.OnNext(StrategyContext);

            // 交易所类价格更新
            OnTickChanged += () => Exchange.OnPriceUpdate();
        }
    }
}
