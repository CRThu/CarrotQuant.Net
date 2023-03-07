using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Analyzer
{
    /// <summary>
    /// 时间片内头寸数据记录
    /// </summary>
    public class PnlPositionLog
    {
        /// <summary>
        /// 股票代码
        /// </summary>
        public string StockCode { get; init; }

        /// <summary>
        /// 头寸大小
        /// </summary>
        public double Size { get; init; }

        /// <summary>
        /// 头寸持仓成本总价值
        /// </summary>
        public double CostValue { get; init; }

        /// <summary>
        /// 头寸平均持仓成本
        /// </summary>
        public double Cost => CostValue / Size;

        /// <summary>
        /// 当前价格
        /// </summary>
        public double CurrentPrice { get; init; }

        /// <summary>
        /// 未实现损益
        /// </summary>
        public double UnRealizedPnl => (CurrentPrice - Cost) * Size;

        /// <summary>
        /// 已实现损益
        /// </summary>
        public double RealizedPnl { get; init; }

        /// <summary>
        /// 总损益
        /// </summary>
        public double TotalPnl => UnRealizedPnl + RealizedPnl;

        /// <summary>
        /// 当前价值
        /// </summary>
        public double CurrentValue => CurrentPrice * Size;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="size">头寸大小</param>
        /// <param name="costValue">头寸持仓成本总价值</param>
        /// <param name="currentPrice">当前价格</param>
        /// <param name="realizedPnl">已实现损益</param>
        public PnlPositionLog(string stockCode, double size, double costValue, double currentPrice, double realizedPnl)
        {
            StockCode = stockCode;
            Size = size;
            CostValue = costValue;
            CurrentPrice = currentPrice;
            RealizedPnl = realizedPnl;
        }
    }
}
