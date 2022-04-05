using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Strategy;
using System;
using System.Collections.Generic;
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
        public IStrategy Strategy;

        // ex. From DataFeed
        public double[] Price = new[] { 1.0, 2, 3, 4, 5, 6, 5, 5, 6, 6, 7, 6, 5, 4, 3 };

        // 策略通用委托
        public delegate void FuncDelegate();
        public event FuncDelegate? OnTickChanged;
        public event FuncDelegate? OnBarChanged;

        public BackTestingEngine(IStrategy strategy)
        {
            StrategyContext = new();
            Strategy = strategy;
            Exchange = new(StrategyContext.PortfolioManager);

            EventRegister();
        }

        public void Run()
        {
            Strategy.Start(StrategyContext);
            for (int i = 0; i < Price.Length; i++)
            {
                StrategyContext.NowPrice = Price[i];

                // 时间片更新(更新指标等数据 触发PreNext)
                OnTickChanged?.Invoke();
                // 策略更新(更新策略,挂单 触发Next)
                OnBarChanged?.Invoke();

                StrategyContext.NowTime += new TimeSpan(1, 0, 0, 0);
            }
            Strategy.End(StrategyContext);
        }

        public void EventRegister()
        {
            // 时间片触发策略更新
            OnTickChanged += () => Strategy.OnTick(StrategyContext);
            OnBarChanged += () => Strategy.OnNext(StrategyContext);

            // 投资组合管理类价格更新
            OnTickChanged += () => StrategyContext.PortfolioManager.OnPriceUpdate(StrategyContext.NowPrice);

            // 交易所类价格更新
            OnTickChanged += () => Exchange.OnPriceUpdate(StrategyContext.NowPrice);
        }
    }
}
