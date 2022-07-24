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
        // int orderId, GeneralOrder order, OrderUpdatedEventOperation operation
    }

    /// <summary>
    /// 
    /// </summary>
    public enum OrderUpdatedEventOperation
    {
        CreateOrder,
        RemoveOrder,
        UpdateOrder
    }

}
