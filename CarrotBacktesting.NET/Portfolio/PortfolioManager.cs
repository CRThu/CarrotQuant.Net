using CarrotBacktesting.NET.Portfolio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Portfolio
{
    public class PortfolioManager
    {
        /// <summary>
        /// 委托单列表
        /// </summary>
        public List<GeneralOrder> Orders { get; set; } = new();
        /// <summary>
        /// 持仓列表
        /// </summary>
        public List<GeneralPosition> Positions { get; set; } = new();

        public delegate void OrdersUpdateHandle();
        /// <summary>
        /// 当委托单被更新, OnOrdersUpdate会被触发
        /// </summary>
        public event OrdersUpdateHandle? OnOrdersUpdate;

        public void AddOrder(GeneralOrder order)
        {
            Orders.Add(order);
            OnOrdersUpdate?.Invoke();
        }
    }
}
