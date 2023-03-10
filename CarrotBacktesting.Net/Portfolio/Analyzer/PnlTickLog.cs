using CarrotBacktesting.Net.Engine;
using CarrotBacktesting.Net.Portfolio.Position;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Analyzer
{
    /// <summary>
    /// 损益记录器
    /// </summary>
    public class PnlTickLog
    {
        /// <summary>
        /// 时间片
        /// </summary>
        public DateTime Time { get; init; }

        /// <summary>
        /// 头寸记录
        /// </summary>
        public List<PnlPositionLog> PositionLogs { get; init; }

        /// <summary>
        /// 现金价值
        /// </summary>
        public double CashValue { get; init; }

        /// <summary>
        /// 当前价值
        /// </summary>
        public double CurrentValue => PositionLogs.Sum(p => p.CurrentValue);

        /// <summary>
        /// 总价值
        /// </summary>
        public double TotalValue => CurrentValue + CashValue;

        /// <summary>
        /// 仓位占比
        /// </summary>
        public double PositionRatio => CurrentValue / (CurrentValue + CashValue);

        /// <summary>
        /// 已实现损益
        /// </summary>
        public double RealizedPnl => PositionLogs.Sum(p => p.RealizedPnl);

        /// <summary>
        /// 未实现损益
        /// </summary>
        public double UnRealizedPnl => PositionLogs.Sum(p => p.UnRealizedPnl);

        /// <summary>
        /// 总损益
        /// </summary>
        public double TotalPnl => PositionLogs.Sum(p => p.TotalPnl);

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dateTime">日期</param>
        /// <param name="cashValue">当前持有现金</param>
        public PnlTickLog(DateTime dateTime, double cashValue)
        {
            Time = dateTime;
            CashValue = cashValue;
            PositionLogs = new();
        }

        /// <summary>
        /// 添加头寸记录
        /// </summary>
        /// <param name="stockCode">股票代码</param>
        /// <param name="currentPrice">当前价格</param>
        /// <param name="size">头寸大小</param>
        /// <param name="costValue">头寸持仓成本总价值</param>
        /// <param name="realizedPnl">已实现损益</param>
        public void AddPosition(string stockCode, double currentPrice, double size, double costValue, double realizedPnl)
        {
            PositionLogs.Add(new PnlPositionLog(stockCode, currentPrice, size, costValue, realizedPnl));
        }
    }
}
