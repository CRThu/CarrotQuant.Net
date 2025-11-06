using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Utility;
using CarrotBacktesting.NET.Utility.ScottPlot;
using ScottPlot;
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
            // 从上下文中获取所有需要的数据
            var summary = context.GetArtifact<SignalReport>();
            var signals = context.BacktestResult.SignalsResult.Signals.ToList();

            if (summary is null || signals.Count == 0)
            {
                Console.WriteLine("[ScottPlot] 缺少必要的分析数据，无法生成图表。");
                return;
            }

            // 确保输出目录存在
            _plotDirectory = Path.Combine(context.Config.Runtime.ProjectDir, context.Config.Out.Exporter);
            Directory.CreateDirectory(_plotDirectory);
            _backtestDays = summary.BacktestDays;

            Console.WriteLine($"[ScottPlot] 开始生成图表，将保存到: {Path.GetFullPath(_plotDirectory)}");

            // 依次调用绘图方法
            CreatePerformanceOverviewPlot(summary);
            CreateDistributionTimelinePlot(summary, signals);
            CreateHeatmapPlot(summary);

            Console.WriteLine("[ScottPlot] 图表生成完成。");
        }

        /// <summary>
        /// 绘制【信号表现概览图】
        /// </summary>
        private void CreatePerformanceOverviewPlot(SignalReport returnsResult)
        {
            var plt = new Plot();
            plt.ScaleFactor = 2;
            plt.Font.Set("Microsoft YaHei UI");

            plt.Title($"信号在 T+1 至 T+{_backtestDays} 日的表现 (基于 {returnsResult.Returns.Count} 个信号)");
            plt.XLabel("持有天数 (T+N)");

            double[] days = Enumerable.Range(1, _backtestDays).Select(d => (double)d).ToArray();

            // 左Y轴 - 收益率
            var leftAxis = plt.Axes.Left;
            leftAxis.Label.Text = "收益率";
            // 以百分比格式显示刻度标签
            leftAxis.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic() { LabelFormatter = y => $"{y:P1}" };

            var avgReturnLine = plt.Add.Scatter(days, returnsResult.AvgReturns.ToArray());
            avgReturnLine.LegendText = "平均收益率";
            var medianReturnLine = plt.Add.Scatter(days, returnsResult.MedianReturns.ToArray());
            medianReturnLine.LegendText = "收益率中位数";
            plt.Add.HorizontalLine(0, 1, Colors.Gray, LinePattern.Dashed);

            // 右Y轴 - 胜率
            var rightAxis = plt.Axes.Right;
            rightAxis.Label.Text = "胜率";
            rightAxis.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic() { LabelFormatter = y => $"{y:P1}" };

            var winRateLine = plt.Add.Scatter(days, returnsResult.WinRates.ToArray());
            winRateLine.LegendText = "胜率";
            winRateLine.LineStyle.Pattern = LinePattern.DenselyDashed;
            winRateLine.Axes.YAxis = rightAxis;
            winRateLine.Color = Colors.Green;

            plt.Legend.IsVisible = true;
            plt.Legend.Alignment = Alignment.UpperLeft;
            plt.SavePng(Path.Combine(_plotDirectory, "1_信号表现概览图.png"), 2880, 1720);
        }

        /// <summary>
        /// 绘制【信号收益分布与月度趋势图】
        /// </summary>
        private void CreateDistributionTimelinePlot(SignalReport returnsResult, IReadOnlyList<Result.SignalInfo> signals)
        {
            int timelinePlotDay = returnsResult.AvgReturns.ToList().IndexOf(returnsResult.AvgReturns.Max()) + 1;
            if (timelinePlotDay > _backtestDays) return;

            var plt = new Plot();
            plt.ScaleFactor = 2;
            plt.Font.Set("Microsoft YaHei UI");
            plt.Title($"信号在 T+{timelinePlotDay} 的收益分布与月度趋势 (基于 {returnsResult.Returns.Count} 个信号)");
            plt.Axes.DateTimeTicksBottom();
            plt.XLabel("信号日期");

            // 准备散点图数据
            var scatterData = new List<(DateTime Date, double Return)>();
            for (int i = 0; i < returnsResult.Returns.Count; i++)
            {
                scatterData.Add((signals[i].Date, returnsResult.Returns[i][timelinePlotDay - 1]));
            }

            // 左Y轴 - 散点图
            var leftAxis = plt.Axes.Left;
            leftAxis.Label.Text = $"单次信号收益率 (T+{timelinePlotDay})";
            leftAxis.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic() { LabelFormatter = y => $"{y:P1}" };
            //leftAxis.Label.ForeColor = Color.FromHex("#aed6f1");
            //leftAxis.TickLabelStyle.ForeColor = Color.FromHex("#aed6f1");

            // 步骤3: 绘制散点图 (分组着色)
            var positiveReturns = scatterData.Where(d => d.Return > 0).ToList();
            var negativeReturns = scatterData.Where(d => d.Return <= 0).ToList();

            if (positiveReturns.Count != 0)
            {
                var scatterPos = plt.Add.Scatter(
                    positiveReturns.Select(p => p.Date.ToOADate()).ToArray(),
                    positiveReturns.Select(p => p.Return).ToArray());
                scatterPos.Color = Color.FromHex("#2ecc71").WithAlpha(0.5);
                scatterPos.LegendText = "盈利信号 (左轴)";
                scatterPos.MarkerStyle.Size = 5;
                scatterPos.LineStyle.Width = 0;
            }
            if (negativeReturns.Count != 0)
            {
                var scatterNeg = plt.Add.Scatter(
                    negativeReturns.Select(p => p.Date.ToOADate()).ToArray(),
                    negativeReturns.Select(p => p.Return).ToArray());
                scatterNeg.Color = Color.FromHex("#e74c3c").WithAlpha(0.5);
                scatterNeg.LegendText = "亏损信号 (左轴)";
                scatterNeg.MarkerStyle.Size = 5;
                scatterNeg.LineStyle.Width = 0;
            }

            plt.Add.HorizontalLine(0, 1, Colors.Gray, LinePattern.DenselyDashed);

            // 右Y轴 - 月度统计
            var rightAxis = plt.Axes.AddRightAxis();
            rightAxis.LabelText = "月度统计收益率";
            //rightAxis.LabelFontColor = Color.FromHex("#f5cba7");
            //rightAxis.TickLabelStyle.ForeColor = Color.FromHex("#f5cba7");
            rightAxis.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic() { LabelFormatter = y => $"{y:P1}" };

            var monthlyStats = scatterData
                .GroupBy(d => new DateTime(d.Date.Year, d.Date.Month, 1))
                .Select(g => new { Month = g.Key, Avg = g.Average(x => x.Return), Median = g.Select(x => x.Return).Median() })
                .OrderBy(x => x.Month).ToList();

            if (monthlyStats.Count != 0)
            {
                var monthDates = monthlyStats.Select(m => m.Month.ToOADate()).ToArray();
                var monthAvgs = monthlyStats.Select(m => m.Avg).ToArray();
                var monthMedians = monthlyStats.Select(m => m.Median).ToArray();

                var monthAvgLine = plt.Add.Scatter(monthDates, monthAvgs);
                monthAvgLine.LegendText = "月度平均收益 (右轴)";
                monthAvgLine.Axes.YAxis = rightAxis; // 关联到右轴
                monthAvgLine.Color = Color.FromHex("#f1c40f");
                monthAvgLine.LineStyle.Width = 2f;
                monthAvgLine.MarkerStyle.Shape = MarkerShape.OpenCircle; // 对应 'o'
                monthAvgLine.MarkerStyle.Size = 5;

                var monthMedianLine = plt.Add.Scatter(monthDates, monthMedians);
                monthMedianLine.LegendText = "月度中位数收益 (右轴)";
                monthMedianLine.Axes.YAxis = rightAxis; // 关联到右轴
                monthMedianLine.Color = Color.FromHex("#e67e22");
                monthMedianLine.LineStyle.Width = 2;
                monthMedianLine.LineStyle.Pattern = LinePattern.DenselyDashed; // 对应 '--'
                monthMedianLine.MarkerStyle.Shape = MarkerShape.Cross; // 对应 'x'
                monthMedianLine.MarkerStyle.Size = 5;
            }

            plt.Legend.IsVisible = true;
            plt.Legend.Alignment = Alignment.UpperLeft;
            plt.SavePng(Path.Combine(_plotDirectory, "2_信号收益分布与月度趋势图.png"), 2880, 1720);
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
            plt.ScaleFactor = 2;
            plt.Font.Set("Microsoft YaHei UI");

            plt.Title($"信号后 T+1 至 T+{_backtestDays} 日收益率分布热力图 (基于 {returnsResult.Returns.Count} 个信号)");
            plt.XLabel("持有天数");
            plt.YLabel("收益率区间");

            var heatmap = plt.Add.Heatmap(heatmapData);
            var coolwarm = new Coolwarm();
            double vmin = 0;
            double vmax = 10;
            double center = 3;
            heatmap.Colormap = new CenteredColormap(coolwarm, center, vmin, vmax);
            heatmap.ManualRange = new ScottPlot.Range(vmin, vmax);
            var colorbar = plt.Add.ColorBar(heatmap);
            colorbar.Label = "信号数量占比 (%)";
            colorbar.LabelStyle.FontName = "Microsoft YaHei UI";

            // 设置坐标轴刻度标签
            Tick[] xTicks = Enumerable.Range(0, _backtestDays).Select((day, i) => new Tick(i, $"T+{day + 1}")).ToArray();
            plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(xTicks);
            //plt.Axes.Bottom.TickLabelStyle.Rotation = 45;

            // Y轴: 收益率区间
            Tick[] yTicks = labels.Select((label, i) => new Tick(i, label)).ToArray();
            plt.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(yTicks);

            // 加入百分比
            for (int y = 0; y < heatmapData.GetLength(0); y++)
            {
                for (int x = 0; x < heatmapData.GetLength(1); x++)
                {
                    var value = heatmapData[y, x];
                    var txt = plt.Add.Text(value.ToString("F1"), x, y);
                    txt.Alignment = Alignment.MiddleCenter;
                    txt.LabelFontSize = 18;
                    txt.LabelFontColor = Colors.White;
                }
            }
            // 添加单元格边框
            // 计算网格线应该在的位置（在单元格之间，即 -0.5, 0.5, 1.5, ...）
            double[] xGridPositions = Enumerable.Range(0, _backtestDays + 1).Select(i => (double)i - 0.5).ToArray();
            double[] yGridPositions = Enumerable.Range(0, labels.Length + 1).Select(i => (double)i - 0.5).ToArray();

            // 循环添加垂直线和水平线来创建网格
            // Loop through and add VerticalLines and HorizontalLines to create the grid
            foreach (double xPos in xGridPositions)
            {
                var vl = plt.Add.VerticalLine(xPos);
                vl.LineStyle.Width = 0.5f;
                vl.LineStyle.Color = Colors.White.WithAlpha(0.5);
            }

            foreach (double yPos in yGridPositions)
            {
                var hl = plt.Add.HorizontalLine(yPos);
                hl.LineStyle.Width = 0.5f;
                hl.LineStyle.Color = Colors.White.WithAlpha(0.5);
            }

            plt.Axes.Margins(0, 0);
            plt.Axes.Frame(false);
            plt.SavePng(Path.Combine(_plotDirectory, "3_信号收益率分布热力图.png"), 2880, 1720);
        }

        // 热力图的辅助方法
        (double[] bins, string[] labels) CreateSmartBins(double binStepPercent, double coarseThresholdPercent)
        {
            double step = binStepPercent / 100.0;
            double threshold = coarseThresholdPercent / 100.0;
            List<double> fineBins = new();
            for (double i = -threshold; i <= threshold; i += step)
            {
                fineBins.Add(i);
            }

            var bins = new List<double> { double.NegativeInfinity };
            bins.AddRange(fineBins);
            bins.Add(double.PositiveInfinity);
            bins = bins.Distinct().ToList(); // 确保唯一性

            var labels = new List<string>();
            for (int i = 0; i < bins.Count - 1; i++)
            {
                double lower = bins[i];
                double upper = bins[i + 1];
                if (double.IsNegativeInfinity(lower))
                    labels.Add($"<{upper:P0}");
                else if (double.IsPositiveInfinity(upper))
                    labels.Add($">{lower:P0}");
                else if (upper > 0)
                    labels.Add($"+{upper:P0}");
                else
                    labels.Add($"{lower:P0}");
            }
            return (bins.ToArray(), labels.ToArray());
        }

        int[] BinData(IEnumerable<double> data, double[] bins)
        {
            int[] counts = new int[bins.Length - 1];
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
