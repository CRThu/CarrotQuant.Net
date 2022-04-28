using CarrotBacktesting.Net.Portfolio;
using CarrotBacktesting.Net.Portfolio.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Engine
{
    public class BackTestingExchange
    {
        /// <summary>
        /// 委托单存储字典
        /// </summary>
        public Dictionary<int, GeneralOrder> Orders { get; set; } = new();

        /// <summary>
        /// 当前时间市场帧
        /// </summary>
        public MarketFrame MarketFrame;
        public PortfolioManager Portfolio;

        public delegate void OrderDealDelegate(int orderId, GeneralOrder order, (double tradePrice, double tradeVolume) tradeInfo, OrderUpdatedEventOperation operation);
        /// <summary>
        /// 交易所更新委托单成交事件
        /// </summary>
        public event OrderDealDelegate? OrderDealEvent;

        public BackTestingExchange(PortfolioManager portfolio, MarketFrame marketFrame)
        {
            Portfolio = portfolio;
            MarketFrame = marketFrame;

            // 委托单更新事件监听
            Portfolio.OrderManager.OrderUpdateEvent += OnOrderUpdate;
            // 委托单成交事件监听
            OrderDealEvent += Portfolio.OrderManager.OnTradeUpdate;
        }

        /// <summary>
        /// 市场价格更新
        /// </summary>
        public void OnPriceUpdate()
        {
            CheckOrder();
        }

        /// <summary>
        /// 同步交易所的委托单
        /// </summary>
        /// <param name="orderId"></param>
        /// <param name="order"></param>
        /// <param name="operation"></param>
        /// <exception cref="Exception"></exception>
        public void OnOrderUpdate(int orderId, GeneralOrder order, OrderUpdatedEventOperation operation)
        {
            switch (operation)
            {
                case OrderUpdatedEventOperation.AddOrder:
                    Orders.Add(orderId, order);
                    break;
                case OrderUpdatedEventOperation.RemoveOrder:
                    Orders.Remove(orderId);
                    break;
                default:
                    throw new Exception($"OnTradeUpdate(): operation={operation}, OrderId={orderId}.");
            }
        }

        /// <summary>
        /// 检查委托单是否成交
        /// </summary>
        public void CheckOrder()
        {
            foreach (var orderId in Orders.Keys)
            {
                var orderInfo = Orders[orderId];

                if ((orderInfo.LimitPrice >= MarketFrame[orderInfo.ShareName].NowPrice && orderInfo.Direction == OrderDirection.Long)
                    || (orderInfo.LimitPrice <= MarketFrame[orderInfo.ShareName].NowPrice && orderInfo.Direction == OrderDirection.Short))
                {
                    // 模拟委托单全部成交状态
                    Console.WriteLine($"交易所:委托单已被成交.\t时间:{MarketFrame.NowTime:d}, 股票名称:{orderInfo.ShareName}, 价格:{MarketFrame[orderInfo.ShareName].NowPrice}, 数量:{orderInfo.Size}, 方向:{orderInfo.Direction}.");
                    orderInfo.Size = 0;
                    OrderDealEvent?.Invoke(orderId, orderInfo,
                        (MarketFrame[orderInfo.ShareName].NowPrice, orderInfo.Size),
                        OrderUpdatedEventOperation.RemoveOrder);
                    Orders.Remove(orderId);

                    // TODO 仓位管理器更新
                    // TODO 投资组合类中交割单记录器更新
                    //OrderDealEvent?.Invoke(orderId, MarketFrame[orderInfo.ShareName].NowPrice, orderInfo.Size);
                }
            }
        }
    }
}
