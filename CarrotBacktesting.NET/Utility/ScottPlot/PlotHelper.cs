using System;
using ScottPlot;

namespace CarrotBacktesting.NET.Utility.ScottPlot
{

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
        double[] y1 = x1.Select(x => 2.0 * x + 1.0 + (rand.NextDouble() - 0.5) * 48.0).ToArray();
        double[] y2 = x1.Select(x => x * x / 150.0 + 20.0 + (rand.NextDouble() - 0.5) * 120.0).ToArray();
        double[] y3 = x1.Select(x => 15.0 + (rand.NextDouble() - 0.5) * 300.0).ToArray();
        Plot plot = new Plot();
        PlotHelper.SetStyle(plot, "标题", "X轴", "Y轴");
        PlotHelper.Scatter(plot, x1, y1, legend: "数据1", color: "#003366");
        PlotHelper.Scatter(plot, x1, y2, legend: "数据2", color: "#D23104");
        PlotHelper.Scatter(plot, x1, y3, legend: "数据3", color: "#1cbb5e");
        plot.SavePng("out.png", 2880, 1720);
        return;

        */

        public static void SetStyle(Plot plot, string? title = null, string? xLabel = null, string? yLabel = null)
        {
            plot.ScaleFactor = 2;
            plot.Font.Set("Microsoft YaHei UI");
            if (title != null)
                plot.Title(title);
            if (xLabel != null)
                plot.XLabel(xLabel);
            if (yLabel != null)
                plot.YLabel(yLabel);

            plot.Legend.IsVisible = true;
            plot.Legend.Alignment = Alignment.UpperLeft;
        }

        /// <summary>
        /// 向 Plot 对象中添加热图。
        /// </summary>
        /// <param name="plot">要向其添加热图的 Plot 对象。</param>
        /// <param name="data">一个二维数组，包含了每个单元格的数值。数组的 [0,0] 索引对应于热图的左上角。</param>
        /// <param name="xLabels">（可选）用于X轴刻度的字符串标签数组。如果提供，其长度必须与 <paramref name="data"/> 的第二维长度匹配。</param>
        /// <param name="yLabels">（可选）用于Y轴刻度的字符串标签数组。如果提供，其长度必须与 <paramref name="data"/> 的第一维长度匹配。</param>
        /// <param name="cLabel">（可选）显示在颜色条旁边的标签文本。</param>
        /// <param name="v">（可选）一个元组 (min, mid, max)，用于定义颜色映射的范围和中心点。
        /// <list type="bullet">
        /// <item><description><c>min</c>: 对应颜色图起始端的数值。</description></item>
        /// <item><description><c>mid</c>: 对应颜色图中心点的数值。</description></item>
        /// <item><description><c>max</c>: 对应颜色图结束端的数值。</description></item>
        /// </list>
        /// 如果为 null，将根据数据自动计算范围。
        /// </param>
        /// <param name="annoFormat">（可选）用于在每个单元格上显示数值的格式化字符串。例如, "F1" 表示一位小数。如果为 null，则不显示注解。</param>
        /// <param name="lineWidth">（可选）单元格之间分割线的宽度（以像素为单位）。</param>
        public static void Heatmap(Plot plot, double[,] data, string[]? xLabels = null, string[]? yLabels = null, string? cLabel = null, (double min, double mid, double max)? v = null, string? annoFormat = null, float lineWidth = 1.0f)
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
        }

        public static global::ScottPlot.Plottables.Scatter Scatter(Plot plot, double[] xdata, double[] ydata, string? legend = null, string? color = null, double alpha = 0.5, float markerSize = 5.0f, MarkerShape markerShape = MarkerShape.FilledCircle)
        {
            var scatter = plot.Add.Scatter(xdata, ydata);
            if(color != null)
                scatter.Color = Color.FromHex(color).WithAlpha(alpha);
            scatter.LegendText = legend ?? string.Empty;
            scatter.MarkerStyle.Size = markerSize;
            scatter.LineStyle.Width = 0;

            return scatter;
        }
    }
}
