using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Portfolio.Analyzer;
using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Portfolio.Position;
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
        public PortfolioManager PortfolioManager { get; set; }

        /// <summary>
        /// 回测模拟器
        /// </summary>
        public TickSimulator Simulator { get; set; }

        /// <summary>
        /// 委托管理器
        /// </summary>
        public OrderManager OrderManager => PortfolioManager.OrderManager;
        
        /// <summary>
        /// 头寸管理器
        /// </summary>
        public PositionManager PositionManager => PortfolioManager.PositionManager;

        /// <summary>
        /// 委托管理器
        /// </summary>
        public AnalyzerManager Analyzer => PortfolioManager.Analyzer;

        /// <summary>
        /// 交易记录器
        /// </summary>
        public TransactionLogger TransactionLogger => PortfolioManager.Analyzer.TransactionLogger;
        
        /// <summary>
        /// 损益记录器
        /// </summary>
        public PnlLogger PnlLogger => PortfolioManager.Analyzer.PnlLogger;

        /// <summary>
        /// 构造函数
        /// </summary>
        public StrategyContext()
        {
        }
    }
}
