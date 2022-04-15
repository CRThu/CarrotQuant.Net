using CarrotBacktesting.Net.Portfolio.Order;
using CarrotBacktesting.Net.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Position
{
    /// <summary>
    /// 常规头寸存储类
    /// </summary>
    public class GeneralPosition
    {
        /// <summary>
        /// 股票名称
        /// </summary>
        public string ShareName { get; set; }
        /// <summary>
        /// 头寸大小
        /// </summary>
        public double Size { get; set; }
        /// <summary>
        /// 头寸持仓成本
        /// </summary>
        public double Cost { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="shareName"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        public GeneralPosition(string shareName, double size, double cost, OrderDirection direction = OrderDirection.Long)
        {
            ShareName = shareName;
            Size = direction == OrderDirection.Long ? size : -size;
            Cost = cost;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="generalOrder"></param>
        public GeneralPosition(GeneralOrder generalOrder, double price)
        {
            ShareName = generalOrder.ShareName;
            Size = generalOrder.Direction == OrderDirection.Long ? generalOrder.Size : -generalOrder.Size;
            Cost = price;
        }

        /// <summary>
        /// 判断两个头寸类是否为同一标的
        /// </summary>
        /// <param name="positionA"></param>
        /// <param name="positionB"></param>
        /// <returns></returns>
        public static bool IsSameShare(GeneralPosition positionA, GeneralPosition positionB)
        {
            return positionA.ShareName == positionB.ShareName;
        }

        /// <summary>
        /// 判断两个头寸类是否为同一标的
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public bool IsSameShare(GeneralPosition position)
        {
            return IsSameShare(this, position);
        }

        public override string ToString()
        {
            return Serializer.Serialize(this);
        }
    }
}
