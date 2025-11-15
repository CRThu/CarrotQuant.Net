using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Result;
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
            // 确保输出目录存在
            _plotDirectory = context.Config.ResolvePath(context.Config.Out.Exporter);
            Directory.CreateDirectory(_plotDirectory);

            Console.WriteLine($"[PlotExporter] 开始生成图表，将保存到: {Path.GetFullPath(_plotDirectory)}");

            // a. 渲染 T+N 信号表现图
            var signalReport = context.GetArtifact<SignalReport>();
            if (signalReport != null)
            {
                _backtestDays = signalReport.BacktestDays;
                var tradesForSignal = context.BacktestResult.Trades; // 获取用于匹配日期的交易列表

                CreatePerformanceOverviewPlot(signalReport);
                CreateDistributionTimelinePlot(signalReport, tradesForSignal);
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
        private void CreatePerformanceOverviewPlot(SignalReport returnsResult)
        {
            string title = $"信号在 T+1 至 T+{_backtestDays} 日的表现 (基于 {returnsResult.Returns.Count} 个信号)";
            string xLabel = "持有天数 (T+N)";
            string yLabel = "收益率";
            string yRightLabel = "胜率";
            var plot = new Plot();
            PlotHelper.SetStyle(plot, title, xLabel, yLabel, yRightLabel);

            double[] days = Enumerable.Range(1, _backtestDays).Select(d => (double)d).ToArray();

            // 左Y轴 - 收益率
            var leftAxis = plot.Axes.Left;
            leftAxis.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic() { LabelFormatter = y => $"{y:P1}" };

            // 右Y轴 - 胜率
            var rightAxis = plot.Axes.Right;
            rightAxis.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic() { LabelFormatter = y => $"{y:P1}" };

            PlotHelper.ScatterLine(plot, days, returnsResult.AvgReturns.ToArray(), "平均收益率");
            PlotHelper.ScatterLine(plot, days, returnsResult.MedianReturns.ToArray(), "中位数收益率");
            PlotHelper.ScatterLine(plot, days, returnsResult.WinRates.ToArray(), "胜率", color: "#008000", linePattern: LinePattern.DenselyDashed, yAxis: Edge.Right);

            plot.Add.HorizontalLine(0, 1, Colors.Gray, LinePattern.Dashed);

            string plotPath = Path.Combine(_plotDirectory, "1_信号表现概览图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plot.SavePng(plotPath, 2880, 1720);
        }

        /// <summary>
        /// 绘制【信号收益分布与月度趋势图】
        /// </summary>
        private void CreateDistributionTimelinePlot(SignalReport returnsResult, List<Trade> trades)
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
                scatterData.Add((trades[i].EntryDate, returnsResult.Returns[i][timelinePlotDay - 1]));
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

            string plotPath = Path.Combine(_plotDirectory, "2_信号收益分布与月度趋势图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plt.SavePng(plotPath, 2880, 1720);
        }

        /// <summary>
        /// 绘制【收益率分布热力图】
        /// </summary>
        private void CreateHeatmapPlot(SignalReport returnsResult)
        {
            // 1. 智能分箱
            var (bins, labels) = HistogramHelper.GetBins(0.02, -0.24, 0.24);

            // 2. 数据处理
            var heatmapData = new double[labels.Length, _backtestDays];
            for (int day = 0; day < _backtestDays; day++)
            {
                var returnsOnDay = returnsResult.Returns.Select(r => r[day]);
                var counts = returnsOnDay.ToHist(bins);
                for (int binIdx = 0; binIdx < counts.Length; binIdx++)
                {
                    heatmapData[binIdx, day] = (double)counts[binIdx] / returnsResult.Returns.Count * 100;
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
                    double plotY = heatmapData.GetLength(0) - 1 - y;
                    var txt = plt.Add.Text(value.ToString("F1"), x, plotY);
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

            string plotPath = Path.Combine(_plotDirectory, "3_信号收益率分布热力图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plt.SavePng(plotPath, 2880, 1720);
        }

        /// <summary>
        /// 绘制按月统计的交易表现图
        /// </summary>
        private void CreateMonthlyTradePerformancePlot(TradeReport report, List<Trade> trades)
        {
            var result = report.MonthlyStats;

            var plt = new Plot();
            plt.ScaleFactor = 2;
            plt.Font.Set("Microsoft YaHei UI");
            plt.Title($"按月统计交易表现 (基于 {trades.Count} 个信号)");
            plt.Axes.DateTimeTicksBottom();
            plt.XLabel("月份");

            // 准备散点图数据
            var scatterData = new List<(DateTime Date, double Return)>();
            for (int i = 0; i < trades.Count; i++)
            {
                scatterData.Add((trades[i].EntryDate, trades[i].Return ?? 0));
            }

            // 左Y轴 - 散点图
            var leftAxis = plt.Axes.Left;
            leftAxis.Label.Text = $"单次信号收益率";
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

            string plotPath = Path.Combine(_plotDirectory, "4_交易月度表现图.png");
            Directory.CreateDirectory(Path.GetDirectoryName(plotPath)!);
            plt.SavePng(plotPath, 2880, 1720);
        }
    }
}
