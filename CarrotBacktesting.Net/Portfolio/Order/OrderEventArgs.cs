using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Order
{
    /// <summary>
    /// 委托单事件参数类
    /// </summary>
    public class OrderEventArgs
    {
        /// <summary>
        /// 委托单操作(创建/更新/取消委托单)
        /// </summary>
        public OrderUpdatedEventOperation Operation { get; set; }

        /// <summary>
        /// 待更新委托单id
        /// </summary>
        public int OrderId { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="orderId">待更新委托单id</param>
        /// <param name="operation">委托单操作</param>
        public OrderEventArgs(int orderId, OrderUpdatedEventOperation operation)
        {
            OrderId = orderId;
            Operation = operation;
        }
    }

    /// <summary>
    /// 委托单操作
    /// </summary>
    public enum OrderUpdatedEventOperation
    {
        /// <summary>
        /// 创建委托单
        /// </summary>
        CreateOrder,
        /// <summary>
        /// 更新委托单
        /// </summary>
        UpdateOrder,
        /// <summary>
        /// 取消委托单
        /// </summary>
        CancelOrder
    }

}
