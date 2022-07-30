using CarrotBacktesting.Net.Portfolio.Position;
using CarrotBacktesting.Net.Shared;
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
        public List<PnlLog> Logs { get; set; } = new();

        public void AddPnlSnapshot(DateTime dateTime, double shareValue, double cashValue, double realizedPnl, double unrealizedPnl)
        {
            Logs.Add(new PnlLog(dateTime, shareValue, cashValue, realizedPnl, unrealizedPnl));
        }

        public void AddPnlSnapshot(DateTime dateTime, PositionManager positionManager)
        {
            double shareValue = 0, cashValue = positionManager.Cash;
            double realizedPnl = 0, unrealizedPnl = 0;
            foreach (var position in positionManager.PositionsStorage.Values)
            {
                throw new NotImplementedException();
                //shareValue += position.CurrentValue;
                //realizedPnl += position.RealizedPnl;
                //unrealizedPnl += position.UnRealizedPnl;
            }
            Logs.Add(new PnlLog(dateTime, shareValue, cashValue, realizedPnl, unrealizedPnl));
        }

        public override string ToString()
        {
            return ClassFormatter.Formatter(Logs);
        }

        /// <summary>
        /// 调用分析器
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, double> Analyze() => Analyzer.Analyze(this);
    }
}
