using CarrotBacktesting.Net.DataModel;
using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    /// <summary>
    /// 回测交易所
    /// </summary>
    public class BackTestingExchange
    {
        /// <summary>
        /// 委托单存储字典
        /// </summary>
        public Dictionary<int, GeneralOrder> Orders { get; set; } = new();

        /// <summary>
        /// 当前时间市场帧
        /// </summary>
        public MarketFrame MarketFrame { get; set; }

        /// <summary>
        /// 当前交易信息
        /// TODO
        /// </summary>
        //public PortfolioManager Portfolio { get; set; }

        /// <summary>
        /// 交易所成交更新委托
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="tradeEventArgs"></param>
        public delegate void TradeUpdateHandler(BackTestingExchange sender, TradeEventArgs tradeEventArgs);

        /// <summary>
        /// 交易所成交更新事件
        /// </summary>
        public event TradeUpdateHandler? OnTradeUpdate;

        /// <summary>
        /// 构造函数
        /// </summary>
        public BackTestingExchange()
        {
            //EventRegister();
        }

        ///// <summary>
        ///// 事件监听
        ///// </summary>
        //public void EventRegister()
        //{
        //    // 委托单更新事件监听
        //    //Portfolio.OrderManager.OnOrderUpdated += OnOrderUpdate;
        //    throw new NotImplementedException();
        //    // 交易所成交更新事件订阅
        //    //OnTradeUpdated += Portfolio.OrderManager.OnTradeUpdate;
        //    // 头寸管理器更新
        //    //OnTradeUpdated += Portfolio.PositionManager.OnTradeUpdate;
        //    throw new NotImplementedException();
        //}

        /// <summary>
        /// 委托单更新事件订阅方法
        /// </summary>
        /// <param name="sender">委托单管理器</param>
        /// <param name="operation">委托单事件参数</param>
        public void OnOrderUpdate(OrderManager sender, OrderEventArgs operation)
        {
            switch (operation.Operation)
            {
                case OrderUpdatedEventOperation.CreateOrder:
                    Orders.Add(operation.OrderId, sender[operation.OrderId]);
                    break;
                case OrderUpdatedEventOperation.CancelOrder:
                    Orders.Remove(operation.OrderId);
                    break;
                case OrderUpdatedEventOperation.UpdateOrder:
                    if (Orders.ContainsKey(operation.OrderId))
                    {
                        Orders[operation.OrderId] = sender[operation.OrderId];
                    }
                    else
                    {
                        throw new InvalidOperationException($"交易所委托单中不存在此委托, Id:{operation.OrderId}.");
                    }
                    break;
            }
        }

        /// <summary>
        /// 市场信息更新事件订阅方法
        /// </summary>
        /// <param name="marketFrame">市场时间</param>
        /// <param name="marketEventArgs">市场是否开盘</param>
        public void OnMarketUpdate(MarketFrame marketFrame, MarketEventArgs marketEventArgs)
        {
            MarketFrame = marketFrame;
        }

        /// <summary>
        /// 检查委托单是否成交
        /// TODO
        /// </summary>
        public void CheckOrder()
        {
            foreach (var orderId in Orders.Keys)
            {
                var orderInfo = Orders[orderId];
                if (MarketFrame[orderInfo.StockCode].IsTrading)
                {
                    // TODO
                    if ((orderInfo.Price >= MarketFrame[orderInfo.StockCode].ClosePrice && orderInfo.Direction == OrderDirection.Buy)
                        || (orderInfo.Price <= MarketFrame[orderInfo.StockCode].ClosePrice && orderInfo.Direction == OrderDirection.Sell))
                    {
                        // 模拟委托单全部成交状态
                        // Console.WriteLine($"交易所({MarketFrame.DateTime:d}):委托单已被成交.\t股票名称:{orderInfo.ShareName}, 价格:{MarketFrame[orderInfo.ShareName].ClosePrice}, 数量:{orderInfo.Size}, 方向:{orderInfo.Direction}.");
                        double tradeSize = orderInfo.OrderSize;
                        orderInfo.OrderSize = 0;
                        throw new NotImplementedException();
                        //OrderDealEvent?.Invoke(orderId, orderInfo,
                        //    (MarketFrame.DateTime, MarketFrame[orderInfo.StockCode].ClosePrice, tradeSize),
                        //    OrderUpdatedEventOperation.RemoveOrder);
                        Orders.Remove(orderId);
                    }
                }
            }
        }
    }
}
