using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Result
{
    public class BacktestingResult
    {
        /// <summary>
        /// 本次回测生成的信号集合。
        /// 它在构造时就被创建，并可以在回测过程中被填充。
        /// </summary>
        public SignalSet SignalsResult { get; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        public BacktestingResult()
        {
            SignalsResult = new SignalSet();
        }
    }
}
