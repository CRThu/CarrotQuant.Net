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
    /// 常规头寸
    /// </summary>
    public class GeneralPosition
    {
        /// <summary>
        /// 股票名称
        /// </summary>
        public string StockName { get; set; }

        /// <summary>
        /// 头寸大小
        /// </summary>
        public double Size { get; set; }

        /// <summary>
        /// 头寸平均持仓成本
        /// </summary>
        public double Cost => CostValue / Size;

        /// <summary>
        /// 头寸持仓成本总价值
        /// </summary>
        public double CostValue { get; set; }

        ///// <summary>
        ///// 当前价格
        ///// </summary>
        //public double CurrentPrice { get; set; }

        ///// <summary>
        ///// 未实现损益
        ///// </summary>
        //public double UnRealizedPnl => (CurrentPrice - Cost) * Size;

        ///// <summary>
        ///// 已实现损益
        ///// </summary>
        //public double RealizedPnl { get; set; }

        ///// <summary>
        ///// 总损益
        ///// </summary>
        //public double TotalPnl => UnRealizedPnl + RealizedPnl;

        ///// <summary>
        ///// 当前价值
        ///// </summary>
        //public double CurrentValue => CurrentPrice * Size;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="stockName">股票代码</param>
        /// <param name="size">头寸大小</param>
        /// <param name="cost">股票单价</param>
        public GeneralPosition(string stockName, double size, double cost)
        {
            StockName = stockName;
            Trade(size, cost);
        }

        /// <summary>
        /// 交易
        /// </summary>
        /// <param name="size">头寸大小</param>
        /// <param name="cost">股票单价</param>
        public void Trade(double size, double cost)
        {
            Size += size;
            CostValue += cost * size;
        }
    }
}
