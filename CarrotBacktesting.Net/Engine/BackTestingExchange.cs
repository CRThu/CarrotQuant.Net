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
        /// 当前时间市场帧
        /// </summary>
        public MarketFrame MarketFrame;
        public PortfolioManager Portfolio;

        public delegate void OrderDealDelegate(int orderId, double price, double size);
        /// <summary>
        /// 交易所更新委托单成交事件
        /// </summary>
        public event OrderDealDelegate? OrderDealEvent;

        public BackTestingExchange(PortfolioManager portfolio, MarketFrame marketFrame)
        {
            Portfolio = portfolio;
            MarketFrame = marketFrame;

            // 委托单添加事件监听
            Portfolio.AddOrderEvent += OnOrderUpdate;
            // 委托单成交事件监听
            OrderDealEvent += Portfolio.OnExchangeOrderDealUpdate;
        }

        /// <summary>
        /// 市场价格更新
        /// </summary>
        public void OnPriceUpdate()
        {
            CheckOrder();
        }

        public void OnOrderUpdate()
        {
            // TODO改为开盘价触发
            CheckOrder();
        }

        /// <summary>
        /// 检查委托单是否成交
        /// </summary>
        public void CheckOrder()
        {
            foreach ((var orderId, var orderInfo) in Portfolio.OrderManager.Orders)
            {
                if ((orderInfo.LimitPrice >= MarketFrame.NowPrice && orderInfo.Direction == OrderDirection.Long)
                    || (orderInfo.LimitPrice <= MarketFrame.NowPrice && orderInfo.Direction == OrderDirection.Short))
                {
                    OrderDealEvent?.Invoke(orderId, MarketFrame.NowPrice, orderInfo.Size);
                }
            }
        }
    }
}
