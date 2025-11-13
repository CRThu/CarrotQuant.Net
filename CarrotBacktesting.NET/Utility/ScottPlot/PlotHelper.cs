using System;
using ScottPlot;

namespace CarrotBacktesting.NET.Utility.ScottPlot
{

    public static class PlotHelper
    {
        /*
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
        }

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
    }

}
