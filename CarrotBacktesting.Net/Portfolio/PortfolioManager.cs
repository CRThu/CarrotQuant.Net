﻿using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio;
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
        public MarketFrame MarketFrame { get; set; } = new();

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


        public delegate void SetCashDelegate(DateTime dateTime, double cash);
        public event SetCashDelegate? OnSetCashEvent;

        public delegate void OrderDealDelegate(DateTime dateTime, GeneralPosition position);
        public event OrderDealDelegate? OnOrderDealEvent;

        public delegate void AddOrderDelegate();
        public event AddOrderDelegate? AddOrderEvent;

        public PortfolioManager(MarketFrame marketFrame)
        {
            MarketFrame = marketFrame;
            EventRegister();
        }

        public void EventRegister()
        {
            OnSetCashEvent += (t, v) => TransactionLogger.SetCash(t, v);
            OnOrderDealEvent += (t, v) => TransactionLogger.AddTransaction(t, v);
        }

        /// <summary>
        /// 实时股价更新
        /// </summary>
        public void OnPriceUpdate()
        {
        }

        /// <summary>
        /// 添加委托单
        /// </summary>
        /// <param name="exchangeName"></param>
        /// <param name="shareName"></param>
        /// <param name="limitPrice"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        public void AddOrder(string exchangeName, string shareName, double limitPrice, double size, OrderDirection direction)
        {
            Console.WriteLine($"委托单已挂单, 价格:{limitPrice}, 数量:{size}, 方向:{direction}.");
            OrderManager.AddOrder(exchangeName, shareName, limitPrice, size, direction);
            AddOrderEvent?.Invoke();
        }

        /// <summary>
        /// 交易所更新委托单成交
        /// </summary>
        /// <param name="orderId"></param>
        /// <param name="price"></param>
        /// <param name="size"></param>
        public void OnExchangeOrderDealUpdate(int orderId, double price, double size)
        {
            var currentOrder = OrderManager.GetOrder(orderId);
            currentOrder.Size -= size;
            var tradePosition = PositionManager.Trade(currentOrder.ExchangeName, currentOrder.ShareName, price, size, currentOrder.Direction);

            Console.WriteLine($"委托单已被成交, 价格:{price}, 数量:{size}, 方向:{currentOrder.Direction}.");

            //若全部成交, 则删除委托单
            if (currentOrder.Size == 0)
                OrderManager.RemoveOrder(orderId);

            OnOrderDealEvent?.Invoke(MarketFrame.NowTime, tradePosition);
        }

        public void SetCash(double cash = 100000)
        {
            PositionManager.SetCash(cash);
            OnSetCashEvent?.Invoke(MarketFrame.NowTime, cash);
        }
    }
}