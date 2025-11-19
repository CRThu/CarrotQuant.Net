using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Utility;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotBacktesting.NET.Analysis.Exporters
{
    public class ConsoleExporter : IExporter
    {
        public string Name => nameof(ConsoleExporter);

        /// <summary>
        /// 导出器的入口方法
        /// </summary>
        public void Export(AnalysisContext context)
        {
            string outputDir = context.Config.ResolvePath(context.Config.Out.Exporter);
            string htmlPath = Path.Combine(outputDir, "summary.html");
            Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);

            var recorder = new Recorder(AnsiConsole.Console);

            var signalReport = context.GetArtifact<SignalReport[]>();
            if (signalReport != null && signalReport.Length > 0)
            {
                if (signalReport[0].Global.SignalCount == 0)
                    AnsiConsole.MarkupLine("[yellow1][ConsoleExporter] 没有有效的信号报告可供输出。[/]");
                else
                    recorder.Write(new Rule("[bold blue]T+N 信号表现分析[/]").Centered());

                RenderSignalReport(recorder, signalReport, context);
            }

            var tradeReport = context.GetArtifact<TradeReport>();
            if (tradeReport != null)
            {
                RenderTradeReport(recorder, tradeReport);
            }

            try
            {
                const string darkThemeCss = @"
<style>
body { background-color: #1e1e1e; color: #d4d4d4; font-family: Consolas, monospace; }
</style>";
                string finalHtml = darkThemeCss + recorder.ExportHtml();

                File.WriteAllText(htmlPath, finalHtml);
                AnsiConsole.MarkupLine($"[bold springgreen3]分析报告已成功保存到: [link]{Path.GetFullPath(htmlPath)}[/][/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold indianred]错误: 保存文本报告失败: {ex.Message}[/]");
            }
        }

        /// <summary>
        /// 专门渲染T+N信号表现报告
        /// </summary>
        private void RenderSignalReport(IAnsiConsole console, SignalReport[] report, AnalysisContext context)
        {
            console.WriteLine();

            PrintPeakAnalysis(console, report);
            PrintDailySummary(console, report);
            PrintMonthlyReturns(console, report);
        }

        /// <summary>
        /// 渲染交易分析的两个核心部分：总体摘要和月度统计。
        /// </summary>
        private void RenderTradeReport(IAnsiConsole console, TradeReport report)
        {
            RenderTradeSummaryReport(console, report);
            RenderMonthlyPerformanceReport(console, report);
            RenderExitTimingReport(console, report);
        }

        /// <summary>
        /// 打印最佳持有期分析
        /// </summary>
        private void PrintPeakAnalysis(IAnsiConsole console, SignalReport[] reports)
        {
            if (reports.Length == 0) return;

            // 1. 寻找各项指标的峰值所在的报告对象
            // MaxBy 返回的是整个 SignalReport 对象
            var bestAvgReport = reports.MaxBy(r => r.Global.AvgReturn)!;
            var bestMedianReport = reports.MaxBy(r => r.Global.MedianReturn)!;
            var bestWinRateReport = reports.MaxBy(r => r.Global.WinRate)!;

            // 获取对应的天数 (数组索引 + 1)
            int dayMaxAvg = Array.IndexOf(reports, bestAvgReport) + 1;
            int dayMaxMedian = Array.IndexOf(reports, bestMedianReport) + 1;
            int dayMaxWin = Array.IndexOf(reports, bestWinRateReport) + 1;

            console.Write(new Rule("策略最佳持有期分析 (各指标峰值)"));

            console.MarkupLine($"平均收益率峰值: [springgreen3]{bestAvgReport.Global.AvgReturn:P2}[/] ([yellow1]T+{dayMaxAvg}[/], 当日胜率 [deepskyblue1]{bestAvgReport.Global.WinRate:P2}[/])");
            console.MarkupLine($"收益率中位数峰值: [springgreen3]{bestMedianReport.Global.MedianReturn:P2}[/] ([yellow1]T+{dayMaxMedian}[/], 当日胜率 [deepskyblue1]{bestMedianReport.Global.WinRate:P2}[/])");
            console.MarkupLine($"(参考) 胜率峰值: [deepskyblue1]{bestWinRateReport.Global.WinRate:P2}[/] ([yellow1]T+{dayMaxWin}[/])");
            console.WriteLine();

            // 使用平均收益率最高的那个周期的详细数据
            var bestGlobal = bestAvgReport.Global;
            console.Write(new Rule($"最佳持有期 (T+{dayMaxAvg}) 详细指标:"));
            console.MarkupLine($"平均盈利:[springgreen3] {bestGlobal.AvgWin:P2}[/]");
            console.MarkupLine($"平均亏损:[indianred] {bestGlobal.AvgLoss:P2}[/]");
            console.MarkupLine($"盈亏比:[deepskyblue1] {bestGlobal.WinLossRatio:F2}[/]");
            console.WriteLine();
        }

        /// <summary>
        /// 打印每日表现统计
        /// </summary>
        private void PrintDailySummary(IAnsiConsole console, SignalReport[] reports)
        {
            if (reports.Length == 0) return;

            console.Write(new Rule());

            int validCount = reports[0].ValidSignalCount;

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"策略在 T+1 至 T+{reports.Length} 期间的每日表现统计")
                .Caption($"基于 {validCount} 个有效信号")
                .AddColumn("持有天数")
                .AddColumn(new TableColumn("平均收益率").RightAligned())
                .AddColumn(new TableColumn("收益率中位数").RightAligned())
                .AddColumn(new TableColumn("胜率").RightAligned())
                //.AddColumn(new TableColumn("平均盈利").RightAligned())
                //.AddColumn(new TableColumn("平均亏损").RightAligned())
                .AddColumn(new TableColumn("盈亏比").RightAligned());

            for (int i = 0; i < reports.Length; i++)
            {
                var r = reports[i];
                var g = r.Global;

                string avgReturnColor = g.AvgReturn > 0 ? "springgreen3" : "indianred";
                string medianReturnColor = g.MedianReturn > 0 ? "springgreen3" : "indianred";

                table.AddRow(
                    $"T+{i + 1}",
                    $"[{avgReturnColor}]{g.AvgReturn:P2}[/]",
                    $"[{medianReturnColor}]{g.MedianReturn:P2}[/]",
                    $"[deepskyblue1]{g.WinRate:P2}[/]",
                    //$"[springgreen3]{g.AvgWin:P2}[/]",
                    //$"[indianred]{g.AvgLoss:P2}[/]",
                    $"[deepskyblue1]{g.WinLossRatio:F2}[/]"
                );
            }
            console.Write(table);
        }

        /// <summary>
        /// 打印 T+X 月度收益统计
        /// </summary>
        private void PrintMonthlyReturns(IAnsiConsole console, SignalReport[] reports)
        {
            if (reports.Length == 0) return;

            // 1. 找到最佳平均收益率的持有天数对应的报告
            var bestReport = reports.MaxBy(r => r.Global.AvgReturn)!;
            int bestDay = Array.IndexOf(reports, bestReport) + 1;

            console.Write(new Rule());
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"策略信号在 T+{bestDay} 的月度表现统计")
                .AddColumn("月份")
                .AddColumn(new TableColumn("信号数").RightAligned())
                .AddColumn(new TableColumn("月度平均收益").RightAligned())
                .AddColumn(new TableColumn("月度中位数收益").RightAligned())
                .AddColumn(new TableColumn("胜率").RightAligned())
                //.AddColumn(new TableColumn("月度平均盈利").RightAligned())
                //.AddColumn(new TableColumn("月度平均亏损").RightAligned())
                .AddColumn(new TableColumn("月度盈亏比").RightAligned());

            // 2. 遍历 Monthly 列表
            // 这里的 item 是 (DateTime Month, SignalPerf Perf)
            foreach (var (month, perf) in bestReport.Monthly)
            {
                string avgReturnColor = perf.AvgReturn > 0 ? "springgreen3" : "indianred";
                string medianReturnColor = perf.MedianReturn > 0 ? "springgreen3" : "indianred";

                table.AddRow(
                    $"{month:yyyy-MM}",
                    $"{perf.SignalCount}",
                    $"[{avgReturnColor}]{perf.AvgReturn:P2}[/]",
                    $"[{medianReturnColor}]{perf.MedianReturn:P2}[/]",
                    $"[deepskyblue1]{perf.WinRate:P2}[/]",
                    //$"[springgreen3]{perf.AvgWin:P2}[/]",
                    //$"[indianred]{perf.AvgLoss:P2}[/]",
                    $"[deepskyblue1]{perf.WinLossRatio:F2}[/]"
                );
            }
            console.Write(table);
        }


        /// <summary>
        /// 渲染交易统计报告
        /// </summary>
        private void RenderTradeSummaryReport(IAnsiConsole console, TradeReport report)
        {
            console.Write(new Rule("核心交易性能指标"));

            var grid = new Grid()
                .AddColumn(new GridColumn().RightAligned().PadRight(1)) // 键列：右对齐，右边距1
                .AddColumn(); // 值列

            // 添加所有核心指标行
            grid.AddRow("[bold]总交易次数:[/]", $"[cyan]{report.TotalTrades}[/]");
            grid.AddRow("[bold]胜率:[/]", $"[deepskyblue1]{report.WinRate:P2}[/]");
            grid.AddRow("[bold]平均收益率:[/]", $"[{(report.AverageReturn >= 0 ? "springgreen3" : "indianred")}]{report.AverageReturn:P2}[/]");
            grid.AddRow("[bold]中位数收益率:[/]", $"[{(report.MedianReturn >= 0 ? "magenta" : "indianred")}]{report.MedianReturn:P2}[/]");
            grid.AddRow("[bold]平均盈利:[/]", $"[springgreen3]{report.AverageWinReturn:P2}[/]");
            grid.AddRow("[bold]平均亏损:[/]", $"[indianred]{report.AverageLossReturn:P2}[/]");
            grid.AddRow("[bold]盈亏比:[/]", $"[yellow1]{report.WinLossRatio:F2}[/]");
            grid.AddRow("[bold]平均持仓周期:[/]", $"[deepskyblue1]{report.AverageHoldingPeriod:F2} 天[/]");
            grid.AddEmptyRow();

            // --- 卖点评估指标 ---

            grid.AddRow("[bold underline]交易效率评估 (Trade Efficiency)[/]", "");
            grid.AddRow("[bold]平均交易效率:[/]", $"[cyan]{report.AverageTradeEfficiency:P2}[/]");
            grid.AddRow("[bold]中位数交易效率:[/]", $"[cyan]{report.MedianTradeEfficiency:P2}[/]");

            console.Write(
                new Panel(grid)
                    .Header("交易统计摘要")
                    .Expand() // Panel 宽度自动扩展以适应内容
            );
            console.WriteLine(); // 添加一个空行
        }

        /// <summary>
        /// 渲染卖出时机/踏空分析报告
        /// </summary>
        private void RenderExitTimingReport(IAnsiConsole console, TradeReport report)
        {
            console.Write(new Rule("卖出时机分析 (平仓后T+N日表现)").Centered());

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Caption($"[grey]基于 {report.ExitValidCount} 个有效平仓点[/]")
                .AddColumn("平仓后天数")
                .AddColumn(new TableColumn("平均后续收益").RightAligned())
                .AddColumn(new TableColumn("中位数后续收益").RightAligned())
                .AddColumn(new TableColumn("后续上涨概率").RightAligned());

            for (int i = 0; i < report.ExitTimingBacktestDays; i++)
            {
                string avgReturnColor = report.ExitTimingAvgReturns[i] > 0 ? "springgreen3" : "indianred";
                string medianReturnColor = report.ExitTimingMedianReturns[i] > 0 ? "springgreen3" : "indianred";

                table.AddRow(
                    $"T+{i + 1}",
                    $"[{avgReturnColor}]{report.ExitTimingAvgReturns[i]:P2}[/]",
                    $"[{medianReturnColor}]{report.ExitTimingMedianReturns[i]:P2}[/]",
                    $"[deepskyblue1]{report.ExitTimingWinRates[i]:P2}[/]"
                );
            }
            console.Write(table);
        }


        /// <summary>
        /// 渲染按月分组的交易表现报告
        /// </summary>
        private void RenderMonthlyPerformanceReport(IAnsiConsole console, TradeReport report)
        {
            if (report.MonthlyStats == null || !report.MonthlyStats.Any()) return;

            console.Write(new Rule("[yellow bold]按月度统计交易表现[/]").Justify(Justify.Center));

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("月份")
                .AddColumn(new TableColumn("交易次数").RightAligned())
                .AddColumn(new TableColumn("月度平均收益").RightAligned())
                .AddColumn(new TableColumn("月度中位数收益").RightAligned())
                .AddColumn(new TableColumn("胜率").RightAligned())
                .AddColumn(new TableColumn("月度平均盈利").RightAligned())
                .AddColumn(new TableColumn("月度平均亏损").RightAligned())
                .AddColumn(new TableColumn("月度盈亏比").RightAligned());

            foreach (var stat in report.MonthlyStats)
            {
                string avgReturnColor = stat.AverageReturn > 0 ? "springgreen3" : "indianred";
                string medianReturnColor = stat.MedianReturn > 0 ? "springgreen3" : "indianred";
                table.AddRow(
                    $"{stat.Month:yyyy-MM}",
                    $"{stat.TradeCount}",
                    $"[{avgReturnColor}]{stat.AverageReturn:P2}[/]",
                    $"[{medianReturnColor}]{stat.MedianReturn:P2}[/]",
                    $"[deepskyblue1]{stat.WinRate:P2}[/]",
                    $"[springgreen3]{stat.AverageWin:P2}[/]",
                    $"[indianred]{stat.AverageLoss:P2}[/]",
                    $"[deepskyblue1]{stat.WinLossRatio:F2}[/]"
                );
            }
            console.Write(table);
        }
    }
}