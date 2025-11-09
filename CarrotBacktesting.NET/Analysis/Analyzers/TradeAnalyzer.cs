using CarrotBacktesting.NET.Analysis.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Analyzers
{
    public class TradeAnalyzer : IAnalyzer
    {
        public string Name => nameof(TradeAnalyzer);

        public void Analyze(AnalysisContext context)
        {
            var trades = context.BacktestResult.Trades;
            if (trades == null || trades.Count == 0)
            {
                Console.WriteLine("[TradeAnalyzer] 没有交易可供分析。");
                return;
            }

            var report = new TradeReport(trades);
            context.SetArtifact(report);
        }
    }
}
