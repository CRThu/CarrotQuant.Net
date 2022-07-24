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
        /// 当前价格
        /// </summary>
        public double CurrentPrice { get; set; }
        /// <summary>
        /// 未实现损益
        /// </summary>
        public double UnRealizedPnl => (CurrentPrice - Cost) * Size;
        /// <summary>
        /// 已实现损益
        /// </summary>
        public double RealizedPnl { get; set; }
        /// <summary>
        /// 总损益
        /// </summary>
        public double TotalPnl => UnRealizedPnl + RealizedPnl;
        /// <summary>
        /// 成本价值
        /// </summary>
        public double CostValue => Cost * Size;
        /// <summary>
        /// 当前价值
        /// </summary>
        public double CurrentValue => CurrentPrice * Size;


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="shareName"></param>
        /// <param name="size"></param>
        /// <param name="direction"></param>
        public GeneralPosition(string shareName, double size, double cost, OrderDirection direction = OrderDirection.Buy)
        {
            ShareName = shareName;
            Size = direction == OrderDirection.Buy ? size : -size;
            Cost = cost;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="generalOrder"></param>
        public GeneralPosition(GeneralOrder generalOrder, double price)
        {
            ShareName = generalOrder.ShareName;
            Size = generalOrder.Direction == OrderDirection.Buy ? generalOrder.Size : -generalOrder.Size;
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
    }
}
