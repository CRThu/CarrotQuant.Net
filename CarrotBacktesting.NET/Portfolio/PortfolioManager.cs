using CarrotBacktesting.NET.Portfolio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Portfolio
{
    public class PortfolioManager
    {
        /// <summary>
        /// 当前价格
        /// </summary>
        public double NowPrice;

        /// <summary>
        /// 委托单列表
        /// </summary>
        public List<GeneralOrder> Orders { get; set; } = new();
        /// <summary>
        /// 持仓列表
        /// </summary>
        public List<GeneralPosition> Positions { get; set; } = new();

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
        /// <param name="order"></param>
        public void AddOrder(GeneralOrder order)
        {
            Console.WriteLine($"委托单已挂单, 价格{order.LimitPrice}, 数量{order.Size}, 方向{order.Position}.");
            Orders.Add(order);
            AddOrderEvent?.Invoke();
        }

        /// <summary>
        /// 交易所更新委托单成交
        /// </summary>
        /// <param name="idx"></param>
        /// <param name="price"></param>
        /// <param name="size"></param>
        public void OnExchangeOrderDealUpdate(int idx, double price, double size)
        {
            Orders[idx].Size -= size;
            var position = new GeneralPosition(Orders[idx]) { Size = size };
            Console.WriteLine($"委托单已被成交, 价格{price}, 数量{position.Size}.");
            if (Orders[idx].Size == 0)
                Orders.Remove(Orders[idx]);
            Positions.Add(position);
        }
    }
}
