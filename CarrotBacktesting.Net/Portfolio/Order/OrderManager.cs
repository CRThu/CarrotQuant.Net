using CarrotBacktesting.Net.Engine;
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
        private Dictionary<int, GeneralOrder> OrdersStorage { get; set; }

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
        /// 获取委托单
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns></returns>
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
        /// <param name="sender">委托单管理器</param>
        /// <param name="operation">委托单事件参数</param>
        public delegate void OrderUpdateHandler(OrderManager sender, OrderEventArgs operation);

        /// <summary>
        /// 委托单更新事件
        /// </summary>
        public event OrderUpdateHandler? OnOrderUpdate;

        /// <summary>
        /// 构造函数
        /// </summary>
        public OrderManager()
        {
            OrdersStorage = new();
        }

        /// <summary>
        /// 获取委托单
        /// </summary>
        /// <param name="orderId">委托单id</param>
        /// <returns>查询到的委托单</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public GeneralOrder GetOrder(int orderId)
        {
            if (OrdersStorage.TryGetValue(orderId, out GeneralOrder? value))
            {
                return value!;
            }
            else
            {
                throw new InvalidOperationException($"委托单中不存在此委托, Id:{orderId}.");
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
            GeneralOrder order = new(stockCode, type, direction, size, price);
            OrdersStorage.Add(order.OrderId, order);

            OrderEventArgs orderEventArgs = new(order.OrderId, OrderUpdatedEventOperation.CreateOrder);
            OnOrderUpdate?.Invoke(this, orderEventArgs);
            return order.OrderId;
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
            OnOrderUpdate?.Invoke(this, orderEventArgs);
        }

        /// <summary>
        /// 委托单交易更新
        /// </summary>
        /// <param name="orderId">委托单id</param>
        /// <param name="price">成交价格</param>
        /// <param name="size">成交头寸</param>
        public void TradeOrder(int orderId, double price, double size)
        {
            GeneralOrder order = GetOrder(orderId);
            order.Trade(size, price);

            OrderEventArgs orderEventArgs = new(orderId, OrderUpdatedEventOperation.UpdateOrder);
            OnOrderUpdate?.Invoke(this, orderEventArgs);
        }

        /// <summary>
        /// 交易所成交更新事件订阅回调
        /// </summary>
        /// <param name="sender">回测交易所实例</param>
        /// <param name="tradeEventArgs">成交事件参数</param>
        public void OnTradeUpdate(BackTestingExchange sender, TradeEventArgs tradeEventArgs)
        {
            TradeOrder(tradeEventArgs.OrderId, tradeEventArgs.Price, tradeEventArgs.Volume);
        }
    }
}
