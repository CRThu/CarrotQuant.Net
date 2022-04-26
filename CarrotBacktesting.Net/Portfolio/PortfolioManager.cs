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
        public MarketFrame MarketFrame { get; set; }

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

        public delegate void SetCashDelegate(DateTime dateTime, double cash);
        public event SetCashDelegate? OnSetCashEvent;

        public delegate void OrderDealDelegate(TransactionLog transaction);
        public event OrderDealDelegate? OnOrderDealEvent;

        public delegate void AddOrderDelegate();
        public event AddOrderDelegate? AddOrderEvent;

        public PortfolioManager(MarketFrame marketFrame)
        {
            Analyzer = new Analyzer.Analyzer(TransactionLogger, PnlLogger);
            MarketFrame = marketFrame;
            EventRegister();
        }

        public void EventRegister()
        {
            //交割单记录器
            OnSetCashEvent += (t, v) => TransactionLogger.SetCash(t, v);
            OnOrderDealEvent += (transaction) => TransactionLogger.AddTransaction(transaction);
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
            Console.WriteLine($"委托单已挂单, 股票名称:{shareName}, 价格:{limitPrice}, 数量:{size}, 方向:{direction}.");
            OrderManager.AddOrder(shareName, limitPrice, size, direction);
            AddOrderEvent?.Invoke();
        }

        /// <summary>
        /// 交易所更新委托单成交
        /// TODO 代码整理
        /// </summary>
        /// <param name="orderId"></param>
        /// <param name="price"></param>
        /// <param name="size"></param>
        public void OnExchangeOrderDealUpdate(int orderId, double price, double size)
        {
            var currentOrder = OrderManager.GetOrder(orderId);
            currentOrder.Size -= size;
            PositionManager.Trade(currentOrder.ShareName, price, size, currentOrder.Direction);

            Console.WriteLine($"委托单已被成交, 股票名称:{currentOrder.ShareName}, 价格:{price}, 数量:{size}, 方向:{currentOrder.Direction}.");

            //若全部成交, 则删除委托单
            if (currentOrder.Size == 0)
                OrderManager.RemoveOrder(orderId);

            TransactionLog transaction = new(MarketFrame.NowTime, currentOrder.ShareName, price, size, currentOrder.Direction);
            OnOrderDealEvent?.Invoke(transaction);
        }

        public void SetCash(double cash = 100000)
        {
            PositionManager.Cash = cash;
            OnSetCashEvent?.Invoke(MarketFrame.NowTime, cash);
        }
    }
}
