using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Order
{
    /// <summary>
    /// 委托单管理器
    /// </summary>
    public class OrderManager
    {
        /// <summary>
        /// 委托单存储字典
        /// </summary>
        public Dictionary<int, GeneralOrder> Orders { get; set; } = new();

        /// <summary>
        /// 委托单数量
        /// </summary>
        public int OrderCount => Orders.Count;

        /// <summary>
        /// 委托单字典自增键生成
        /// </summary>
        private int orderIdGen;
        /// <summary>
        /// 委托单字典自增键生成
        /// </summary>
        public int OrderIdGen
        {
            get
            {
                return orderIdGen++;
            }
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
            Orders.Add(OrderIdGen, new GeneralOrder(exchangeName, shareName, limitPrice, size, direction));
        }

        /// <summary>
        /// 移除委托单
        /// </summary>
        /// <param name="orderId"></param>
        /// <exception cref="Exception"></exception>
        public void RemoveOrder(int orderId)
        {
            if (Orders.ContainsKey(orderId))
                Orders.Remove(orderId);
            else
                throw new Exception($"找不到自增键{orderId}, 无法删除委托单.");
        }

        public GeneralOrder GetOrder(int orderId)
        {
            if (Orders.ContainsKey(orderId))
                return Orders[orderId];
            else
                throw new Exception($"找不到自增键{orderId}, 委托单不存在或已销毁.");
        }
    }
}
