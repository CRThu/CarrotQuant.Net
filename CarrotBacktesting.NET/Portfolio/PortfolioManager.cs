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

        /// <summary>
        /// 添加委托单
        /// </summary>
        /// <param name="order"></param>
        public void AddOrder(GeneralOrder order)
        {
            Console.WriteLine($"委托单已挂单, 价格{order.LimitPrice}, 数量{order.Size}, 方向{order.Position}.");
            Orders.Add(order);
            CheckOrder();
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

        public void CheckOrder()
        {
            for (int i = 0; i < Orders.Count; i++)
            {
                if ((Orders[i].LimitPrice >= NowPrice && Orders[i].Position == PositionEnum.Long)
                    || (Orders[i].LimitPrice <= NowPrice && Orders[i].Position == PositionEnum.Short))
                {
                    // TODO pos ord 建立单独的类
                    Orders[i].LimitPrice = NowPrice;
                    Positions.Add(new GeneralPosition(Orders[i]));
                    Console.WriteLine($"委托单已被成交, 价格{Orders[i].LimitPrice}, 数量{Orders[i].Size}, 方向{Orders[i].Position}.");
                    Orders.Remove(Orders[i]);
                }
            }
        }
    }
}
