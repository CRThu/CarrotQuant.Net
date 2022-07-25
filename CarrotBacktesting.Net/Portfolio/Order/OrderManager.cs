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

        public GeneralOrder this[int orderId]
        {
            get
            {
                return GetOrder(orderId);
            }
        }

        /// <summary>
        /// 委托单更新委托
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="operation"></param>
        public delegate void OrderUpdatedHandler(OrderManager sender, OrderEventArgs operation);

        /// <summary>
        /// 委托单更新事件
        /// </summary>
        public event OrderUpdatedHandler? OnOrderUpdated;

        /// <summary>
        /// 获取委托单
        /// </summary>
        /// <param name="orderId">委托单id</param>
        /// <returns>查询到的委托单</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public GeneralOrder GetOrder(int orderId)
        {
            if (OrdersStorage.ContainsKey(orderId))
            {
                return OrdersStorage[orderId];
            }
            else
            {
                throw new InvalidOperationException($"不存在此键委托单, Id:{orderId}.");
            }
        }

        /// <summary>
        /// 创建委托单
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="price">委托限价(委托单类型为市价时此属性无效)</param>
        /// <param name="size">头寸大小</param>
        /// <param name="direction">头寸方向(买入/卖出)</param>
        /// <param name="type">头寸类型(限价/市价)</param>
        /// <returns>委托单id号</returns>
        public int CreateOrder(string stockCode, OrderDirection direction, double size, OrderType type, double price = 0)
        {
            int orderId = OrderIdGen;
            GeneralOrder order = new(orderId, stockCode, direction, size, type, price);
            OrdersStorage.Add(orderId, order);

            OrderEventArgs orderEventArgs = new(orderId, OrderUpdatedEventOperation.CreateOrder);
            OnOrderUpdated?.Invoke(this, orderEventArgs);
            return orderId;
        }

        /// <summary>
        /// 取消委托单
        /// </summary>
        /// <param name="orderId">委托单id</param>
        /// <exception cref="InvalidOperationException"></exception>
        public void CancelOrder(int orderId)
        {
            GeneralOrder order = GetOrder(orderId);
            order.Cancel();

            OrderEventArgs orderEventArgs = new(orderId, OrderUpdatedEventOperation.CancelOrder);
            OnOrderUpdated?.Invoke(this, orderEventArgs);
        }

        /// <summary>
        /// 委托单交易更新
        /// </summary>
        /// <param name="orderId">委托单id</param>
        /// <param name="size">成交头寸</param>
        /// <param name="price">成交价格</param>
        public void TradeOrder(int orderId, double size, double price)
        {
            GeneralOrder order = GetOrder(orderId);
            order.Trade(size, price);

            OrderEventArgs orderEventArgs = new(orderId, OrderUpdatedEventOperation.UpdateOrder);
            OnOrderUpdated?.Invoke(this, orderEventArgs);
        }

        /// <summary>
        /// 委托单成交更新
        /// TODO
        /// </summary>
        /// <param name="orderId"></param>
        /// <param name="order"></param>
        /// <param name="tradeInfo"></param>
        /// <param name="operation"></param>
        /// <exception cref="Exception"></exception>
        public void OnTradeUpdate(int orderId, GeneralOrder order, (DateTime time, double tradePrice, double tradeVolume) tradeInfo, OrderUpdatedEventOperation operation)
        {
            Console.WriteLine($"委托单管理器:委托单已更新({operation}).\t股票名称:{order.StockCode}, 剩余数量:{order.OrderSize}, 方向:{order.Direction}.");

            throw new NotImplementedException();
            switch (operation)
            {
                //case OrderUpdatedEventOperation.RemoveOrder:
                //    OrdersStorage.Remove(orderId);
                //    break;
                case OrderUpdatedEventOperation.UpdateOrder:
                    OrdersStorage[orderId] = order;
                    break;
                default:
                    throw new Exception($"OnTradeUpdate(): operation={operation}, OrderId={orderId}.");
            }
        }

    }
}
