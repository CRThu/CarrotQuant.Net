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
        public PortfolioManager PortfolioManager;

        /// <summary>
        /// 当前价格
        /// </summary>
        public double NowPrice;
        /// <summary>
        /// 当前日期
        /// </summary>
        public DateTime NowTime;

        public StrategyContext()
        {
            PortfolioManager = new PortfolioManager();
        }
    }
}
