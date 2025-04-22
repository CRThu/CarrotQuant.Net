using CarrotBacktesting.Net.Common;
using CarrotBacktesting.Net.DataModel;
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
    public class PnlLogger
    {
        /// <summary>
        /// 记录器存储结构
        /// </summary>
        public List<PnlTickLog> Logs { get; init; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PnlLogger()
        {
            Logs = new();
        }

        /// <summary>
        /// 添加损益记录
        /// </summary>
        /// <param name="marketFrame">市场信息</param>
        /// <param name="cashValue">现金价值</param>
        /// <param name="positions">头寸信息</param>
        public void AddPnlTickLog(MarketFrame marketFrame, double cashValue, IEnumerable<GeneralPosition> positions)
        {
            PnlTickLog tickLog = new(marketFrame.Time, cashValue);
            foreach (var position in positions)
            {
                // TODO RelizedPNL
                tickLog.AddPosition(position.StockCode, marketFrame[position.StockCode]!.Close, position.Size, position.CostValue, 0);
            }
            Logs.Add(tickLog);
        }

        /// <summary>
        /// 添加损益记录
        /// </summary>
        /// <param name="marketFrame">市场信息</param>
        /// <param name="manager">头寸管理器</param>
        public void AddPnlTickLog(MarketFrame marketFrame, PositionManager manager)
        {
            AddPnlTickLog(marketFrame, manager.Cash, manager.Positions);
        }

        public override string ToString()
        {
            return ClassFormatter.Formatter(Logs);
        }

        ///// <summary>
        ///// 调用分析器
        ///// </summary>
        ///// <returns></returns>
        //public Dictionary<string, double> Analyze() => AnalyzerManager.Analyze(this);
    }
}
