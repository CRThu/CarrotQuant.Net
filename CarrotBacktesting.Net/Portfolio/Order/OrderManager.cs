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
        private Dictionary<int, GeneralOrder> OrdersStorage { get; set; } = new();

        /// <summary>
        /// 全部委托单集合
        /// </summary>
        public IEnumerable<GeneralOrder> Orders
        {
            get
            {
                return OrdersStorage.Values;
            }
        }

        /// <summary>
        /// 待成交委托单集合
        /// </summary>
        public IEnumerable<GeneralOrder> PendingOrders
        {
            get
            {
                return OrdersStorage.Values.Where(o => o.Status == GeneralOrderStatus.Pending);
            }
        }

        /// <summary>
        /// 委托单数量
        /// </summary>
        public int Count => OrdersStorage.Count;

        /// <summary>
        /// 待成交委托单数量
        /// </summary>
        public int PendingCount => OrdersStorage.Values.Count(o => o.Status == GeneralOrderStatus.Pending);

        /// <summary>
        /// 委托单字典自增键
        /// </summary>
        private int orderIdGen = 0;

        /// <summary>
        /// 委托单字典自增键访问器
        /// </summary>
        private int OrderIdGen
        {
            get
            {
                return orderIdGen++;
            }
        }

        /// <summary>
        /// 委托单更新委托
        /// </summary>
        /// <param name="operation"></param>
        public delegate void OrderUpdatedHandler(OrderEventArgs operation);

        /// <summary>
        /// 委托单更新事件
        /// </summary>
        public event OrderUpdatedHandler? OnOrderUpdated;

        /// <summary>
        /// 内部新增委托方法
        /// </summary>
        /// <param name="order">委托单信息</param>
        /// <returns>委托单id</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private int Add(GeneralOrder order)
        {
            int id = OrderIdGen;
            order.Id = id;
            if (OrdersStorage.ContainsKey(id))
            {
                throw new InvalidOperationException($"待添加委托单键相同,Id:{id}.");
            }
            else
            {
                OrdersStorage.Add(id, order);
                return id;
            }
        }

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
            OrdersStorage.Add(OrderIdGen, new GeneralOrder(shareName, limitPrice, size, direction));
            //OnOrderUpdated?.Invoke(OrderIdGen, GetOrder(OrderIdGen), OrderUpdatedEventOperation.AddOrder);
            //return OrderIdGen++;
            throw new NotImplementedException();
        }

        /// <summary>
        /// 移除委托单
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns></returns>
        public bool RemoveOrder(int orderId)
        {
            //OnOrderUpdated?.Invoke(orderId, GetOrder(orderId), OrderUpdatedEventOperation.RemoveOrder);
            throw new NotImplementedException();
            return OrdersStorage.Remove(orderId);
        }

        /// <summary>
        /// 获取委托单
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns>返回委托单, 若不存在则为null</returns>
        public GeneralOrder GetOrder(int orderId)
        {
            if (OrdersStorage.ContainsKey(orderId))
                return OrdersStorage[orderId];
            else
                throw new Exception($"找不到委托单, OrderId={orderId}.");
        }

        /// <summary>
        /// 委托单成交更新
        /// </summary>
        /// <param name="orderId"></param>
        /// <param name="order"></param>
        /// <param name="tradeInfo"></param>
        /// <param name="operation"></param>
        /// <exception cref="Exception"></exception>
        public void OnTradeUpdate(int orderId, GeneralOrder order, (DateTime time, double tradePrice, double tradeVolume) tradeInfo, OrderUpdatedEventOperation operation)
        {
            Console.WriteLine($"委托单管理器:委托单已更新({operation}).\t股票名称:{order.ShareName}, 剩余数量:{order.Size}, 方向:{order.Direction}.");

            switch (operation)
            {
                case OrderUpdatedEventOperation.RemoveOrder:
                    OrdersStorage.Remove(orderId);
                    break;
                case OrderUpdatedEventOperation.UpdateOrder:
                    OrdersStorage[orderId] = order;
                    break;
                default:
                    throw new Exception($"OnTradeUpdate(): operation={operation}, OrderId={orderId}.");
            }
        }
    }
}
