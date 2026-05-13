using System;
using ScottPlot;
using ScottPlot.Plottables;

namespace CarrotBacktesting.NET.Utility.ScottPlot
{
    /// <summary>
    /// 用于 ScottPlot 图表的创建的静态辅助方法。
    /// </summary>
    public static class PlotHelper
    {
        /*
            // heatmap
            double[,] data = new double[,]
            {
                { 9.5,   2.1,   5.8,   8.0 },
                { 8.8,   3.3,   6.5,   7.5 },
                { 7.1,   4.0,   7.2,   5.1 },
                { 5.0,   5.5,   4.9,   3.0 },
                { 2.0,   1.5,   3.1,   0.5 }
            };
    
            Plot plot = new Plot();
            PlotHelper.SetStyle(plot, "标题", "X轴", "Y轴");
            PlotHelper.Heatmap(plot, data, annoFormat:"F1", cLabel: "百分比");
            plot.SavePng("out.png", 2880, 1720);
            return;


            // scatter
            Random rand = new Random(123);
            double[] x1 = Enumerable.Range(1, 500).Select(i => (double)i).ToArray();
            double[] y1 = x1.Select(x => 2.0 * x + 1.0 + (rand.NextDouble() - 0.5) * 96.0).ToArray();
            double[] x2 = Enumerable.Range(1, 200).Select(i => (double)i * 2.5).ToArray();
            double[] y2 = x2.Select(x => x * x / 240.0 + 45.0 + (rand.NextDouble() - 0.5) * 120.0).ToArray();
            double[] x3 = Enumerable.Range(100, 300).Select(i => (double)i).ToArray();
            double[] y3 = x3.Select(x => -15.0 * x - 1500.0 + (rand.NextDouble() - 0.5) * 300.0).ToArray();
            Plot plot = new Plot();
            PlotHelper.SetStyle(plot, "标题", "X轴", "Y轴", "数据3.Y轴");
            PlotHelper.Scatter(plot, x1, y1, legend: "数据1", color: "#003366");
            PlotHelper.Scatter(plot, x2, y2, legend: "数据2", color: "#D23104", markerSize: 10, lineWidth: 2, markerShape: MarkerShape.Eks);
            PlotHelper.Scatter(plot, x3, y3, legend: "数据3", color: "#1cbb5e", yAxis: Edge.Right);
            plot.SavePng("out.png", 2880, 1720);
            return;
        
            // scatter datetime
            Random rand = new Random(123);
            double[] x1 = Enumerable.Range(1, 500).Select(i => (double)i).ToArray();
            DateTime[] d1 = x1.Select(x => new DateTime(2025, 11, 14, 0, 0, 0).AddDays(x)).ToArray();
            double[] y1 = x1.Select(x => 2.0 * x + 1.0 + (rand.NextDouble() - 0.5) * 96.0).ToArray();
            Plot plot = new Plot();
            PlotHelper.SetStyle(plot, "标题", "X轴时间", "Y轴");
            PlotHelper.Scatter(plot, d1, y1, legend: "数据1", color: "#003366");
            plot.SavePng("out.png", 2880, 1720);
            return;
        
            // hist
            Random rand = new Random(123);

            int count = 300;
            double mean = 100.0;
            double stdDev = 5.0;
            double[] data = Enumerable.Range(0, count).Select(_ => mean + stdDev * Math.Sqrt(-2.0 * Math.Log(1.0 - rand.NextDouble())) * Math.Sin(2.0 * Math.PI * (1.0 - rand.NextDouble()))).ToArray();
            
            Plot plot = new Plot();
            PlotHelper.SetStyle(plot, "标题", "X轴", "分布");
            PlotHelper.Hist(plot, data);
            plot.SavePng("out.png", 2880, 1720);
            return;

        */

        /// <summary>
        /// 为 Plot 对象应用一套统一的视觉样式。
        /// </summary>
        /// <param name="plot">要应用样式的 Plot 对象。</param>
        /// <param name="title">（可选）图表的总标题。</param>
        /// <param name="xLabel">（可选）X轴（底部）的标签文本。</param>
        /// <param name="yLabel">（可选）Y轴（左侧）的标签文本。</param>
        /// <param name="rightLabel">（可选）右侧Y轴的标签文本。</param>
        public static void SetStyle(Plot plot, string? title = null, string? xLabel = null, string? yLabel = null, string? rightLabel = null, string? yTickFormat = null, string? rightTickFormat = null)
        {
            plot.ScaleFactor = 2;
            plot.Font.Set("Microsoft YaHei UI");
            if (title != null)
                plot.Title(title);
            if (xLabel != null)
                plot.Axes.Bottom.Label.Text = xLabel;
            if (yLabel != null)
                plot.Axes.Left.Label.Text = yLabel;
            if (rightLabel != null)
                plot.Axes.Right.Label.Text = rightLabel;
            if (yTickFormat != null)
                plot.Axes.Left.TickGenerator = new global::ScottPlot.TickGenerators.NumericAutomatic() { LabelFormatter = y => y.ToString(yTickFormat) };
            if (rightTickFormat != null)
                plot.Axes.Right.TickGenerator = new global::ScottPlot.TickGenerators.NumericAutomatic() { LabelFormatter = y => y.ToString(rightTickFormat) };

            plot.Legend.IsVisible = true;
            plot.Legend.Alignment = Alignment.UpperLeft;
        }

        /// <summary>
        /// 向 Plot 对象中添加一个样式统一的热力图。
        /// </summary>
        /// <param name="plot">要向其添加热力图的 Plot 对象。</param>
        /// <param name="data">一个二维数组，包含了每个单元格的数值。数组的 [0,0] 索引对应于热力图的左上角。</param>
        /// <param name="xLabels">（可选）用于X轴刻度的字符串标签数组。如果提供，其长度必须与 <paramref name="data"/> 的第二维长度匹配。</param>
        /// <param name="yLabels">（可选）用于Y轴刻度的字符串标签数组。如果提供，其长度必须与 <paramref name="data"/> 的第一维长度匹配，顺序为从上到下。</param>
        /// <param name="cLabel">（可选）显示在颜色条旁边的标签文本。</param>
        /// <param name="v">（可选）一个元组 (min, mid, max)，用于定义颜色映射的范围和中心点。</param>
        /// <param name="annoFormat">（可选）用于在每个单元格上显示数值的格式化字符串。例如, "F1" 表示一位小数。如果为 null，则不显示注解。</param>
        /// <param name="lineWidth">（可选）单元格之间分割线的宽度（以像素为单位）。</param>
        /// <returns>创建的 Heatmap 对象，以便进行进一步的自定义。</returns>
        public static Heatmap Heatmap(Plot plot, double[,] data, string[]? xLabels = null, string[]? yLabels = null, string? cLabel = null, (double min, double mid, double max)? v = null, string? annoFormat = null, float lineWidth = 1.0f)
        {
            var heatmap = plot.Add.Heatmap(data);
            var coolwarm = new Coolwarm();

            if (!v.HasValue)
            {
                var flattenData = data.Cast<double>();
                v = (flattenData.Min(), (flattenData.Max() + flattenData.Min()) / 2, flattenData.Max());
            }

            heatmap.Colormap = new CenteredColormap(coolwarm, v.Value.mid, v.Value.min, v.Value.max);
            heatmap.ManualRange = new global::ScottPlot.Range(v.Value.min, v.Value.max);
            var colorbar = plot.Add.ColorBar(heatmap);
            colorbar.Label = cLabel ?? string.Empty;
            // colorbar.LabelStyle.FontName = "Microsoft YaHei UI";

            // 设置坐标轴刻度标签
            Tick[] xTicks = Enumerable.Range(0, data.GetLength(1)).Select(i => (xLabels != null) ? new Tick(i, xLabels[i]) : new Tick(i, i.ToString())).ToArray();
            plot.Axes.Bottom.TickGenerator = new global::ScottPlot.TickGenerators.NumericManual(xTicks);
            //plt.Axes.Bottom.TickLabelStyle.Rotation = 45;

            Tick[] yTicks = Enumerable.Range(0, data.GetLength(0)).Select(i => (yLabels != null) ? new Tick(i, yLabels[i]) : new Tick(i, i.ToString())).ToArray();
            plot.Axes.Left.TickGenerator = new global::ScottPlot.TickGenerators.NumericManual(yTicks);

            // Annotations
            if (annoFormat != null)
            {
                for (int y = 0; y < data.GetLength(0); y++)
                {
                    for (int x = 0; x < data.GetLength(1); x++)
                    {
                        var value = data[y, x];
                        var txt = plot.Add.Text(value.ToString(annoFormat), x, y);
                        txt.Alignment = Alignment.MiddleCenter;
                        txt.LabelFontSize = 18;
                        txt.LabelFontColor = Colors.White;
                    }
                }
            }

            // 添加单元格边框
            double[] xGridPositions = Enumerable.Range(0, data.GetLength(1) + 1).Select(i => (double)i - 0.5).ToArray();
            double[] yGridPositions = Enumerable.Range(0, data.GetLength(0) + 1).Select(i => (double)i - 0.5).ToArray();
            foreach (double xPos in xGridPositions)
            {
                var vl = plot.Add.VerticalLine(xPos);
                vl.LineStyle.Width = lineWidth;
                vl.LineStyle.Color = Colors.White.WithAlpha(0.5);
            }
            foreach (double yPos in yGridPositions)
            {
                var hl = plot.Add.HorizontalLine(yPos);
                hl.LineStyle.Width = lineWidth;
                hl.LineStyle.Color = Colors.White.WithAlpha(0.5);
            }

            //plot.Axes.Margins(0, 0);
            //设置坐标原点在左上方, 边缘不留空
            plot.Axes.SetLimits(
                left: -0.5,
                right: data.GetLength(1) - 0.5,
                bottom: data.GetLength(0) - 0.5,
                top: -0.5
            );
            plot.Axes.Frame(false);

            return heatmap;
        }

        /// <summary>
        /// 向 Plot 对象中添加一个散点图或折线图。
        /// </summary>
        /// <param name="plot">要向其添加图表的 Plot 对象。</param>
        /// <param name="xdata">X轴的数据数组。</param>
        /// <param name="ydata">Y轴的数据数组。</param>
        /// <param name="legend">（可选）此数据系列在图例中显示的名称。</param>
        /// <param name="color">（可选）颜色，格式为十六进制字符串 (如 "#FF0000")。</param>
        /// <param name="alpha">（可选）透明度，范围从 0.0 (完全透明) 到 1.0 (完全不透明)。</param>
        /// <param name="markerSize">（可选）数据点标记的大小。设置为 0 可隐藏标记。</param>
        /// <param name="markerShape">（可选）数据点标记的形状。</param>
        /// <param name="lineWidth">（可选）连接数据点的线条宽度。设置为 0 可绘制纯散点图。</param>
        /// <param name="linePattern">（可选）线条的样式（如虚线、点线等）。</param>
        /// <param name="yAxis">（可选）指定此数据系列关联的Y轴（左轴或右轴）。</param>
        /// <returns>创建的 Scatter 对象，以便进行进一步的自定义。</returns>
        public static Scatter Scatter(Plot plot, double[] xdata, double[] ydata, string? legend = null, string? color = null, double? alpha = null, float markerSize = 5.0f, MarkerShape markerShape = MarkerShape.FilledCircle, float lineWidth = 0.0f, LinePattern? linePattern = null, Edge yAxis = Edge.Left)
        {
            var scatter = plot.Add.Scatter(xdata, ydata);
            if (color != null)
                scatter.Color = Color.FromHex(color);
            if (alpha != null)
                scatter.Color = scatter.Color.WithAlpha(alpha.Value);
            scatter.LegendText = legend ?? string.Empty;
            scatter.MarkerStyle.Size = markerSize;
            scatter.MarkerStyle.Shape = markerShape;
            scatter.LineStyle.Width = lineWidth;
            scatter.LineStyle.Pattern = linePattern ?? LinePattern.Solid;

            if (yAxis == Edge.Right)
                scatter.Axes.YAxis = plot.Axes.Right;

            return scatter;
        }

        /// <summary>
        /// 向 Plot 对象中添加一个使用日期时间作为X轴的散点图或折线图。
        /// </summary>
        /// <inheritdoc cref="Scatter(Plot, double[], double[], string, string, double?, float, MarkerShape, float, LinePattern?, Edge)"/>
        public static Scatter Scatter(Plot plot, DateTime[] xdate, double[] ydata, string? legend = null, string? color = null, double? alpha = null, float markerSize = 5.0f, MarkerShape markerShape = MarkerShape.FilledCircle, float lineWidth = 0.0f, LinePattern? linePattern = null, Edge yAxis = Edge.Left)
        {
            var scatter = Scatter(plot, xdate.Select(d => d.ToOADate()).ToArray(), ydata, legend, color, alpha, markerSize, markerShape, lineWidth, linePattern, yAxis);
            plot.Axes.DateTimeTicksBottom();
            return scatter;
        }

        /// <summary>
        /// 向 Plot 对象中添加一条折线图 (无数据点标记)。
        /// </summary>
        /// <inheritdoc cref="Scatter(Plot, double[], double[], string, string, double?, float, MarkerShape, float, LinePattern?, Edge)"/>
        public static Scatter Line(Plot plot, double[] xdata, double[] ydata, string? legend = null, string? color = null, double? alpha = null, float markerSize = 0.0f, MarkerShape markerShape = MarkerShape.FilledCircle, float lineWidth = 1.0f, LinePattern? linePattern = null, Edge yAxis = Edge.Left)
        => Scatter(plot, xdata, ydata, legend, color, alpha, markerSize, markerShape, lineWidth, linePattern, yAxis);

        /// <summary>
        /// 向 Plot 对象中添加一条使用日期时间作为X轴的折线图 (无数据点标记)。
        /// </summary>
        /// <inheritdoc cref="Scatter(Plot, DateTime[], double[], string, string, double?, float, MarkerShape, float, LinePattern?, Edge)"/>
        public static Scatter Line(Plot plot, DateTime[] xdate, double[] ydata, string? legend = null, string? color = null, double? alpha = null, float markerSize = 0.0f, MarkerShape markerShape = MarkerShape.FilledCircle, float lineWidth = 1.0f, LinePattern? linePattern = null, Edge yAxis = Edge.Left)
        => Scatter(plot, xdate, ydata, legend, color, alpha, markerSize, markerShape, lineWidth, linePattern, yAxis);

        /// <summary>
        /// 向 Plot 对象中添加一条折线图 (无数据点标记)。
        /// </summary>
        /// <inheritdoc cref="Scatter(Plot, double[], double[], string, string, double?, float, MarkerShape, float, LinePattern?, Edge)"/>
        public static Scatter ScatterLine(Plot plot, double[] xdata, double[] ydata, string? legend = null, string? color = null, double? alpha = null, float markerSize = 5.0f, MarkerShape markerShape = MarkerShape.FilledCircle, float lineWidth = 1.0f, LinePattern? linePattern = null, Edge yAxis = Edge.Left)
        => Scatter(plot, xdata, ydata, legend, color, alpha, markerSize, markerShape, lineWidth, linePattern, yAxis);

        /// <summary>
        /// 向 Plot 对象中添加一条使用日期时间作为X轴的折线图 (无数据点标记)。
        /// </summary>
        /// <inheritdoc cref="Scatter(Plot, DateTime[], double[], string, string, double?, float, MarkerShape, float, LinePattern?, Edge)"/>
        public static Scatter ScatterLine(Plot plot, DateTime[] xdate, double[] ydata, string? legend = null, string? color = null, double? alpha = null, float markerSize = 5.0f, MarkerShape markerShape = MarkerShape.FilledCircle, float lineWidth = 1.0f, LinePattern? linePattern = null, Edge yAxis = Edge.Left)
        => Scatter(plot, xdate, ydata, legend, color, alpha, markerSize, markerShape, lineWidth, linePattern, yAxis);

        /// <summary>
        /// 向 Plot 对象中添加一个直方图。
        /// </summary>
        /// <param name="plot">要向其添加图表的 Plot 对象。</param>
        /// <param name="data">用于生成直方图的一维原始数据数组。</param>
        /// <param name="color">（可选）条形的颜色，格式为十六进制字符串。</param>
        /// <param name="alpha">（可选）条形的透明度。</param>
        /// <param name="barWidth">（可选）条形宽度的比例，1.0 表示条形之间没有间隙。</param>
        /// <param name="binCount">（可选）指定直方图的条柱数量。</param>
        /// <param name="binSize">（可选）指定每个条柱的宽度。</param>
        /// <returns>创建的 HistogramBars 对象，以便进行进一步的自定义。</returns>
        public static HistogramBars Hist(Plot plot, double[] data, string? color = null, double? alpha = null, double barWidth = 1.0f, int? binCount = null, double? binSize = null)
        {
            global::ScottPlot.Statistics.Histogram? histData = null;
            if (binCount != null)
                histData = global::ScottPlot.Statistics.Histogram.WithBinSize(2, data);
            if (binSize != null)
                histData = global::ScottPlot.Statistics.Histogram.WithBinCount(50, data);
            if (histData == null)
                histData = global::ScottPlot.Statistics.Histogram.WithBinCount(HistogramHelper.CalculateFDBins(data), data);
            HistogramBars histPlot = plot.Add.Histogram(histData);
            Bar[] bars = histPlot.Bars;
            for (int i = 0; i < bars.Length; i++)
            {
                if (color != null)
                    bars[i].FillColor = Color.FromHex(color);
                if (alpha != null)
                    bars[i].FillColor = bars[i].FillColor.WithAlpha(alpha.Value);
            }
            histPlot.BarWidthFraction = barWidth;
            return histPlot;
        }
    }
}
