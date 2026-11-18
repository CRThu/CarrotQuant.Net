using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Analysis;

/// <summary>
/// 绩效分析引擎实现，统筹调度执行四层量化统计与信号前向评估。
/// </summary>
public sealed class AnalysisEngine : IAnalysisEngine
{
    /// <inheritdoc />
    public AnalysisReport Analyze(
        TradingResult result,
        AnalysisOptions? options = null,
        ITimeseriesRegistry? timeseries = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var opt = options ?? new AnalysisOptions();

        // 1. 若配置了基准标的，尝试计算基准收益率序列
        IReadOnlyList<double>? benchmarkReturns = TryCalculateBenchmarkReturns(result, opt, timeseries);

        // 2. 依次通过各层 Statistics.Create 静态工厂纯函数式构建统计模型
        var portfolio = PortfolioStatistics.Create(result.EquityCurve, result.DailySnapshots, opt, benchmarkReturns);
        var capital = CapitalStatistics.Create(result.DailySnapshots, result.EquityCurve, result.Settlements, opt);
        var trades = TradeStatistics.Create(result.ClosedTrades, opt, timeseries);
        var cost = CostStatistics.Create(result.Settlements, result.ClosedTrades);
        var signals = SignalStatistics.Create(result.Signals, timeseries, opt);

        return new AnalysisReport(
            Facts: result,
            Portfolio: portfolio,
            Capital: capital,
            Trades: trades,
            Cost: cost,
            Signals: signals
        );
    }

    private static IReadOnlyList<double>? TryCalculateBenchmarkReturns(
        TradingResult result,
        AnalysisOptions options,
        ITimeseriesRegistry? timeseries)
    {
        if (timeseries == null || string.IsNullOrWhiteSpace(options.BenchmarkSymbol))
        {
            return null;
        }

        if (!timeseries.HasField(options.PriceFieldName))
        {
            return null;
        }

        var metadata = timeseries.Metadata;
        int col = metadata.GetSymbolIndex(options.BenchmarkSymbol);
        if (col < 0) return null;

        var matrix = timeseries.GetMatrix<double>(options.PriceFieldName);
        int rowCount = matrix.RowCount;

        if (result.DailySnapshots.Count > 1)
        {
            var list = new List<double>(result.DailySnapshots.Count - 1);
            for (int i = 1; i < result.DailySnapshots.Count; i++)
            {
                int r0 = metadata.GetDateIndex(result.DailySnapshots[i - 1].Time.Date);
                int r1 = metadata.GetDateIndex(result.DailySnapshots[i].Time.Date);
                if (r0 >= 0 && r1 >= 0 && r0 < rowCount && r1 < rowCount)
                {
                    double p0 = matrix[r0, col];
                    double p1 = matrix[r1, col];
                    double ret = (!double.IsNaN(p0) && !double.IsNaN(p1) && p0 > 0)
                        ? (p1 - p0) / p0
                        : 0.0;
                    list.Add(ret);
                }
                else
                {
                    list.Add(0.0);
                }
            }
            return list;
        }

        if (result.EquityCurve.Count > 1)
        {
            var list = new List<double>(result.EquityCurve.Count - 1);
            for (int i = 1; i < result.EquityCurve.Count; i++)
            {
                int r0 = metadata.GetDateIndex(result.EquityCurve[i - 1].Time.Date);
                int r1 = metadata.GetDateIndex(result.EquityCurve[i].Time.Date);
                if (r0 >= 0 && r1 >= 0 && r0 < rowCount && r1 < rowCount)
                {
                    double p0 = matrix[r0, col];
                    double p1 = matrix[r1, col];
                    double ret = (!double.IsNaN(p0) && !double.IsNaN(p1) && p0 > 0)
                        ? (p1 - p0) / p0
                        : 0.0;
                    list.Add(ret);
                }
                else
                {
                    list.Add(0.0);
                }
            }
            return list;
        }

        return null;
    }
}
