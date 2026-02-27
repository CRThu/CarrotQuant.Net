using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Utility;
using CarrotBacktesting.NET.Utility.ScottPlot;
using ScottPlot;
using ScottPlot.TickGenerators.TimeUnits;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Exporters
{
    public class PlotExporter : IExporter
    {
        public string Name => nameof(PlotExporter);
        private string _plotDirectory = "plots";
        private int _backtestDays = 30;
        private AnalysisContext? _context;

        public void Init(ExporterConfig config)
        {
            _plotDirectory = config.Dir;
        }

        public void Export(AnalysisContext context)
        {
            _context = context;

            string baseDir = context.Config.ResolvePath(context.Config.Out.Dir);
            _plotDirectory = Path.Combine(baseDir, _plotDirectory);
            Directory.CreateDirectory(_plotDirectory);

            Console.WriteLine($"[PlotExporter] 开始生成图表，将保存到: {Path.GetFullPath(_plotDirectory)}");

            var signalResult = context.GetArtifact<SignalAnalysisResult>();
            if (signalResult != null)
            {
                foreach (var groupName in signalResult.Groups.Keys)
                {
                    var reports = signalResult[groupName];
                    if (reports.Length > 0)
                    {
                        _backtestDays = reports.Length;
                        CreatePerformanceOverviewPlot(context, reports, groupName, weighted: false);
                        CreatePerformanceOverviewPlot(context, reports, groupName, weighted: true);
                        CreateDistributionTimelinePlot(context, reports, groupName);
                        CreateHeatmapPlot(context, reports, groupName, weighted: false);
                        CreateHeatmapPlot(context, reports, groupName, weighted: true);
                    }
                }
            }

            var tradeResult = context.GetArtifact<TradeAnalysisResult>();
            if (tradeResult != null)
            {
                var allTrades = context.BacktestResult.Trades;

                foreach (var groupName in tradeResult.Groups.Keys)
                {
                    var report = tradeResult[groupName];

                    var groupTrades = groupName == "Total"
                        ? allTrades
                        : allTrades.Where(t => t.EntryGroup == groupName).ToList();
                    CreateMonthlyTradePerformancePlot(context, report, groupTrades, groupName);
                }
            }

            Console.WriteLine("[PlotExporter] 图表生成完成。");
        }

        /// <summary>
        /// 绘制【信号表现概览图】
        /// </summary>
        private void CreatePerformanceOverviewPlot(AnalysisContext context, SignalReport[] reports, string groupName, bool weighted = false)
        {
            if (reports.Length == 0) return;

            Func<SignalReport, SignalPerf> getPerf = weighted ? r => r.WeightedGlobal : r => r.Global;
            string modeLabel = weighted ? "时间加权" : "信号加权";

            var plot = new Plot();
            double[] days = Enumerable.Range(1, reports.Length).Select(d => (double)d).ToArray();

            double[] avgReturns = reports.Select(r => getPerf(r).AvgReturn).ToArray();
            double[] medianReturns = reports.Select(r => getPerf(r).MedianReturn).ToArray();
            double[] winRates = reports.Select(r => getPerf(r).WinRate).ToArray();

            PlotHelper.ScatterLine(plot, days, avgReturns, "平均收益率");
            PlotHelper.ScatterLine(plot, days, medianReturns, "中位数收益率");
            PlotHelper.ScatterLine(plot, days, winRates, "胜率", color: "#008000", linePattern: LinePattern.DenselyDashed, yAxis: Edge.Right);

            plot.Add.HorizontalLine(0, 1, Colors.Gray, LinePattern.Dashed);

            int signalCount = reports[0].ValidSignalCount;

            string title = $"[{groupName}] 信号在 T+1 至 T+{reports.Length} 日的表现 (基于 {signalCount} 个信号, {modeLabel})";
            string xLabel = "持有天数 (T+N)";
            string yLabel = "收益率";
            string yRightLabel = "胜率";
            PlotHelper.SetStyle(plot, title, xLabel, yLabel, yRightLabel, yTickFormat: "P1", rightTickFormat: "P1");

            string plotPath = Path.Combine(_plotDirectory, $"[{groupName}] 1_信号表现概览图_{modeLabel}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plot.SavePng(plotPath, 2880, 1720);

            string key = $"Plot_{groupName}_Overview_{(weighted ? "Weighted" : "Signal")}";
            context.SetFileArtifact(key, plotPath);
        }

        /// <summary>
        /// 绘制【信号收益分布与月度趋势图】
        /// </summary>
        private void CreateDistributionTimelinePlot(AnalysisContext context, SignalReport[] reports, string groupName)
        {
            if (reports.Length == 0) return;

            // 1. 找到平均收益率最高的那个周期的报告
            var bestReport = reports.MaxBy(r => r.Global.AvgReturn)!;
            int timelinePlotDay = Array.IndexOf(reports, bestReport) + 1;

            var plot = new Plot();

            // 2. 准备散点图数据
            var scatterData = new List<(DateTime Date, double Return)>();
            for (int i = 0; i < bestReport.Returns.Count; i++)
            {
                scatterData.Add((bestReport.Trades[i].EntryDate, bestReport.Returns[i]));
            }

            plot.Add.HorizontalLine(0, 1, Colors.Gray, LinePattern.DenselyDashed);

            // 步骤3: 绘制散点图 (分组着色)
            var positiveReturns = scatterData.Where(d => d.Return > 0).ToList();
            var negativeReturns = scatterData.Where(d => d.Return <= 0).ToList();

            if (positiveReturns.Count != 0)
            {
                PlotHelper.Scatter(plot,
                    positiveReturns.Select(p => p.Date).ToArray(),
                    positiveReturns.Select(p => p.Return).ToArray(),
                    legend: "盈利信号",
                    color: "#2ecc71", alpha: 0.5,
                    yAxis: Edge.Right);
            }
            if (negativeReturns.Count != 0)
            {
                PlotHelper.Scatter(plot,
                    negativeReturns.Select(p => p.Date).ToArray(),
                    negativeReturns.Select(p => p.Return).ToArray(),
                    legend: "亏损信号",
                    color: "#e74c3c", alpha: 0.5,
                    yAxis: Edge.Right);
            }

            var monthlyStats = bestReport.Monthly;

            if (monthlyStats.Count != 0)
            {
                var monthDates = monthlyStats.Select(m => m.Month).ToArray();
                var monthAvgs = monthlyStats.Select(m => m.Perf.AvgReturn).ToArray();
                var monthMedians = monthlyStats.Select(m => m.Perf.MedianReturn).ToArray();

                PlotHelper.ScatterLine(plot,
                    monthDates,
                    monthAvgs,
                    legend: "月度平均收益",
                    color: "#f1c40f",
                    markerShape: MarkerShape.OpenCircle);
                PlotHelper.ScatterLine(plot,
                    monthDates,
                    monthMedians,
                    legend: "月度中位数收益",
                    color: "#e67e22",
                    markerShape: MarkerShape.Eks,
                    linePattern: LinePattern.DenselyDashed);
           }

            int signalCount = reports[0].ValidSignalCount;

            string title = $"[{groupName}] 信号在 T+{timelinePlotDay} 的收益分布与月度趋势 (基于 {signalCount} 个信号)";
            string xLabel = "信号日期";
            string yLabel = $"月度统计收益率";
            string yRightLabel = $"单次信号收益率 (T+{timelinePlotDay})";
            PlotHelper.SetStyle(plot, title, xLabel, yLabel, yRightLabel, yTickFormat: "P1", rightTickFormat: "P1");

            string plotPath = Path.Combine(_plotDirectory, $"[{groupName}] 2_信号收益分布与月度趋势图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plot.SavePng(plotPath, 2880, 1720);

            context.SetFileArtifact($"Plot_{groupName}_Timeline", plotPath);
        }

        /// <summary>
        /// 绘制【收益率分布热力图】
        /// </summary>
        private void CreateHeatmapPlot(AnalysisContext context, SignalReport[] reports, string groupName, bool weighted = false)
        {
            if (reports.Length == 0) return;

            // 1. 智能分箱
            var (bins, labels) = HistogramHelper.GetBins(0.02, -0.24, 0.24);

            // 2. 准备数据容器
            var heatmapData = new double[labels.Length, reports.Length];

            // 3. 遍历每一天 (T+1 ... T+N)
            for (int day = 0; day < reports.Length; day++)
            {
                var r = reports[day];
                double[]? weights = null;

                if (weighted)
                {
                    // 默认 weights 为 null，对应 weighted = false (信号加权)
                    weights = new double[r.ValidSignalCount];
                    // 按月分组索引
                    var monthlyGroups = r.Trades
                        .Select((trade, index) => new { Date = trade.EntryDate, Index = index })
                        .GroupBy(x => new { x.Date.Year, x.Date.Month });

                    foreach (var group in monthlyGroups)
                    {
                        double weightPerSignal = 1.0 / group.Count();
                        foreach (var item in group)
                        {
                            weights[item.Index] = weightPerSignal;
                        }
                    }
                }

                var finalDistribution = r.Returns.ToHist(bins, weights, normalize: true);

                // 4. 填充热力图矩阵 (翻转Y轴，并转换为 0-100 的数值)
                for (int binIdx = 0; binIdx < finalDistribution.Length; binIdx++)
                {
                    // heatmapData[行, 列]
                    heatmapData[labels.Length - 1 - binIdx, day] = finalDistribution[binIdx] * 100;
                }
            }

            // 3. 绘图
            var plot = new Plot();
            string[] xTicks = Enumerable.Range(0, _backtestDays).Select(d => $"T+{d + 1}").ToArray();

            PlotHelper.Heatmap(plot, heatmapData,
                xLabels: xTicks,
                yLabels: labels.Reverse().ToArray(),
                cLabel: "信号数量占比 (%)",
                v: (0, 3, 10),
                annoFormat: "F1");

            int signalCount = reports[0].ValidSignalCount;
            string modeLabel = weighted ? "时间加权" : "信号加权";
            string title = $"[{groupName}] 信号后 T+1 至 T+{_backtestDays} 日收益率分布热力图 (基于 {signalCount} 个信号, {modeLabel})";
            string xLabel = "持有天数";
            string yLabel = "收益率区间";
            PlotHelper.SetStyle(plot, title, xLabel, yLabel);

            string plotPath = Path.Combine(_plotDirectory, $"[{groupName}] 3_信号收益率分布热力图_{modeLabel}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plot.SavePng(plotPath, 2880, 1720);

            string key = $"Plot_{groupName}_Heatmap_{(weighted ? "Weighted" : "Signal")}";
            context.SetFileArtifact(key, plotPath);
        }

        /// <summary>
        /// 绘制按月统计的交易表现图
        /// </summary>
        private void CreateMonthlyTradePerformancePlot(AnalysisContext context, TradeReport report, List<Trade> trades, string groupName)
        {
            var plot = new Plot();

            // 准备散点图数据
            var scatterData = new List<(DateTime Date, double Return)>();
            for (int i = 0; i < trades.Count; i++)
            {
                scatterData.Add((trades[i].EntryDate, trades[i].Return ?? 0));
            }

            // 步骤3: 绘制散点图 (分组着色)
            var positiveReturns = scatterData.Where(d => d.Return > 0).ToList();
            var negativeReturns = scatterData.Where(d => d.Return <= 0).ToList();

            if (positiveReturns.Count != 0)
            {
                PlotHelper.Scatter(plot,
                    positiveReturns.Select(p => p.Date).ToArray(),
                    positiveReturns.Select(p => p.Return).ToArray(),
                    legend: "盈利信号",
                    color: "#2ecc71", alpha: 0.5,
                    yAxis: Edge.Right);
            }
            if (negativeReturns.Count != 0)
            {
                PlotHelper.Scatter(plot,
                    negativeReturns.Select(p => p.Date).ToArray(),
                    negativeReturns.Select(p => p.Return).ToArray(),
                    legend: "亏损信号",
                    color: "#e74c3c", alpha: 0.5,
                    yAxis: Edge.Right);
            }
            plot.Add.HorizontalLine(0, 1, Colors.Gray, LinePattern.DenselyDashed);

            var monthlyStats = scatterData
                .GroupBy(d => new DateTime(d.Date.Year, d.Date.Month, 1))
                .Select(g => new { Month = g.Key, Avg = g.Average(x => x.Return), Median = g.Select(x => x.Return).Median() })
                .OrderBy(x => x.Month).ToList();

            if (monthlyStats.Count != 0)
            {
                var monthDates = monthlyStats.Select(m => m.Month.ToOADate()).ToArray();
                var monthAvgs = monthlyStats.Select(m => m.Avg).ToArray();
                var monthMedians = monthlyStats.Select(m => m.Median).ToArray();

                PlotHelper.ScatterLine(plot,
                    monthDates,
                    monthAvgs,
                    legend: "月度平均收益",
                    color: "#f1c40f",
                    markerShape: MarkerShape.OpenCircle);
                PlotHelper.ScatterLine(plot,
                    monthDates,
                    monthMedians,
                    legend: "月度中位数收益",
                    color: "#e67e22",
                    markerShape: MarkerShape.Eks,
                    linePattern: LinePattern.DenselyDashed);
            }

            string title = $"[{groupName}] 按月统计交易表现 (基于 {trades.Count} 个信号)";
            string xLabel = "月份";
            string yLabel = "月度统计收益率";
            string rightLabel = "单次信号收益率";
            PlotHelper.SetStyle(plot, title, xLabel, yLabel, rightLabel, yTickFormat: "P1", rightTickFormat: "P1");

            string plotPath = Path.Combine(_plotDirectory, $"[{groupName}] 4_交易月度表现图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plot.SavePng(plotPath, 2880, 1720);

            context.SetFileArtifact($"Plot_{groupName}_Monthly", plotPath);
        }
    }
}
