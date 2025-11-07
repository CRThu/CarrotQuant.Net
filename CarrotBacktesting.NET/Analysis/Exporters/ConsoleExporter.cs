using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Utility;
using Spectre.Console;
using System;
using System.Collections.Generic;
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
            // 从上下文中获取核心分析报告

            var report = context.GetArtifact<SignalReport>();
            if (report == null || report.ValidSignalCount == 0)
            {
                AnsiConsole.MarkupLine("[yellow1][ConsoleExporter] 没有有效的信号报告可供输出。[/]");
                return;
            }

            // 1. 实时输出到控制台
            //AnsiConsole.MarkupLine("\n[bold springgreen3]正在打印实时分析报告到控制台...[/]");
            //RenderReport(AnsiConsole.Console, report, context);

            // 2. 捕获输出并写入文件
            string outputDir = context.Config.ResolvePath(context.Config.Out.Exporter);
            string htmlPath = Path.Combine(outputDir, "summary.html");
            Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);
             
            var recorder = new Recorder(AnsiConsole.Console);
            RenderReport(recorder, report, context);

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

        private void RenderReport(IAnsiConsole console, SignalReport report, AnalysisContext context)
        {
            console.WriteLine();
            
            PrintPeakAnalysis(console, report);
            PrintDailySummary(console, report);

            // 月度分析需要原始信号的时间信息
            var signals = context.BacktestResult.SignalsResult.GetSignals();
            PrintMonthlyReturns(console, report, signals.ToList());
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
        private void PrintMonthlyReturns(IAnsiConsole console, SignalReport report, List<Result.SignalInfo> signals)
        {
            // 找到最佳平均收益率的持有天数
            var maxAvgReturn = report.AvgReturns.Max();
            int bestDay = Array.IndexOf(report.AvgReturns.ToArray(), maxAvgReturn); // 索引 (0-based)

            var returnsWithDate = signals.Zip(report.Returns, (signal, returns) => new
            {
                Date = signal.Date,
                Return = returns[bestDay]
            });

            var monthlyStats = returnsWithDate
                .GroupBy(x => new { x.Date.Year, x.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new
                {
                    Month = new DateTime(g.Key.Year, g.Key.Month, 1),
                    SignalCount = g.Count(),
                    AvgReturn = g.Average(x => x.Return),
                    MedianReturn = g.Select(x => x.Return).Median()
                });

            console.Write(new Rule());
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"策略信号在 T+{bestDay + 1} 的月度收益统计")
                .AddColumn("月份")
                .AddColumn(new TableColumn("信号数").RightAligned())
                .AddColumn(new TableColumn("月度平均收益").RightAligned())
                .AddColumn(new TableColumn("月度中位数收益").RightAligned());

            foreach (var stat in monthlyStats)
            {
                string avgReturnColor = stat.AvgReturn > 0 ? "springgreen3" : "indianred";
                string medianReturnColor = stat.MedianReturn > 0 ? "springgreen3" : "indianred";
                table.AddRow(
                    $"{stat.Month:yyyy-MM}",
                    $"{stat.SignalCount}",
                    $"[{avgReturnColor}]{stat.AvgReturn:P2}[/]",
                    $"[{medianReturnColor}]{stat.MedianReturn:P2}[/]"
                );
            }
            console.Write(table);
        }
    }
}
