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

            var signalReport = context.GetArtifact<SignalReport>();
            if (signalReport != null)
            {
                if (signalReport.ValidSignalCount == 0)
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
        private void RenderSignalReport(IAnsiConsole console, SignalReport report, AnalysisContext context)
        {
            console.WriteLine();

            PrintPeakAnalysis(console, report);
            PrintDailySummary(console, report);

            // 月度分析需要原始信号的时间信息
            PrintMonthlyReturns(console, report, context.BacktestResult.Trades);
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
        private void PrintPeakAnalysis(IAnsiConsole console, SignalReport report)
        {
            // 计算平均收益率峰值
            var maxAvgReturn = report.AvgReturns.Max();
            int dayMaxAvg = Array.IndexOf(report.AvgReturns.ToArray(), maxAvgReturn) + 1;
            var winRateAtMaxAvg = report.WinRates[dayMaxAvg - 1];

            // 计算收益率中位数峰值
            var maxMedianReturn = report.MedianReturns.Max();
            int dayMaxMedian = Array.IndexOf(report.MedianReturns.ToArray(), maxMedianReturn) + 1;
            var winRateAtMaxMedian = report.WinRates[dayMaxMedian - 1];

            // 计算胜率峰值
            var maxWinRate = report.WinRates.Max();
            int dayMaxWin = Array.IndexOf(report.WinRates.ToArray(), maxWinRate) + 1;

            console.Write(new Rule("策略最佳持有期分析 (各指标峰值)"));

            console.MarkupLine($"平均收益率峰值: [springgreen3]{maxAvgReturn:P2}[/] ([yellow1]T+{dayMaxAvg}[/], 当日胜率 [deepskyblue1]{winRateAtMaxAvg:P2}[/])");
            console.MarkupLine($"收益率中位数峰值: [springgreen3]{maxMedianReturn:P2}[/] ([yellow1]T+{dayMaxMedian}[/], 当日胜率 [deepskyblue1]{winRateAtMaxMedian:P2}[/])");
            console.MarkupLine($"(参考) 胜率峰值: [deepskyblue1]{maxWinRate:P2}[/] ([yellow1]T+{dayMaxWin}[/])");
            console.WriteLine();

            console.Write(new Rule($"最佳持有期 (T+{dayMaxAvg}) 详细指标:"));
            console.MarkupLine($"平均盈利:[springgreen3] {report.AvgWinReturns[dayMaxAvg - 1]:P2}[/]");
            console.MarkupLine($"平均亏损:[indianred] {report.AvgLossReturns[dayMaxAvg - 1]:P2}[/]");
            console.MarkupLine($"盈亏比:[deepskyblue1] {report.WinLossRatio[dayMaxAvg - 1]:F2}[/]");
            console.WriteLine();
        }

        /// <summary>
        /// 打印每日表现统计表格
        /// </summary>
        private void PrintDailySummary(IAnsiConsole console, SignalReport report)
        {
            console.Write(new Rule());

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"策略在 T+1 至 T+{report.BacktestDays} 期间的每日表现统计")
                .Caption($"基于 {report.ValidSignalCount} 个有效信号")
                .AddColumn("持有天数")
                .AddColumn(new TableColumn("平均收益率").RightAligned())
                .AddColumn(new TableColumn("收益率中位数").RightAligned())
                .AddColumn(new TableColumn("胜率").RightAligned())
                .AddColumn(new TableColumn("平均盈利").RightAligned())
                .AddColumn(new TableColumn("平均亏损").RightAligned())
                .AddColumn(new TableColumn("盈亏比").RightAligned());

            for (int i = 0; i < report.BacktestDays; i++)
            {
                string avgReturnColor = report.AvgReturns[i] > 0 ? "springgreen3" : "indianred";
                string medianReturnColor = report.MedianReturns[i] > 0 ? "springgreen3" : "indianred";
                table.AddRow(
                    $"T+{i + 1}",
                    $"[{avgReturnColor}]{report.AvgReturns[i]:P2}[/]",
                    $"[{medianReturnColor}]{report.MedianReturns[i]:P2}[/]",
                    $"[deepskyblue1]{report.WinRates[i]:P2}[/]",
                    $"[springgreen3]{report.AvgWinReturns[i]:P2}[/]",
                    $"[indianred]{report.AvgLossReturns[i]:P2}[/]",
                    $"[deepskyblue1]{report.WinLossRatio[i]:F2}[/]"
                );
            }
            console.Write(table);
        }

        /// <summary>
        /// 打印 T+X 月度收益统计
        /// </summary>
        private void PrintMonthlyReturns(IAnsiConsole console, SignalReport report, List<Trade> trades)
        {
            // 找到最佳平均收益率的持有天数
            var maxAvgReturn = report.AvgReturns.Max();
            int bestDay = Array.IndexOf(report.AvgReturns.ToArray(), maxAvgReturn); // 索引 (0-based)

            var returnsWithDate = trades.Zip(report.Returns, (signal, returns) => new
            {
                Date = signal.EntryDate,
                Return = returns[bestDay]
            });

            var monthlyStats = returnsWithDate
                .GroupBy(x => new { x.Date.Year, x.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g =>
                {
                    // 将当月的所有收益率提取到一个列表中，避免重复枚举
                    var monthlyReturns = g.Select(x => x.Return).ToList();
                    // 分离出盈利和亏损的部分
                    var winningReturns = monthlyReturns.Where(r => r > 0).ToList();
                    var losingReturns = monthlyReturns.Where(r => r < 0).ToList();

                    // 计算平均盈利和亏损
                    var avgWin = winningReturns.Any() ? winningReturns.Average() : 0;
                    var avgLoss = losingReturns.Any() ? losingReturns.Average() : 0;

                    // 计算盈亏比
                    var winLossRatio = (avgLoss < 0) ? avgWin / Math.Abs(avgLoss) : 0;

                    // 返回一个包含所有新旧指标的匿名对象
                    return new
                    {
                        Month = new DateTime(g.Key.Year, g.Key.Month, 1),
                        SignalCount = monthlyReturns.Count,
                        AvgReturn = monthlyReturns.Average(),
                        MedianReturn = monthlyReturns.Median(),
                        WinRate = (double)winningReturns.Count / monthlyReturns.Count,
                        AvgWin = avgWin,
                        AvgLoss = avgLoss,
                        WinLossRatio = winLossRatio
                    };
                });

            console.Write(new Rule());
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"策略信号在 T+{bestDay + 1} 的月度表现统计")
                .AddColumn("月份")
                .AddColumn(new TableColumn("信号数").RightAligned())
                .AddColumn(new TableColumn("月度平均收益").RightAligned())
                .AddColumn(new TableColumn("月度中位数收益").RightAligned())
                .AddColumn(new TableColumn("胜率").RightAligned())
                .AddColumn(new TableColumn("月度平均盈利").RightAligned())
                .AddColumn(new TableColumn("月度平均亏损").RightAligned())
                .AddColumn(new TableColumn("月度盈亏比").RightAligned());

            foreach (var stat in monthlyStats)
            {
                string avgReturnColor = stat.AvgReturn > 0 ? "springgreen3" : "indianred";
                string medianReturnColor = stat.MedianReturn > 0 ? "springgreen3" : "indianred";

                table.AddRow(
                    $"{stat.Month:yyyy-MM}",
                    $"{stat.SignalCount}",
                    $"[{avgReturnColor}]{stat.AvgReturn:P2}[/]",
                    $"[{medianReturnColor}]{stat.MedianReturn:P2}[/]",
                    $"[deepskyblue1]{stat.WinRate:P2}[/]",
                    $"[springgreen3]{stat.AvgWin:P2}[/]",
                    $"[indianred]{stat.AvgLoss:P2}[/]",
                    $"[deepskyblue1]{stat.WinLossRatio:F2}[/]"
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