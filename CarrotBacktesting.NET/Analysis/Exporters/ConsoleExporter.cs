using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Analysis.Analyzers;
using Spectre.Console;

namespace CarrotBacktesting.NET.Analysis.Exporters;

/// <summary>
/// 控制台报表导出器配置选项。
/// 支持各层统计与下钻模块的强类型布尔开关与深色 HTML 报表持久化配置，完全兼容 YAML / JSON 序列化映射。
/// </summary>
public sealed record ConsoleExporterOptions
{
    // 一级大层级开关 (默认全开)
    public bool ShowPortfolio { get; init; } = true;
    public bool ShowCapital { get; init; } = true;
    public bool ShowTrades { get; init; } = true;
    public bool ShowCost { get; init; } = true;
    public bool ShowSignals { get; init; } = true;

    // 二级细粒度下钻开关 (默认全开)
    public bool ShowMonthlyReturns { get; init; } = true;
    public bool ShowTradeEfficiency { get; init; } = true;
    public bool ShowExitTiming { get; init; } = true;
    public bool ShowTradeTags { get; init; } = true;
    public bool ShowSignalGroups { get; init; } = true;
    public bool ShowExtensions { get; init; } = true;

    // 交付选项
    public bool SaveHtml { get; init; } = false;
    public string? HtmlOutputPath { get; init; } = "summary.html";

    /// <summary>
    /// 仅展示主摘要的极简预设（关闭二级逐月与下钻详情）。
    /// </summary>
    public static ConsoleExporterOptions SummaryOnly => new()
    {
        ShowMonthlyReturns = false,
        ShowTradeEfficiency = false,
        ShowExitTiming = false,
        ShowTradeTags = false,
        ShowSignalGroups = false
    };
}

/// <summary>
/// 基于 Spectre.Console 的控制台绩效分析与报表导出器 (Console Exporter)。
/// 统筹四层量化统计与 Alpha 信号前向评估的彩色网格仪表盘渲染，支持模块开关与深色 HTML 一键落盘。
/// </summary>
public sealed class ConsoleExporter : IReportExporter
{
    private readonly ConsoleExporterOptions _options;
    private readonly IAnsiConsole _console;

    /// <summary>
    /// 默认全局单例实例（输出全部模块）。
    /// </summary>
    public static ConsoleExporter Default { get; } = new();

    public ConsoleExporter(ConsoleExporterOptions? options = null, IAnsiConsole? console = null)
    {
        _options = options ?? new ConsoleExporterOptions();
        _console = console ?? AnsiConsole.Console;
    }

    /// <summary>
    /// 执行控制台报表渲染与导出。
    /// </summary>
    /// <param name="report">分析引擎生成的综合报告聚合根。</param>
    public void Export(AnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var recorder = _options.SaveHtml ? _console.CreateRecorder() : null;
        var targetConsole = recorder ?? _console;

        targetConsole.WriteLine();
        targetConsole.Write(new Rule("[bold cyan]CarrotQuant 量化回测与实盘绩效归因报告[/]").Centered());

        if (report.Facts.Status != TradingStatus.Normal)
        {
            targetConsole.WriteLine();
            string statusTitle = report.Facts.Status switch
            {
                TradingStatus.Bankrupt => "状态提示: 账户穿仓 (Bankrupt)",
                TradingStatus.Aborted => "状态提示: 会话中止 (Aborted)",
                _ => $"状态提示: {report.Facts.Status}"
            };
            string statusDesc = report.Facts.Status switch
            {
                TradingStatus.Bankrupt => "账户总权益小于等于零，在途委托已撤销并执行强平，后续开仓委托已被拦截。",
                TradingStatus.Aborted => "交易会话异常中止退出。",
                _ => string.Empty
            };
            var alertPanel = new Panel(new Markup($"[bold yellow]{statusTitle}[/]\n{statusDesc}"))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Yellow)
            };
            targetConsole.Write(alertPanel);
            targetConsole.WriteLine();
        }

        if (_options.ShowPortfolio && report.Portfolio != null)
        {
            RenderPortfolio(targetConsole, report.Portfolio);

            if (_options.ShowMonthlyReturns && report.Portfolio.MonthlyReturns.Count > 0)
            {
                RenderMonthlyReturns(targetConsole, report.Portfolio.MonthlyReturns);
            }
        }

        if (_options.ShowCapital && report.Capital != null)
        {
            RenderCapital(targetConsole, report.Capital);
        }

        if (_options.ShowTrades && report.Trades != null)
        {
            RenderTrades(targetConsole, report.Trades);

            if (_options.ShowTradeEfficiency && report.Trades.AverageTradeEfficiency.HasValue)
            {
                RenderTradeEfficiency(targetConsole, report.Trades);
            }

            if (_options.ShowExitTiming && report.Trades.ExitTiming != null)
            {
                RenderExitTiming(targetConsole, report.Trades.ExitTiming);
            }

            if (_options.ShowTradeTags && report.Trades.TagBreakdowns.Count > 0)
            {
                RenderTradeTags(targetConsole, report.Trades.TagBreakdowns);
            }
        }

        if (_options.ShowCost && report.Cost != null)
        {
            RenderCost(targetConsole, report.Cost);
        }

        if (_options.ShowSignals && report.Signals != null)
        {
            RenderSignals(targetConsole, report.Signals);

            if (_options.ShowSignalGroups && report.Signals.GroupBreakdowns.Count > 0)
            {
                RenderSignalGroups(targetConsole, report.Signals.GroupBreakdowns);
            }
        }

        if (_options.ShowExtensions)
        {
            for (int i = 0; i < report.Results.Count; i++)
            {
                var res = report.Results[i];
                if (res is PortfolioStatistics or CapitalStatistics or TradeStatistics or CostStatistics or SignalStatistics)
                {
                    continue;
                }

                RenderGenericResult(targetConsole, res);
            }
        }

        targetConsole.Write(new Rule("[grey]报告生成完毕[/]").Centered());
        targetConsole.WriteLine();

        if (recorder != null)
        {
            SaveHtmlReport(recorder);
        }
    }

    private static void RenderPortfolio(IAnsiConsole console, PortfolioStatistics p)
    {
        console.Write(new Rule("[bold blue][[1]] 组合净值与风险收益统计 (Portfolio Statistics)[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("核心指标");
        table.AddColumn(new TableColumn("数值").RightAligned());
        table.AddColumn("核心指标");
        table.AddColumn(new TableColumn("数值").RightAligned());

        string retColor = p.TotalReturn >= 0 ? "springgreen3" : "indianred";
        string cagrColor = p.AnnualizedReturn >= 0 ? "springgreen3" : "indianred";

        table.AddRow(
            "累计收益率 (Total Return)", $"[{retColor}]{p.TotalReturn:P2}[/]",
            "年化收益率 (CAGR)", $"[{cagrColor}]{p.AnnualizedReturn:P2}[/]"
        );
        table.AddRow(
            "年化波动率 (Volatility)", $"{p.AnnualizedVolatility:P2}",
            "下行波动率 (Downside Vol)", $"{p.DownsideVolatility:P2}"
        );
        table.AddRow(
            "夏普比率 (Sharpe)", $"[deepskyblue1]{p.SharpeRatio:F2}[/]",
            "索提诺比率 (Sortino)", $"[deepskyblue1]{p.SortinoRatio:F2}[/]"
        );
        table.AddRow(
            "卡玛比率 (Calmar)", $"[deepskyblue1]{p.CalmarRatio:F2}[/]",
            "最大回撤 (Max Drawdown)", $"[indianred]{p.MaxDrawdown:P2}[/]"
        );
        table.AddRow(
            "最大回撤持续步数 (Steps)", $"{p.MaxDrawdownDurationSteps}",
            "日度 95% 在险价值 (VaR95)", $"{p.DailyVaR95:P2}"
        );

        if (p.BenchmarkReturn.HasValue || p.Beta.HasValue || p.Alpha.HasValue || p.InformationRatio.HasValue)
        {
            string benchColor = (p.BenchmarkReturn ?? 0.0) >= 0 ? "springgreen3" : "indianred";
            string alphaColor = (p.Alpha ?? 0.0) >= 0 ? "springgreen3" : "indianred";
            string benchStr = p.BenchmarkReturn.HasValue ? $"{p.BenchmarkReturn.Value:P2}" : "N/A";
            string betaStr = p.Beta.HasValue ? $"{p.Beta.Value:F2}" : "N/A";
            string alphaStr = p.Alpha.HasValue ? $"{p.Alpha.Value:P2}" : "N/A";
            string irStr = p.InformationRatio.HasValue ? $"{p.InformationRatio.Value:F2}" : "N/A";

            table.AddRow(
                "基准累计收益 (Benchmark)", $"[{benchColor}]{benchStr}[/]",
                "系统贝塔 (Beta)", $"[yellow1]{betaStr}[/]"
            );
            table.AddRow(
                "詹森阿尔法 (Alpha)", $"[{alphaColor}]{alphaStr}[/]",
                "信息比率 (Information Ratio)", $"[deepskyblue1]{irStr}[/]"
            );
        }

        console.Write(table);
    }

    private static void RenderMonthlyReturns(IAnsiConsole console, IReadOnlyList<MonthlyReturn> monthly)
    {
        console.Write(new Rule("[bold yellow]逐月收益率分布 (Monthly Returns)[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("年份");
        table.AddColumn("月份");
        table.AddColumn(new TableColumn("月度收益率").RightAligned());

        foreach (var m in monthly)
        {
            string color = m.ReturnRate >= 0 ? "springgreen3" : "indianred";
            table.AddRow($"{m.Year}", $"{m.Month:D2}月", $"[{color}]{m.ReturnRate:P2}[/]");
        }

        console.Write(table);
    }

    private static void RenderCapital(IAnsiConsole console, CapitalStatistics c)
    {
        console.Write(new Rule("[bold blue][[2]] 资本利用与敞口分析 (Capital Statistics)[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("资本与敞口指标");
        table.AddColumn(new TableColumn("数值").RightAligned());
        table.AddColumn("资本与敞口指标");
        table.AddColumn(new TableColumn("数值").RightAligned());

        string statusStr = c.Status switch
        {
            TradingStatus.Bankrupt => "[bold red]Bankrupt (穿仓)[/]",
            TradingStatus.Aborted => "[bold yellow]Aborted (中止)[/]",
            _ => "[springgreen3]Normal (正常)[/]"
        };

        table.AddRow(
            "交易终局状态 (Status)", statusStr,
            "年化单边换手率 (Turnover)", $"[yellow1]{c.AnnualizedTurnover:F2}x[/]"
        );
        table.AddRow(
            "平均持仓占比 (Avg Position)", $"[cyan]{c.AveragePositionRatio:P2}[/]",
            "最大持仓占比 (Max Position)", $"[cyan]{c.MaxPositionRatio:P2}[/]"
        );
        table.AddRow(
            "平均保证金占用 (Avg Margin)", $"{c.AverageMarginRatio:P2}",
            "最大保证金占用 (Max Margin)", $"{c.MaxMarginRatio:P2}"
        );
        table.AddRow(
            "现金闲置率 (Cash Drag)", $"{c.CashDrag:P2}",
            "-", "-"
        );

        console.Write(table);
    }

    private static void RenderTrades(IAnsiConsole console, TradeStatistics t)
    {
        console.Write(new Rule("[bold blue][[3]] 交易行为与执行归因 (Trade Statistics)[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("交易统计指标");
        table.AddColumn(new TableColumn("数值").RightAligned());
        table.AddColumn("交易统计指标");
        table.AddColumn(new TableColumn("数值").RightAligned());

        string winRateColor = t.WinRate >= 0.5 ? "springgreen3" : "yellow1";

        table.AddRow(
            "总交易笔数 (Total Trades)", $"[cyan]{t.TotalTrades}[/]",
            "总体胜率 (Win Rate)", $"[{winRateColor}]{t.WinRate:P2}[/]"
        );
        table.AddRow(
            "盈利 / 亏损 / 保本笔数", $"{t.WinningTrades} / {t.LosingTrades} / {t.BreakevenTrades}",
            "盈亏比 (Win/Loss Ratio)", $"[yellow1]{t.WinLossRatio:F2}[/]"
        );
        table.AddRow(
            "多头笔数 / 胜率", $"{t.LongTrades} / {t.LongWinRate:P2}",
            "空头笔数 / 胜率", $"{t.ShortTrades} / {t.ShortWinRate:P2}"
        );
        table.AddRow(
            "平均收益率 (Avg Return)", $"{t.AverageReturn:P2}",
            "中位数收益率 (Median Return)", $"{t.MedianReturn:P2}"
        );
        table.AddRow(
            "平均盈利幅度 (Avg Win)", $"[springgreen3]{t.AverageWinReturn:P2}[/]",
            "平均亏损幅度 (Avg Loss)", $"[indianred]{t.AverageLossReturn:P2}[/]"
        );
        table.AddRow(
            "盈利因子 (Profit Factor)", $"[deepskyblue1]{t.ProfitFactor:F2}[/]",
            "最大连胜 / 连败", $"{t.MaxConsecutiveWins} / {t.MaxConsecutiveLosses}"
        );
        table.AddRow(
            "平均持仓步数 (Avg Holding)", $"{t.AverageHoldingSteps:F1} 步",
            "盈利/亏损持仓步数", $"{t.AverageWinHoldingSteps:F1} / {t.AverageLossHoldingSteps:F1} 步"
        );

        console.Write(table);
    }

    private static void RenderTradeEfficiency(IAnsiConsole console, TradeStatistics t)
    {
        console.Write(new Rule("[bold yellow]交易效率评估 (Trade Efficiency / MFE)[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("效率指标");
        table.AddColumn(new TableColumn("数值").RightAligned());

        table.AddRow("平均最大有利偏离捕获效率 (Avg Trade Efficiency)", $"[cyan]{t.AverageTradeEfficiency:P2}[/]");
        table.AddRow("中位数最大有利偏离捕获效率 (Median Trade Efficiency)", $"[cyan]{t.MedianTradeEfficiency:P2}[/]");

        console.Write(table);
    }

    private static void RenderExitTiming(IAnsiConsole console, ExitTimingStatistics et)
    {
        console.Write(new Rule($"[bold yellow]卖点时机反思走势 (Exit Timing T+1..T+{et.HorizonSteps})[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("平仓后续步数");
        table.AddColumn(new TableColumn("平均后续收益").RightAligned());
        table.AddColumn(new TableColumn("中位数后续收益").RightAligned());
        table.AddColumn(new TableColumn("后续上涨概率 (踏空风险)").RightAligned());

        for (int i = 0; i < et.HorizonSteps; i++)
        {
            double avg = et.AverageReturns[i];
            double med = et.MedianReturns[i];
            double win = et.WinRates[i];

            string avgColor = avg >= 0 ? "springgreen3" : "indianred";
            string medColor = med >= 0 ? "springgreen3" : "indianred";

            table.AddRow(
                $"T+{i + 1}",
                $"[{avgColor}]{avg:P2}[/]",
                $"[{medColor}]{med:P2}[/]",
                $"[deepskyblue1]{win:P2}[/]"
            );
        }

        console.Write(table);
    }

    private static void RenderTradeTags(IAnsiConsole console, IReadOnlyDictionary<string, TradeStatistics> tagBreakdowns)
    {
        console.Write(new Rule("[bold yellow]策略标签下钻分析 (Tag Breakdowns)[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("策略标签 (Tag)");
        table.AddColumn(new TableColumn("交易笔数").RightAligned());
        table.AddColumn(new TableColumn("胜率").RightAligned());
        table.AddColumn(new TableColumn("平均收益").RightAligned());
        table.AddColumn(new TableColumn("盈亏比").RightAligned());
        table.AddColumn(new TableColumn("盈利因子").RightAligned());

        foreach (var (tag, s) in tagBreakdowns)
        {
            string winColor = s.WinRate >= 0.5 ? "springgreen3" : "yellow1";
            table.AddRow(
                $"[bold]{Markup.Escape(tag)}[/]",
                $"{s.TotalTrades}",
                $"[{winColor}]{s.WinRate:P2}[/]",
                $"{s.AverageReturn:P2}",
                $"{s.WinLossRatio:F2}",
                $"{s.ProfitFactor:F2}"
            );
        }

        console.Write(table);
    }

    private static void RenderCost(IAnsiConsole console, CostStatistics c)
    {
        console.Write(new Rule("[bold blue][[4]] 现实损耗与交易成本 (Cost Statistics)[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("摩擦损耗项目");
        table.AddColumn(new TableColumn("数值").RightAligned());
        table.AddColumn("摩擦损耗项目");
        table.AddColumn(new TableColumn("数值").RightAligned());

        table.AddRow(
            "累计手续费 (Commission)", $"[indianred]{c.TotalCommission:C2}[/]",
            "累计滑点损耗 (Slippage)", $"[indianred]{c.TotalSlippage:C2}[/]"
        );
        table.AddRow(
            "综合摩擦成本 (Total Friction)", $"[indianred]{c.TotalFrictionCost:C2}[/]",
            "费用占毛利比 (Fee/Gross Profit)", $"{c.FeeToGrossProfitRatio:P2}"
        );
        table.AddRow(
            "单笔平均摩擦成本 (Avg Friction)", $"{c.AverageFrictionPerTrade:C2}",
            "-", "-"
        );

        console.Write(table);
    }

    private static void RenderSignals(IAnsiConsole console, SignalStatistics sig)
    {
        console.Write(new Rule($"[bold blue][[5]] 独立 Alpha 信号前向收益评估 (Signal Statistics) - 有效信号数: {sig.TotalSignals}[/]").LeftJustified());

        if (sig.TotalSignals == 0 || sig.Horizons.Count == 0)
        {
            console.MarkupLine("[yellow]无有效信号评估数据[/]");
            return;
        }

        var peakHorizon = sig.Horizons.FirstOrDefault(h => h.Step == sig.PeakHorizonStep);
        if (peakHorizon != null)
        {
            var p = peakHorizon.SampleWeighted;
            console.MarkupLine($"[bold springgreen3]最优前向持有周期识别: T+{sig.PeakHorizonStep} 步[/]");
            console.MarkupLine($"平均收益率: [springgreen3]{p.AvgReturn:P2}[/] | 胜率: [deepskyblue1]{p.WinRate:P2}[/] | 盈亏比: [yellow1]{p.WinLossRatio:F2}[/] | 平均盈利: [springgreen3]{p.AvgWin:P2}[/] | 平均亏损: [indianred]{p.AvgLoss:P2}[/]");
            console.WriteLine();
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("持有步数");
        table.AddColumn(new TableColumn("样本等权均值").RightAligned());
        table.AddColumn(new TableColumn("样本等权中位数").RightAligned());
        table.AddColumn(new TableColumn("样本等权胜率").RightAligned());
        table.AddColumn(new TableColumn("样本等权盈亏比").RightAligned());
        table.AddColumn(new TableColumn("月度时间等权均值").RightAligned());
        table.AddColumn(new TableColumn("月度时间等权胜率").RightAligned());

        foreach (var h in sig.Horizons)
        {
            var sw = h.SampleWeighted;
            var tw = h.TimeWeighted;

            string swAvgColor = sw.AvgReturn >= 0 ? "springgreen3" : "indianred";
            string twAvgColor = tw.AvgReturn >= 0 ? "springgreen3" : "indianred";

            table.AddRow(
                $"T+{h.Step}",
                $"[{swAvgColor}]{sw.AvgReturn:P2}[/]",
                $"{sw.MedianReturn:P2}",
                $"[deepskyblue1]{sw.WinRate:P2}[/]",
                $"{sw.WinLossRatio:F2}",
                $"[{twAvgColor}]{tw.AvgReturn:P2}[/]",
                $"[deepskyblue1]{tw.WinRate:P2}[/]"
            );
        }

        console.Write(table);
    }

    private static void RenderSignalGroups(IAnsiConsole console, IReadOnlyDictionary<string, SignalStatistics> groupBreakdowns)
    {
        console.Write(new Rule("[bold yellow]Alpha 信号分组下钻分析 (Signal Group Breakdowns)[/]").LeftJustified());

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("信号组别 (Group)");
        table.AddColumn(new TableColumn("有效样本量").RightAligned());
        table.AddColumn(new TableColumn("最优持有周期").RightAligned());
        table.AddColumn(new TableColumn("最优周期均值").RightAligned());
        table.AddColumn(new TableColumn("最优周期胜率").RightAligned());

        foreach (var (group, sub) in groupBreakdowns)
        {
            var peak = sub.Horizons.FirstOrDefault(h => h.Step == sub.PeakHorizonStep);
            string avgStr = peak != null ? $"{peak.SampleWeighted.AvgReturn:P2}" : "N/A";
            string winStr = peak != null ? $"{peak.SampleWeighted.WinRate:P2}" : "N/A";

            table.AddRow(
                $"[bold]{Markup.Escape(group)}[/]",
                $"{sub.TotalSignals}",
                $"T+{sub.PeakHorizonStep}",
                $"[springgreen3]{avgStr}[/]",
                $"[deepskyblue1]{winStr}[/]"
            );
        }

        console.Write(table);
    }

    private static void RenderGenericResult(IAnsiConsole console, IAnalysisResult result)
    {
        console.Write(new Rule($"[bold yellow]{Markup.Escape(result.Title)} ({Markup.Escape(result.Name)})[/]").LeftJustified());

        var metrics = result.GetMetrics().ToList();
        if (metrics.Count == 0) return;

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("指标名称 (Metric)");
        table.AddColumn(new TableColumn("数值 (Value)").RightAligned());
        table.AddColumn("分类 (Category)");

        foreach (var m in metrics)
        {
            table.AddRow(
                $"[bold]{Markup.Escape(m.Label)}[/]",
                $"[cyan]{Markup.Escape(m.Value)}[/]",
                $"{Markup.Escape(m.Category ?? string.Empty)}"
            );
        }

        console.Write(table);
    }

    private void SaveHtmlReport(Recorder recorder)
    {
        try
        {
            string html = recorder.ExportHtml();
            const string darkThemeCss = @"
<style>
body { background-color: #1e1e1e; color: #d4d4d4; font-family: Consolas, monospace; padding: 20px; }
table { margin-bottom: 20px; }
</style>";
            string finalHtml = darkThemeCss + html;

            string outputPath = !string.IsNullOrWhiteSpace(_options.HtmlOutputPath) ? _options.HtmlOutputPath : "summary.html";
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(outputPath, finalHtml);
            _console.MarkupLine($"[bold springgreen3]分析报告已成功保存至: [link]{Path.GetFullPath(outputPath)}[/][/]");
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[bold indianred]保存 HTML 报告失败: {ex.Message}[/]");
        }
    }
}
