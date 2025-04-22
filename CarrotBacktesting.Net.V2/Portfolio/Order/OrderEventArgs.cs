using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CarrotBacktesting.Net.Common.Enums;

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
}
