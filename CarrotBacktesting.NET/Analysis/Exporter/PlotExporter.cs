using CarrotBacktesting.NET.Analysis.Model;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Presenters
{
    public class PlotExporter : IExporter
    {
        public string Name => "ScottPlot";
        private string _plotDirectory = "plots";
        private int _backtestDays = 30;

        public void Export(AnalysisContext context)
        {
            // 从上下文中获取所有需要的数据
            var summary = context.GetArtifact<SignalReport>();
            var signals = context.BacktestResult.SignalsResult.Signals.ToList();

            if (summary is null || signals.Count == 0)
            {
                Console.WriteLine("[ScottPlot] 缺少必要的分析数据，无法生成图表。");
                return;
            }

            // 设置中文字体
            // TODO

            // 确保输出目录存在
            _plotDirectory = Path.Combine(context.Config.Runtime.ProjectDir, context.Config.Out.Plots);
            Directory.CreateDirectory(_plotDirectory);
            _backtestDays = summary.BacktestDays;

            Console.WriteLine($"[ScottPlot] 开始生成图表，将保存到: {Path.GetFullPath(_plotDirectory)}");

            // 依次调用绘图方法
            CreatePerformanceOverviewPlot(summary, summary.Returns.Count);
            CreateDistributionTimelinePlot(summary, signals);
            CreateHeatmapPlot(summary);

            Console.WriteLine("[ScottPlot] 图表生成完成。");
        }

        /// <summary>
        /// 绘制【策略表现概览图】
        /// </summary>
        private void CreatePerformanceOverviewPlot(SignalReport summary, int validSignalCount)
        {
            var plt = new Plot();
            plt.Title($"策略在 T+1 至 T+{_backtestDays} 日的表现 (基于 {validSignalCount} 个信号)");
            plt.XLabel("信号后的持有天数 (T+N)");

            double[] days = Enumerable.Range(1, _backtestDays).Select(d => (double)d).ToArray();

            // 左Y轴 - 收益率
            var leftAxis = plt.Axes.Left;
            leftAxis.Label.Text = "收益率";
            //leftAxis.TickGenerator.LabelFormatter = y => $"{y:P1}";
            var avgReturnLine = plt.Add.Scatter(days, summary.AvgReturns.ToArray());
            avgReturnLine.Label = "平均收益率";
            var medianReturnLine = plt.Add.Scatter(days, summary.MedianReturns.ToArray());
            medianReturnLine.Label = "收益率中位数";
            medianReturnLine.LineStyle.Pattern = LinePattern.Dashed;
            plt.Add.HorizontalLine(0, 1, Colors.Gray, LinePattern.Dashed);

            // 右Y轴 - 胜率
            //var rightAxis = plt.Add.Axis.Right();
            //rightAxis.Label.Text = "胜率";
            //rightAxis.TickGenerator.LabelFormatter = y => $"{y:P0}";
            var winRateLine = plt.Add.Scatter(days, summary.WinRates.ToArray());
            winRateLine.Label = "胜率";
            //winRateLine.Axes.YAxis = rightAxis; // 关键：将胜率线关联到右轴
            winRateLine.Color = Colors.Green;

            plt.Legend.IsVisible = true;
            plt.Legend.Location = Alignment.UpperLeft;
            plt.SavePng(Path.Combine(_plotDirectory, "1_performance_overview.png"), 1200, 600);
        }

        /// <summary>
        /// 绘制【信号收益时序分布图】
        /// </summary>
        private void CreateDistributionTimelinePlot(SignalReport returnsResult, IReadOnlyList<Result.SignalInfo> signals)
        {
            const int timelinePlotDay = 18; // 可配置
            if (timelinePlotDay > _backtestDays) return;

            var plt = new Plot();
            plt.Title($"策略信号在 T+{timelinePlotDay} 的收益分布与月度趋势");
            plt.XLabel("信号日期");
            plt.Axes.DateTimeTicksBottom();

            // 准备散点图数据
            var scatterData = new List<(DateTime Date, double Return)>();
            for (int i = 0; i < returnsResult.Returns.Count; i++)
            {
                scatterData.Add((signals[i].Date, returnsResult.Returns[i][timelinePlotDay - 1]));
            }

            // 左Y轴 - 散点图
            var leftAxis = plt.Axes.Left;
            leftAxis.Label.Text = $"单次信号收益率 (T+{timelinePlotDay})";
            //leftAxis.TickGenerator.LabelFormatter = y => $"{y:P0}";
            var scatterGroups = scatterData.GroupBy(d => d.Return > 0);
            foreach (var group in scatterGroups)
            {
                var points = group.Select(p => new Coordinates(p.Date.ToOADate(), p.Return)).ToArray();
                var scatter = plt.Add.Scatter(points);
                scatter.Color = group.Key ? Colors.Green.WithAlpha(0.5) : Colors.Red.WithAlpha(0.5);
                scatter.Label = group.Key ? "盈利信号" : "亏损信号";
            }
            plt.Add.HorizontalLine(0, 1, Colors.Gray, LinePattern.Dashed);

            // 右Y轴 - 月度统计
            //var rightAxis = plt.Add.Axis.Right();
            //rightAxis.Label.Text = "月度统计收益率";
            //rightAxis.TickGenerator.LabelFormatter = y => $"{y:P1}";
            var monthlyStats = scatterData
                .GroupBy(d => new DateTime(d.Date.Year, d.Date.Month, 1))
                .Select(g => new { Month = g.Key, Avg = g.Average(x => x.Return), Median = 0 /*g.Select(x => x.Return).Median()*/ })
                .OrderBy(x => x.Month).ToList();

            if (monthlyStats.Any())
            {
                var monthDates = monthlyStats.Select(m => m.Month.ToOADate()).ToArray();
                var monthAvgs = monthlyStats.Select(m => m.Avg).ToArray();
                var monthMedians = monthlyStats.Select(m => m.Median).ToArray();
                var monthAvgLine = plt.Add.Scatter(monthDates, monthAvgs);
                monthAvgLine.Label = "月度平均收益";
                //monthAvgLine.Axes.YAxis = rightAxis;
                monthAvgLine.Color = Colors.Orange;
                monthAvgLine.MarkerShape = MarkerShape.FilledCircle;
                var monthMedianLine = plt.Add.Scatter(monthDates, monthMedians);
                monthMedianLine.Label = "月度中位数收益";
                //monthMedianLine.Axes.YAxis = rightAxis;
                monthMedianLine.Color = Colors.DarkOrange;
                monthMedianLine.MarkerShape = MarkerShape.Cross;
            }

            plt.Legend.IsVisible = true;
            plt.Legend.Location = Alignment.UpperLeft;
            plt.SavePng(Path.Combine(_plotDirectory, "2_distribution_timeline.png"), 1600, 700);
        }

        /// <summary>
        /// 绘制【收益率分布热力图】
        /// </summary>
        private void CreateHeatmapPlot(SignalReport returnsResult)
        {
            const double binStepPercent = 3;
            const double coarseThresholdPercent = 30;

            // 1. 智能分箱
            var (bins, labels) = CreateSmartBins(binStepPercent, coarseThresholdPercent);

            // 2. 数据处理
            var heatmapData = new double[labels.Length, _backtestDays];
            for (int day = 0; day < _backtestDays; day++)
            {
                var returnsOnDay = returnsResult.Returns.Select(r => r[day]);
                var counts = BinData(returnsOnDay, bins);
                for (int binIdx = 0; binIdx < counts.Length; binIdx++)
                {
                    // 将数量转换为百分比，并反转Y轴
                    heatmapData[labels.Length - 1 - binIdx, day] = (double)counts[binIdx] / returnsResult.Returns.Count * 100;
                }
            }

            // 3. 绘图
            var plt = new Plot();
            plt.Title($"信号后 T+1 至 T+{_backtestDays} 日收益率分布热力图 (基于 {returnsResult.Returns.Count} 个信号)");
            plt.XLabel("持有天数");
            plt.YLabel("收益率区间");

            var heatmap = plt.Add.Heatmap(heatmapData);
            //heatmap.Colormap = new ScottPlot.Palettes.Coolwarm();
            var colorbar = plt.Add.ColorBar(heatmap);
            colorbar.Label = "信号数量占比 (%)";

            // 设置坐标轴刻度标签
            Tick[] xTicks = Enumerable.Range(0, _backtestDays).Select((day, i) => new Tick(i, $"T+{day + 1}")).ToArray();
            plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(xTicks);
            //plt.Axes.Bottom.MajorTickStyle.Rotation = 45;
            Tick[] yTicks = labels.Select((label, i) => new Tick(labels.Length - 1 - i, label)).ToArray();
            plt.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(yTicks);

            plt.SavePng(Path.Combine(_plotDirectory, "3_returns_heatmap.png"), 1600, 800);
        }

        // 热力图的辅助方法
        private (double[] bins, string[] labels) CreateSmartBins(double stepPercent, double thresholdPercent)
        {
            double step = stepPercent / 100.0;
            double threshold = thresholdPercent / 100.0;
            var fineBins = new List<double>();
            for (double b = -threshold; b <= threshold; b += step)
                fineBins.Add(b);

            var allBins = new List<double> { double.NegativeInfinity };
            allBins.AddRange(fineBins);
            allBins.Add(double.PositiveInfinity);
            var bins = allBins.Distinct().OrderBy(b => b).ToArray();

            var labels = new List<string>();
            for (int i = 0; i < bins.Length - 1; i++)
            {
                if (bins[i] == double.NegativeInfinity) labels.Add($"<{bins[i + 1]:P0}");
                else if (bins[i + 1] == double.PositiveInfinity) labels.Add($">{bins[i]:P0}");
                else labels.Add($"{bins[i]:P0} ~ {bins[i + 1]:P0}");
            }
            return (bins, labels.ToArray());
        }

        private int[] BinData(IEnumerable<double> data, double[] bins)
        {
            var counts = new int[bins.Length - 1];
            foreach (var value in data)
            {
                for (int i = 0; i < bins.Length - 1; i++)
                {
                    if (value >= bins[i] && value < bins[i + 1])
                    {
                        counts[i]++;
                        break;
                    }
                }
            }
            return counts;
        }
    }
}
