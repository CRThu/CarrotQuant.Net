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
        /// 未实现损益
        /// </summary>
        public double UnRealizedPnl { get; set; }
        /// <summary>
        /// 已实现损益
        /// </summary>
        public double RealizedPnl { get; set; }
        /// <summary>
        /// 总损益
        /// </summary>
        public double TotalPnl => UnRealizedPnl + RealizedPnl;

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

        public static GeneralPosition operator +(GeneralPosition p1, GeneralPosition p2)
        {
            if (IsSameShare(p1, p2))
            {
                double size = p1.Size + p2.Size;
                double cost = size != 0 ? (p1.Cost * p1.Size + p2.Cost * p2.Size) / (p1.Size + p2.Size) : 0;

                if (size == 0)
                {
                    // 平仓时PNL未实现损益移至已实现损益并清零未实现损益
                    p1.RealizedPnl = p1.UnRealizedPnl + p2.UnRealizedPnl + p1.RealizedPnl + p2.RealizedPnl;
                    p1.UnRealizedPnl = 0;
                    p2.UnRealizedPnl = 0;
                }
                else
                {
                    p1.RealizedPnl = p1.RealizedPnl + p2.RealizedPnl;
                    p1.UnRealizedPnl = p1.UnRealizedPnl + p2.UnRealizedPnl;
                }

                //Console.WriteLine($"RPNL:{p1.RealizedPnl}, URPNL:{p1.UnRealizedPnl}, Size:{size}, Cost:{cost}");

                return new GeneralPosition(p1.ShareName, size, cost)
                {
                    RealizedPnl = p1.RealizedPnl,
                    UnRealizedPnl = p1.UnRealizedPnl
                };
            }
            else
                throw new InvalidOperationException();
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
