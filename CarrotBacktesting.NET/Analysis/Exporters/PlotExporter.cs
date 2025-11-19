using CarrotBacktesting.NET.Analysis.Model;
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

        public void Export(AnalysisContext context)
        {
            // 确保输出目录存在
            _plotDirectory = context.Config.ResolvePath(context.Config.Out.Exporter);
            Directory.CreateDirectory(_plotDirectory);

            Console.WriteLine($"[PlotExporter] 开始生成图表，将保存到: {Path.GetFullPath(_plotDirectory)}");

            // a. 渲染 T+N 信号表现图
            var signalReport = context.GetArtifact<SignalReport[]>();
            if (signalReport != null && signalReport.Length > 0)
            {
                _backtestDays = signalReport.Length;
                CreatePerformanceOverviewPlot(signalReport);
                CreateDistributionTimelinePlot(signalReport);
                CreateHeatmapPlot(signalReport);
            }

            // b. 渲染交易月度表现图
            var tradeReport = context.GetArtifact<TradeReport>();
            if (tradeReport != null)
            {
                var tradesForSignal = context.BacktestResult.Trades; // 获取用于匹配日期的交易列表
                CreateMonthlyTradePerformancePlot(tradeReport, tradesForSignal);
            }

            Console.WriteLine("[PlotExporter] 图表生成完成。");
        }

        /// <summary>
        /// 绘制【信号表现概览图】
        /// </summary>
        private void CreatePerformanceOverviewPlot(SignalReport[] reports)
        {
            if (reports.Length == 0) return;

            var plot = new Plot();
            // X轴：1, 2, ..., N
            double[] days = Enumerable.Range(1, reports.Length).Select(d => (double)d).ToArray();

            // 从数组中提取各天数的全局指标
            double[] avgReturns = reports.Select(r => r.Global.AvgReturn).ToArray();
            double[] medianReturns = reports.Select(r => r.Global.MedianReturn).ToArray();
            double[] winRates = reports.Select(r => r.Global.WinRate).ToArray();

            PlotHelper.ScatterLine(plot, days, avgReturns, "平均收益率");
            PlotHelper.ScatterLine(plot, days, medianReturns, "中位数收益率");
            PlotHelper.ScatterLine(plot, days, winRates, "胜率", color: "#008000", linePattern: LinePattern.DenselyDashed, yAxis: Edge.Right);

            plot.Add.HorizontalLine(0, 1, Colors.Gray, LinePattern.Dashed);

            int signalCount = reports[0].ValidSignalCount;

            string title = $"信号在 T+1 至 T+{reports.Length} 日的表现 (基于 {signalCount} 个信号)";
            string xLabel = "持有天数 (T+N)";
            string yLabel = "收益率";
            string yRightLabel = "胜率";
            PlotHelper.SetStyle(plot, title, xLabel, yLabel, yRightLabel, yTickFormat: "P1", rightTickFormat: "P1");

            string plotPath = Path.Combine(_plotDirectory, "1_信号表现概览图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plot.SavePng(plotPath, 2880, 1720);
        }

        /// <summary>
        /// 绘制【信号收益分布与月度趋势图】
        /// </summary>
        private void CreateDistributionTimelinePlot(SignalReport[] reports)
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
                scatterData.Add((bestReport.Dates[i], bestReport.Returns[i]));
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

            var monthlyStats = scatterData
                .GroupBy(d => new DateTime(d.Date.Year, d.Date.Month, 1))
                .Select(g => new { Month = g.Key, Avg = g.Average(x => x.Return), Median = g.Select(x => x.Return).Median() })
                .OrderBy(x => x.Month).ToList();

            if (monthlyStats.Count != 0)
            {
                var monthDates = monthlyStats.Select(m => m.Month).ToArray();
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

            int signalCount = reports[0].ValidSignalCount;

            string title = $"信号在 T+{timelinePlotDay} 的收益分布与月度趋势 (基于 {signalCount} 个信号)";
            string xLabel = "信号日期";
            string yLabel = $"月度统计收益率";
            string yRightLabel = $"单次信号收益率 (T+{timelinePlotDay})";
            PlotHelper.SetStyle(plot, title, xLabel, yLabel, yRightLabel, yTickFormat: "P1", rightTickFormat: "P1");

            string plotPath = Path.Combine(_plotDirectory, "2_信号收益分布与月度趋势图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plot.SavePng(plotPath, 2880, 1720);
        }

        /// <summary>
        /// 绘制【收益率分布热力图】
        /// </summary>
        private void CreateHeatmapPlot(SignalReport[] reports)
        {
            // 1. 智能分箱
            var (bins, labels) = HistogramHelper.GetBins(0.02, -0.24, 0.24);
            //var (bins, labels) = HistogramHelper.GetBins(0.03, -0.30, 0.30);

            // 2. 数据处理,翻转
            var heatmapData = new double[labels.Length, _backtestDays];
            for (int day = 0; day < _backtestDays; day++)
            {
                var returnsOnDay = reports[day].Returns;
                var counts = returnsOnDay.ToHist(bins, normalize: true);
                for (int binIdx = 0; binIdx < counts.Length; binIdx++)
                {
                    heatmapData[counts.Length - 1 - binIdx, day] = counts[binIdx] * 100;
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

            string title = $"信号后 T+1 至 T+{_backtestDays} 日收益率分布热力图 (基于 {signalCount} 个信号)";
            string xLabel = "持有天数";
            string yLabel = "收益率区间";
            PlotHelper.SetStyle(plot, title, xLabel, yLabel);

            string plotPath = Path.Combine(_plotDirectory, "3_信号收益率分布热力图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plot.SavePng(plotPath, 2880, 1720);
        }

        /// <summary>
        /// 绘制按月统计的交易表现图
        /// </summary>
        private void CreateMonthlyTradePerformancePlot(TradeReport report, List<Trade> trades)
        {
            var result = report.MonthlyStats;

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

            string title = $"按月统计交易表现 (基于 {trades.Count} 个信号)";
            string xLabel = "月份";
            string yLabel = "月度统计收益率";
            string rightLabel = "单次信号收益率";
            PlotHelper.SetStyle(plot, title, xLabel, yLabel, rightLabel, yTickFormat: "P1", rightTickFormat: "P1");

            string plotPath = Path.Combine(_plotDirectory, "4_交易月度表现图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plot.SavePng(plotPath, 2880, 1720);
        }
    }
}
