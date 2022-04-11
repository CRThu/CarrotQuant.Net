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

        // ex. From DataFeed
        public double[] Price = new[] { 1.0, 2, 3, 4, 5, 6, 5, 5, 6, 6, 7, 6, 5, 4, 3 };
        public DateTime BackTestingNowTime = new(2021, 1, 1);

        // 策略通用委托
        public delegate void FuncDelegate();
        public event FuncDelegate? OnTickChanged;
        public event FuncDelegate? OnBarChanged;

        public BackTestingEngine(IStrategy strategy)
        {
            Console.WriteLine($"初始化数据库.");
            var options = new BackTestingSimulationOptions()
            {
                SimulationStartDateTime = new DateTime(0001, 10, 1),
                SimulationEndDateTime = new DateTime(9999, 11, 1),
                ShareName = "sz.000422"
            };
            Simulation = new(@"D:\Projects\CarrotQuant\Stock\Data\StockData_1d_baostock.db", options);
            Console.WriteLine($"模拟时间共{Simulation.SimulationDuration.TotalDays}天.");

            StrategyContext = new(Simulation.SimulationMarketFrame);
            Strategy = strategy;
            Exchange = new(StrategyContext.PortfolioManager, Simulation.SimulationMarketFrame);

            EventRegister();
        }

        public void Run()
        {
            Stopwatch stopwatch = new();
            stopwatch.Start();
            int x = 0;

            Strategy.Start(StrategyContext);
            while (!Simulation.IsSimulationEnd)
            {
                Simulation.UpdateFrame();

                // 时间片更新(更新指标等数据 触发PreNext)
                OnTickChanged?.Invoke();
                // 策略更新(更新策略,挂单 触发Next)
                OnBarChanged?.Invoke();
                x++;
            }
            Strategy.End(StrategyContext);

            stopwatch.Stop();
            Console.WriteLine($"循环{x}次, 回测已完成,耗时{stopwatch.ElapsedMilliseconds / 1000.0}秒.");
        }

        public void EventRegister()
        {
            // 时间片触发策略更新
            OnTickChanged += () => Strategy.OnTick(StrategyContext);
            OnBarChanged += () => Strategy.OnNext(StrategyContext);

            // 投资组合管理类价格更新
            OnTickChanged += () => StrategyContext.PortfolioManager.OnPriceUpdate();

            // 交易所类价格更新
            OnTickChanged += () => Exchange.OnPriceUpdate();
        }
    }
}
