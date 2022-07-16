using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio.Analyzer;
using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Portfolio.Position;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio
{
    /// <summary>
    /// 投资组合管理类
    /// </summary>
    public class PortfolioManager
    {
        /// <summary>
        /// 当前时间市场帧
        /// </summary>
        public OldMarketFrame MarketFrame { get; set; }

        /// <summary>
        /// 委托单管理器
        /// </summary>
        public OrderManager OrderManager { get; set; } = new();

        /// <summary>
        /// 头寸管理器
        /// </summary>
        public PositionManager PositionManager { get; set; } = new();

        /// <summary>
        /// 交易记录器
        /// </summary>
        public TransactionLogger TransactionLogger { get; set; } = new();

        /// <summary>
        /// 损益记录器
        /// </summary>
        public PnlLogger PnlLogger { get; set; } = new();

        /// <summary>
        /// 投资组合分析器
        /// </summary>
        public Analyzer.Analyzer Analyzer { get; set; }

        public PortfolioManager(OldMarketFrame marketFrame)
        {
            Analyzer = new Analyzer.Analyzer(TransactionLogger, PnlLogger);
            MarketFrame = marketFrame;
            EventRegister();
        }

        public void EventRegister()
        {
            // 交割单记录器事件
            PositionManager.CashUpdateEvent += TransactionLogger.SetCash;
            PositionManager.PositionUpdateEvent += TransactionLogger.AddTransaction;
        }

        /// <summary>
        /// 实时股价更新
        /// </summary>
        public void OnPriceUpdate()
        {
            PositionManager.OnPriceUpdate(MarketFrame);
            PnlLogger.AddPnlSnapshot(MarketFrame.NowTime, PositionManager);
        }

        /// <summary>
        /// 添加委托单
        /// </summary>
        /// <param name="shareName"></param>
        /// <param name="limitPrice"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        public void AddOrder(string shareName, double limitPrice, double size, OrderDirection direction)
        {
            Console.WriteLine($"{MarketFrame.NowTime:d}:委托单已挂单, 股票名称:{shareName}, 价格:{limitPrice}, 数量:{size}, 方向:{direction}.");
            OrderManager.AddOrder(shareName, limitPrice, size, direction);
        }

        /// <summary>
        /// 添加资金
        /// </summary>
        /// <param name="cash"></param>
        public void SetCash(double cash = 100000) => PositionManager.SetCash(cash);
    }
}
