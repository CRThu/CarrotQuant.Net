using CarrotBacktesting.Net.Portfolio;
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
        /// 当前价格
        /// </summary>
        public double NowPrice;

        /// <summary>
        /// 委托单列表
        /// </summary>
        public OrderManager OrderManager { get; set; } = new();

        /// <summary>
        /// 持仓列表
        /// </summary>
        public PositionManager PositionManager { get; set; } = new();

        public delegate void AddOrderDelegate();
        public event AddOrderDelegate? AddOrderEvent;

        /// <summary>
        /// 实时股价更新
        /// </summary>
        /// <param name="price"></param>
        public void OnPriceUpdate(double price)
        {
            NowPrice = price;
        }

        /// <summary>
        /// 添加委托单
        /// </summary>
        /// <param name="exchangeName"></param>
        /// <param name="shareName"></param>
        /// <param name="limitPrice"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        public void AddOrder(string exchangeName, string shareName, double limitPrice, double size, TradeDirection direction)
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
            PositionManager.Trade(currentOrder.ExchangeName, currentOrder.ShareName, price, size, currentOrder.Direction);

            Console.WriteLine($"委托单已被成交, 价格:{price}, 数量:{size}, 方向:{currentOrder.Direction}.");

            //若全部成交, 则删除委托单
            if (currentOrder.Size == 0)
                OrderManager.RemoveOrder(orderId);

        }
    }
}
