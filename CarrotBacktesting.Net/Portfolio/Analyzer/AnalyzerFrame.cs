using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Analyzer
{
    /// <summary>
    /// 投资组合分析帧
    /// </summary>
    public struct AnalyzerFrame
    {
        /// <summary>
        /// 时间片
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// 总价值
        /// </summary>
        public double TotalValue { get; set; }

        /// <summary>
        /// 仓位占比
        /// </summary>
        public double PositionRatio { get; set; }

        /// <summary>
        /// 总损益
        /// </summary>
        public double TotalPnl { get; set; }

        /// <summary>
        /// 收益率
        /// </summary>
        public double RateOfReturn => TotalPnl / (TotalValue - TotalPnl);

        /// <summary>
        /// 回撤率(恒定本金法)
        /// </summary>
        public double Drawdown { get; set; }
    }
}
