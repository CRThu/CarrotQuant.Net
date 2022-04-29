using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Order
{
    public enum OrderUpdatedEventOperation
    {
        AddOrder,
        RemoveOrder,
        UpdateOrder
    }

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
        /// 委托单字典自增键生成
        /// </summary>
        private int OrderIdGen = 0;

        public delegate void OrderUpdatedDelegate(int orderId, GeneralOrder order, OrderUpdatedEventOperation operation);
        /// <summary>
        /// 委托单更新事件
        /// </summary>
        public event OrderUpdatedDelegate? OrderUpdateEvent;

        /// <summary>
        /// 添加委托单
        /// </summary>
        /// <param name="shareName"></param>
        /// <param name="limitPrice"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        /// <returns>返回委托单号</returns>
        public int AddOrder(string shareName, double limitPrice, double size, OrderDirection direction)
        {
            Orders.Add(OrderIdGen, new GeneralOrder(shareName, limitPrice, size, direction));
            OrderUpdateEvent?.Invoke(OrderIdGen, GetOrder(OrderIdGen), OrderUpdatedEventOperation.AddOrder);
            return OrderIdGen++;
        }

        /// <summary>
        /// 移除委托单
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns></returns>
        public bool RemoveOrder(int orderId)
        {
            OrderUpdateEvent?.Invoke(orderId, GetOrder(orderId), OrderUpdatedEventOperation.RemoveOrder);
            return Orders.Remove(orderId);
        }

        /// <summary>
        /// 获取委托单
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns>返回委托单, 若不存在则为null</returns>
        public GeneralOrder GetOrder(int orderId)
        {
            if (Orders.ContainsKey(orderId))
                return Orders[orderId];
            else
                throw new Exception($"找不到委托单, OrderId={orderId}.");
        }

        public void OnTradeUpdate(int orderId, GeneralOrder order, (DateTime time, double tradePrice, double tradeVolume) tradeInfo, OrderUpdatedEventOperation operation)
        {
            Console.WriteLine($"委托单管理器:委托单已更新({operation}).\t股票名称:{order.ShareName}, 剩余数量:{order.Size}, 方向:{order.Direction}.");

            switch (operation)
            {
                case OrderUpdatedEventOperation.RemoveOrder:
                    Orders.Remove(orderId);
                    break;
                case OrderUpdatedEventOperation.UpdateOrder:
                    Orders[orderId] = order;
                    break;
                default:
                    throw new Exception($"OnTradeUpdate(): operation={operation}, OrderId={orderId}.");
            }
        }
    }
}
