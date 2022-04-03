using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Portfolio
{
    public class BackTestingExchange
    {
        /// <summary>
        /// 当前价格
        /// </summary>
        public double NowPrice;
        public PortfolioManager Portfolio;


        public delegate void OrderDealDelegate(int idx, double price, double size);
        /// <summary>
        /// 交易所更新委托单成交事件
        /// </summary>
        public event OrderDealDelegate? OrderDealEvent;

        public BackTestingExchange(PortfolioManager portfolio)
        {
            Portfolio = portfolio;

            // 委托单添加事件监听
            Portfolio.AddOrderEvent += OnOrderUpdate;
            // 委托单成交事件监听
            OrderDealEvent += Portfolio.OnExchangeOrderDealUpdate;
        }

        /// <summary>
        /// 实时股价更新
        /// </summary>
        /// <param name="price"></param>
        public void OnPriceUpdate(double price)
        {
            NowPrice = price;
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
            for (int i = 0; i < Portfolio.Orders.Count; i++)
            {
                if ((Portfolio.Orders[i].LimitPrice >= NowPrice && Portfolio.Orders[i].Position == PositionDirectionEnum.Long)
                    || (Portfolio.Orders[i].LimitPrice <= NowPrice && Portfolio.Orders[i].Position == PositionDirectionEnum.Short))
                {
                    OrderDealEvent?.Invoke(i, NowPrice, Portfolio.Orders[i].Size);
                }
            }
        }
    }
}
