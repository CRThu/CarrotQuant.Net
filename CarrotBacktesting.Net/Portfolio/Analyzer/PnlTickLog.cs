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
        /// 权益价值
        /// </summary>
        public double ShareValue { get; init; }

        /// <summary>
        /// 现金价值
        /// </summary>
        public double CashValue { get; init; }

        /// <summary>
        /// 总价值
        /// </summary>
        public double TotalValue => ShareValue + CashValue;

        /// <summary>
        /// 仓位占比
        /// </summary>
        public double PositionRatio => ShareValue / (ShareValue + CashValue);

        /// <summary>
        /// 已实现损益
        /// </summary>
        public double RealizedPnl { get; set; }

        /// <summary>
        /// 未实现损益
        /// </summary>
        public double UnRealizedPnl { get; set; }

        /// <summary>
        /// 总损益
        /// </summary>
        public double TotalPnl => RealizedPnl + UnRealizedPnl;

        /// <summary>
        /// 收益率
        /// </summary>
        public double RateOfReturn => TotalPnl / (TotalValue - TotalPnl);

        public PnlTickLog(DateTime dateTime, double shareValue, double cashValue, double realizedPnl, double unrealizedPnl)
        {
            Time = dateTime;
            ShareValue = shareValue;
            CashValue = cashValue;
            RealizedPnl = realizedPnl;
            UnRealizedPnl = unrealizedPnl;
        }
    }
}
