using CarrotBacktesting.Net.Portfolio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 策略上下文, 策略运行时传递给策略类
    /// </summary>
    public class StrategyContext
    {
        /// <summary>
        /// 投资组合管理器
        /// </summary>
        public PortfolioManager PortfolioManager;

        /// <summary>
        /// 市场信息帧
        /// </summary>
        public MarketFrame MarketFrame;

        public StrategyContext()
        {
            PortfolioManager = new PortfolioManager();
            MarketFrame = new MarketFrame();
        }
    }
}
