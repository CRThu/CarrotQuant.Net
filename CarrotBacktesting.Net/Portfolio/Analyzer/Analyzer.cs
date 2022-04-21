using CarrotBacktesting.Net.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.Net.Portfolio.Analyzer
{
    /// <summary>
    /// 投资组合分析器
    /// </summary>
    public class Analyzer
    {
        public AnalyzerFrame[] AnalyzerFrames;

        /// <summary>
        /// 构造函数
        /// </summary>
        public Analyzer()
        {
        }

        /// <summary>
        /// 更新分析器
        /// </summary>
        /// <param name="pnlLogger"></param>
        public void Update(PnlLogger pnlLogger)
        {
            AnalyzerFrames = new AnalyzerFrame[pnlLogger.Logs.Count];

            // 存储历史最高总资产用于计算回撤
            double peakDrawdown = 0;

            for (int i = 0; i < pnlLogger.Logs.Count; i++)
            {
                AnalyzerFrames[i].DateTime = pnlLogger.Logs[i].DateTime;
                AnalyzerFrames[i].TotalValue = pnlLogger.Logs[i].TotalValue;
                AnalyzerFrames[i].PositionRatio = pnlLogger.Logs[i].PositionRatio;
                AnalyzerFrames[i].TotalPnl = pnlLogger.Logs[i].TotalPnl;

                // 回撤计算
                if (AnalyzerFrames[i].TotalValue > peakDrawdown)
                    peakDrawdown = AnalyzerFrames[i].TotalValue;
                AnalyzerFrames[i].Drawdown = 1 - AnalyzerFrames[i].TotalValue / peakDrawdown;
            }
        }

        public override string ToString()
        {
            return ClassFormatter.Formatter(AnalyzerFrames);
        }
    }
}
