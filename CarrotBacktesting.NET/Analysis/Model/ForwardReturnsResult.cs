using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Model
{
    /// <summary>
    /// 存储每个信号未来N天的收益率序列。
    /// </summary>
    public class ForwardReturnsResult
    {
        /// <summary>
        /// 收益率数据。外层List代表每个信号，内层double[]代表该信号未来N天的收益率。
        /// </summary>
        public List<double[]> Returns { get; }

        /// <summary>
        /// 收益率序列的长度 (即回测天数 N)。
        /// </summary>
        public int BacktestDays { get; }

        public ForwardReturnsResult(List<double[]> returns, int backtestDays)
        {
            Returns = returns;
            BacktestDays = backtestDays;
        }
    }
}
