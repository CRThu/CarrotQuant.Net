using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Analysis.Analyzers;
using ClosedXML.Excel;

namespace CarrotBacktesting.NET.Analysis.Exporters;

/// <summary>
/// 结构化多 Sheet Excel 报表导出器配置选项。
/// 支持通过 YAML / JSON 声明式属性注入。
/// </summary>
public sealed class ExcelReportExporterOptions
{
    /// <summary>
    /// 输出文件路径（默认为 "report.xlsx"）。
    /// </summary>
    public string OutputPath { get; set; } = "report.xlsx";

    /// <summary>
    /// 是否导出每日资产与敞口快照 Sheet（默认 true）。
    /// </summary>
    public bool IncludeDailySnapshots { get; set; } = true;

    /// <summary>
    /// 是否导出原始信号流水 Sheet（默认 true）。
    /// </summary>
    public bool IncludeSignals { get; set; } = true;

    /// <summary>
    /// 是否导出订单生命周期事件流水 Sheet（默认 true）。
    /// </summary>
    public bool IncludeOrderEvents { get; set; } = true;
}

/// <summary>
/// 结构化多 Sheet Excel 报表导出器 (Excel Report Exporter)。
/// 基于 ClosedXML 将 AnalysisReport 的五大统计、扩展指标与全量交易事实账本
/// 导出为带数字格式、冻结窗格、自动筛选与条件色阶的专业多 Sheet 工作簿。
/// </summary>
public sealed class ExcelReportExporter : IReportExporter
{
    private const int ExcelMaxDataRows = 1_048_575; // 留出 1 行表头
    private const string FormatPercent = "0.00%";
    private const string FormatCurrency = "#,##0.00";
    private const string FormatPrice = "0.0000";
    private const string FormatRatio = "0.00";
    private const string FormatDateTime = "yyyy-mm-dd hh:mm:ss";

    /// <summary>
    /// 输出文件路径（默认为 "report.xlsx"）。
    /// </summary>
    public string OutputPath { get; set; } = "report.xlsx";

    /// <summary>
    /// 是否导出每日资产与敞口快照 Sheet。
    /// </summary>
    public bool IncludeDailySnapshots { get; set; } = true;

    /// <summary>
    /// 是否导出原始信号流水 Sheet。
    /// </summary>
    public bool IncludeSignals { get; set; } = true;

    /// <summary>
    /// 是否导出订单生命周期事件流水 Sheet。
    /// </summary>
    public bool IncludeOrderEvents { get; set; } = true;

    /// <summary>
    /// 无参构造函数（支持 YAML/JSON 配置工厂反射装配）。
    /// </summary>
    public ExcelReportExporter() { }

    /// <summary>
    /// 指定输出路径与配置构造 Excel 导出器。
    /// </summary>
    public ExcelReportExporter(string outputPath, ExcelReportExporterOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("输出路径不可为空。", nameof(outputPath));

        OutputPath = outputPath;
        if (options != null)
        {
            IncludeDailySnapshots = options.IncludeDailySnapshots;
            IncludeSignals = options.IncludeSignals;
            IncludeOrderEvents = options.IncludeOrderEvents;
        }
    }

    /// <inheritdoc />
    public void Export(AnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Save(report, OutputPath, ToOptions());
    }

    /// <summary>
    /// 将分析报告导出为多 Sheet Excel 工作簿并持久化至指定磁盘路径。
    /// </summary>
    public static void Save(AnalysisReport report, string filePath, ExcelReportExporterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("目标文件路径不可为空。", nameof(filePath));

        options ??= new ExcelReportExporterOptions();
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var workbook = BuildWorkbook(report, options);
        workbook.SaveAs(filePath);
    }

    internal static XLWorkbook BuildWorkbook(AnalysisReport report, ExcelReportExporterOptions options)
    {
        var workbook = new XLWorkbook();
        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CreateDashboardSheet(workbook, usedSheetNames, report);
        CreateMonthlyReturnsSheet(workbook, usedSheetNames, report.Portfolio);
        CreateClosedTradesSheet(workbook, usedSheetNames, report.Facts.ClosedTrades);
        CreateSettlementsSheet(workbook, usedSheetNames, report.Facts.Settlements);

        if (options.IncludeDailySnapshots && report.Facts.DailySnapshots.Count > 0)
        {
            CreateDailySnapshotsSheet(workbook, usedSheetNames, report.Facts.DailySnapshots);
        }

        if (report.Facts.OpenPositions.Count > 0)
        {
            CreateOpenPositionsSheet(workbook, usedSheetNames, report.Facts.OpenPositions);
        }

        if (report.Trades?.ExitTiming != null && report.Trades.ExitTiming.AverageReturns.Count > 0)
        {
            CreateExitTimingSheet(workbook, usedSheetNames, report.Trades.ExitTiming);
        }

        if (report.Trades?.TagBreakdowns != null && report.Trades.TagBreakdowns.Count > 0)
        {
            CreateTradeTagsSheet(workbook, usedSheetNames, report.Trades.TagBreakdowns);
        }

        if (report.Signals != null && report.Signals.Horizons.Count > 0)
        {
            CreateSignalAnalysisSheet(workbook, usedSheetNames, "Signal_Analysis", report.Signals);
            foreach (var kv in report.Signals.GroupBreakdowns)
            {
                CreateSignalAnalysisSheet(workbook, usedSheetNames, $"Sig_{kv.Key}", kv.Value);
            }
        }

        if (options.IncludeSignals && report.Facts.Signals.Count > 0)
        {
            CreateRawSignalsSheet(workbook, usedSheetNames, report.Facts.Signals);
        }

        if (options.IncludeOrderEvents && report.Facts.OrderEvents.Count > 0)
        {
            CreateOrderEventsSheet(workbook, usedSheetNames, report.Facts.OrderEvents);
        }

        return workbook;
    }

    private ExcelReportExporterOptions ToOptions() => new()
    {
        OutputPath = OutputPath,
        IncludeDailySnapshots = IncludeDailySnapshots,
        IncludeSignals = IncludeSignals,
        IncludeOrderEvents = IncludeOrderEvents
    };

    #region 各 Sheet 构建

    private static void CreateDashboardSheet(XLWorkbook wb, HashSet<string> usedNames, AnalysisReport report)
    {
        var ws = AddSheet(wb, usedNames, "Dashboard");
        int row = 1;

        var facts = report.Facts;
        string start = facts.EquityCurve.Count > 0 ? FormatTime((DateTime)facts.EquityCurve[0].Time) : "-";
        string end = facts.EquityCurve.Count > 0 ? FormatTime((DateTime)facts.EquityCurve[^1].Time) : "-";

        row = DrawKeyValueSection(ws, row, "回测与账本概览 (Session Summary)", new[]
        {
            ("终局状态", facts.Status.ToString()),
            ("时间区间", $"{start} ~ {end}"),
            ("净值步数", facts.EquityCurve.Count.ToString("N0", CultureInfo.InvariantCulture)),
            ("已平仓笔数", facts.ClosedTrades.Count.ToString("N0", CultureInfo.InvariantCulture)),
            ("交割单笔数", facts.Settlements.Count.ToString("N0", CultureInfo.InvariantCulture)),
            ("信号记录数", facts.Signals.Count.ToString("N0", CultureInfo.InvariantCulture))
        });

        foreach (var result in report.Results)
        {
            var items = result.GetMetrics().Select(m => (m.Label, m.Value)).ToList();
            if (items.Count > 0)
            {
                row = DrawKeyValueSection(ws, row, $"{result.Title} ({result.Name})", items);
            }
        }

        ws.Column(1).Width = 42;
        ws.Column(2).Width = 34;
    }

    private static int DrawKeyValueSection(IXLWorksheet ws, int startRow, string title, IEnumerable<(string Key, string Value)> rows)
    {
        var headerCell = ws.Cell(startRow, 1);
        headerCell.Value = title;
        headerCell.Style.Font.Bold = true;
        headerCell.Style.Font.FontSize = 11;
        headerCell.Style.Font.FontColor = XLColor.White;
        headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
        ws.Range(startRow, 1, startRow, 2).Merge();

        int r = startRow + 1;
        foreach (var (key, value) in rows)
        {
            ws.Cell(r, 1).Value = key;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 2).Value = value;
            ws.Range(r, 1, r, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(r, 1, r, 2).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            r++;
        }

        return r + 1;
    }

    private static void CreateMonthlyReturnsSheet(XLWorkbook wb, HashSet<string> usedNames, PortfolioStatistics? portfolio)
    {
        var ws = AddSheet(wb, usedNames, "Monthly_Returns");
        if (portfolio == null || portfolio.MonthlyReturns.Count == 0)
        {
            ws.Cell(1, 1).Value = "无月度收益数据";
            return;
        }

        string[] headers = { "年份", "1月", "2月", "3月", "4月", "5月", "6月", "7月", "8月", "9月", "10月", "11月", "12月", "全年汇总" };
        WriteHeaderRow(ws, 1, headers);

        var byYear = portfolio.MonthlyReturns
            .GroupBy(m => m.Year)
            .OrderBy(g => g.Key)
            .ToList();

        int row = 2;
        foreach (var yg in byYear)
        {
            ws.Cell(row, 1).Value = yg.Key;
            ws.Cell(row, 1).Style.Font.Bold = true;
            double annual = 1.0;

            foreach (var mr in yg)
            {
                if (mr.Month is < 1 or > 12) continue;
                int col = mr.Month + 1;
                ws.Cell(row, col).Value = Sanitize(mr.ReturnRate);
                ws.Cell(row, col).Style.NumberFormat.Format = FormatPercent;
                annual *= (1.0 + mr.ReturnRate);
            }

            ws.Cell(row, 14).Value = Sanitize(annual - 1.0);
            ws.Cell(row, 14).Style.NumberFormat.Format = FormatPercent;
            ws.Cell(row, 14).Style.Font.Bold = true;
            row++;
        }

        if (row > 2)
        {
            ApplyRedGreenColorScale(ws.Range(2, 2, row - 1, 14));
        }

        FinalizeTableSheet(ws, freezeHeader: true, autoFilter: false);
    }

    private static void CreateClosedTradesSheet(XLWorkbook wb, HashSet<string> usedNames, IReadOnlyList<Trade> trades)
    {
        string[] headers =
        {
            "标的代码", "方向", "开仓时间", "开仓价", "平仓时间", "平仓价",
            "数量", "合约乘数", "实现净盈亏", "收益率", "手续费", "滑点损耗",
            "持仓步数", "开仓标签", "平仓标签"
        };

        WriteTableWithOverflow(wb, usedNames, "Closed_Trades", headers, trades, (ws, r, t) =>
        {
            ws.Cell(r, 1).Value = t.Symbol;
            ws.Cell(r, 2).Value = t.Side == TradeSide.Long ? "Long" : "Short";
            SetDateTimeCell(ws.Cell(r, 3), (DateTime)t.EntryTime);
            SetNumberCell(ws.Cell(r, 4), t.EntryPrice, FormatPrice);
            SetDateTimeCell(ws.Cell(r, 5), (DateTime)t.ExitTime);
            SetNumberCell(ws.Cell(r, 6), t.ExitPrice, FormatPrice);
            ws.Cell(r, 7).Value = (double)t.Quantity;
            SetNumberCell(ws.Cell(r, 8), t.Multiplier, FormatRatio);
            SetNumberCell(ws.Cell(r, 9), t.RealizedPnL, FormatCurrency);
            ws.Cell(r, 9).Style.Font.FontColor = t.RealizedPnL >= 0 ? XLColor.DarkRed : XLColor.DarkGreen;
            SetNumberCell(ws.Cell(r, 10), t.ReturnRate, FormatPercent);
            ws.Cell(r, 10).Style.Font.FontColor = t.ReturnRate >= 0 ? XLColor.DarkRed : XLColor.DarkGreen;
            SetNumberCell(ws.Cell(r, 11), t.Commission, FormatCurrency);
            SetNumberCell(ws.Cell(r, 12), t.SlippageCost, FormatCurrency);
            ws.Cell(r, 13).Value = t.HoldingSteps;
            ws.Cell(r, 14).Value = t.EntryTag;
            ws.Cell(r, 15).Value = t.ExitTag;
        });
    }

    private static void CreateSettlementsSheet(XLWorkbook wb, HashSet<string> usedNames, IReadOnlyList<SettlementRecord> settlements)
    {
        string[] headers =
        {
            "交割时间", "标的代码", "订单号", "买卖方向", "成交价", "成交量",
            "成交金额", "手续费", "已实现盈亏", "可用现金余额", "订单标签"
        };

        WriteTableWithOverflow(wb, usedNames, "Settlements", headers, settlements, (ws, r, s) =>
        {
            SetDateTimeCell(ws.Cell(r, 1), (DateTime)s.Time);
            ws.Cell(r, 2).Value = s.Symbol;
            ws.Cell(r, 3).Value = s.OrderId;
            ws.Cell(r, 4).Value = s.Direction.ToString();
            SetNumberCell(ws.Cell(r, 5), s.Price, FormatPrice);
            ws.Cell(r, 6).Value = (double)s.Quantity;
            SetNumberCell(ws.Cell(r, 7), s.Turnover, FormatCurrency);
            SetNumberCell(ws.Cell(r, 8), s.Commission, FormatCurrency);
            SetNumberCell(ws.Cell(r, 9), s.RealizedPnL, FormatCurrency);
            SetNumberCell(ws.Cell(r, 10), s.BalanceCash, FormatCurrency);
            ws.Cell(r, 11).Value = s.OrderTag;
        });
    }

    private static void CreateDailySnapshotsSheet(XLWorkbook wb, HashSet<string> usedNames, IReadOnlyList<DailySnapshot> snapshots)
    {
        string[] headers =
        {
            "日期", "总资产权益", "可用资金", "挂单冻结", "占用保证金",
            "持仓总市值", "未实现盈亏", "保证金占比", "持仓标的数量"
        };

        WriteTableWithOverflow(wb, usedNames, "Daily_Snapshots", headers, snapshots, (ws, r, s) =>
        {
            SetDateTimeCell(ws.Cell(r, 1), (DateTime)s.Time);
            SetNumberCell(ws.Cell(r, 2), s.TotalEquity, FormatCurrency);
            SetNumberCell(ws.Cell(r, 3), s.AvailableCash, FormatCurrency);
            SetNumberCell(ws.Cell(r, 4), s.FrozenCash, FormatCurrency);
            SetNumberCell(ws.Cell(r, 5), s.OccupiedMargin, FormatCurrency);
            SetNumberCell(ws.Cell(r, 6), s.TotalMarketValue, FormatCurrency);
            SetNumberCell(ws.Cell(r, 7), s.UnrealizedPnL, FormatCurrency);
            SetNumberCell(ws.Cell(r, 8), s.MarginRatio, FormatPercent);
            ws.Cell(r, 9).Value = s.PositionCount;
        });
    }

    private static void CreateOpenPositionsSheet(XLWorkbook wb, HashSet<string> usedNames, IReadOnlyList<PositionSnapshot> positions)
    {
        string[] headers =
        {
            "标的代码", "方向", "总持仓量", "可用数量", "冻结数量",
            "成本均价", "盯市价格", "合约乘数", "持仓市值", "未实现盈亏",
            "保证金率", "占用保证金", "净值权重"
        };

        WriteTableWithOverflow(wb, usedNames, "Open_Positions", headers, positions, (ws, r, p) =>
        {
            ws.Cell(r, 1).Value = p.Symbol;
            ws.Cell(r, 2).Value = p.Side == TradeSide.Long ? "Long" : "Short";
            ws.Cell(r, 3).Value = (double)p.Quantity;
            ws.Cell(r, 4).Value = (double)p.AvailableQuantity;
            ws.Cell(r, 5).Value = (double)p.FrozenQuantity;
            SetNumberCell(ws.Cell(r, 6), p.CostPrice, FormatPrice);
            SetNumberCell(ws.Cell(r, 7), p.CurrentPrice, FormatPrice);
            SetNumberCell(ws.Cell(r, 8), p.Multiplier, FormatRatio);
            SetNumberCell(ws.Cell(r, 9), p.MarketValue, FormatCurrency);
            SetNumberCell(ws.Cell(r, 10), p.UnrealizedPnL, FormatCurrency);
            SetNumberCell(ws.Cell(r, 11), p.MarginRate, FormatPercent);
            SetNumberCell(ws.Cell(r, 12), p.OccupiedMargin, FormatCurrency);
            SetNumberCell(ws.Cell(r, 13), p.Weight, FormatPercent);
        });
    }

    private static void CreateExitTimingSheet(XLWorkbook wb, HashSet<string> usedNames, ExitTimingStatistics exitTiming)
    {
        var ws = AddSheet(wb, usedNames, "Exit_Timing");
        string[] headers = { "平仓后周期", "有效样本数", "平均后续收益", "中位数后续收益", "后续上涨概率" };
        WriteHeaderRow(ws, 1, headers);

        int count = exitTiming.AverageReturns.Count;
        for (int i = 0; i < count; i++)
        {
            int r = i + 2;
            ws.Cell(r, 1).Value = $"T+{i + 1}";
            ws.Cell(r, 2).Value = exitTiming.ValidTradesCount;
            SetNumberCell(ws.Cell(r, 3), exitTiming.AverageReturns[i], FormatPercent);
            SetNumberCell(ws.Cell(r, 4), exitTiming.MedianReturns[i], FormatPercent);
            SetNumberCell(ws.Cell(r, 5), exitTiming.WinRates[i], FormatPercent);
        }

        if (count > 0)
        {
            ApplyRedGreenColorScale(ws.Range(2, 3, count + 1, 3));
        }

        FinalizeTableSheet(ws, freezeHeader: true, autoFilter: true);
    }

    private static void CreateTradeTagsSheet(XLWorkbook wb, HashSet<string> usedNames, IReadOnlyDictionary<string, TradeStatistics> tags)
    {
        var ws = AddSheet(wb, usedNames, "Trade_Tags");
        string[] headers = { "策略标签", "总笔数", "胜率", "平均收益率", "中位数收益率", "盈亏比", "盈利因子", "平均持仓步数" };
        WriteHeaderRow(ws, 1, headers);

        int row = 2;
        foreach (var kv in tags.OrderByDescending(x => x.Value.TotalTrades))
        {
            var s = kv.Value;
            ws.Cell(row, 1).Value = kv.Key;
            ws.Cell(row, 2).Value = s.TotalTrades;
            SetNumberCell(ws.Cell(row, 3), s.WinRate, FormatPercent);
            SetNumberCell(ws.Cell(row, 4), s.AverageReturn, FormatPercent);
            SetNumberCell(ws.Cell(row, 5), s.MedianReturn, FormatPercent);
            SetNumberCell(ws.Cell(row, 6), s.WinLossRatio, FormatRatio);
            SetNumberCell(ws.Cell(row, 7), s.ProfitFactor, FormatRatio);
            SetNumberCell(ws.Cell(row, 8), s.AverageHoldingSteps, FormatRatio);
            row++;
        }

        FinalizeTableSheet(ws, freezeHeader: true, autoFilter: true);
    }

    private static void CreateSignalAnalysisSheet(XLWorkbook wb, HashSet<string> usedNames, string sheetName, SignalStatistics stats)
    {
        var ws = AddSheet(wb, usedNames, sheetName);
        string[] headers =
        {
            "持有周期", "样本数",
            "样本等权_平均收益", "样本等权_中位数", "样本等权_胜率", "样本等权_盈亏比",
            "时间等权_平均收益", "时间等权_中位数", "时间等权_胜率", "时间等权_盈亏比"
        };
        WriteHeaderRow(ws, 1, headers);

        int row = 2;
        foreach (var h in stats.Horizons)
        {
            ws.Cell(row, 1).Value = $"T+{h.Step}";
            ws.Cell(row, 2).Value = h.SampleWeighted.SignalCount;

            SetNumberCell(ws.Cell(row, 3), h.SampleWeighted.AvgReturn, FormatPercent);
            SetNumberCell(ws.Cell(row, 4), h.SampleWeighted.MedianReturn, FormatPercent);
            SetNumberCell(ws.Cell(row, 5), h.SampleWeighted.WinRate, FormatPercent);
            SetNumberCell(ws.Cell(row, 6), h.SampleWeighted.WinLossRatio, FormatRatio);

            SetNumberCell(ws.Cell(row, 7), h.TimeWeighted.AvgReturn, FormatPercent);
            SetNumberCell(ws.Cell(row, 8), h.TimeWeighted.MedianReturn, FormatPercent);
            SetNumberCell(ws.Cell(row, 9), h.TimeWeighted.WinRate, FormatPercent);
            SetNumberCell(ws.Cell(row, 10), h.TimeWeighted.WinLossRatio, FormatRatio);
            row++;
        }

        if (row > 2)
        {
            ApplyRedGreenColorScale(ws.Range(2, 3, row - 1, 3));
            ApplyRedGreenColorScale(ws.Range(2, 7, row - 1, 7));
        }

        FinalizeTableSheet(ws, freezeHeader: true, autoFilter: true);
    }

    private static void CreateRawSignalsSheet(XLWorkbook wb, HashSet<string> usedNames, IReadOnlyList<SignalRecord> signals)
    {
        string[] headers = { "时间", "标的代码", "信号值", "信号分组", "触发原因" };
        WriteTableWithOverflow(wb, usedNames, "Signals", headers, signals, (ws, r, s) =>
        {
            SetDateTimeCell(ws.Cell(r, 1), (DateTime)s.Time);
            ws.Cell(r, 2).Value = s.Symbol;
            SetNumberCell(ws.Cell(r, 3), s.SignalValue, FormatPrice);
            ws.Cell(r, 4).Value = s.SignalGroup;
            ws.Cell(r, 5).Value = s.Reason;
        });
    }

    private static void CreateOrderEventsSheet(XLWorkbook wb, HashSet<string> usedNames, IReadOnlyList<OrderEvent> events)
    {
        string[] headers = { "时间", "订单号", "标的代码", "事件类型", "状态", "方向", "委托类型", "委托量", "委托价", "成交量", "成交价", "标签", "原因" };
        WriteTableWithOverflow(wb, usedNames, "Order_Events", headers, events, (ws, r, e) =>
        {
            SetDateTimeCell(ws.Cell(r, 1), (DateTime)e.Time);
            ws.Cell(r, 2).Value = e.OrderId;
            ws.Cell(r, 3).Value = e.Symbol;
            ws.Cell(r, 4).Value = e.EventType.ToString();
            ws.Cell(r, 5).Value = e.Status.ToString();
            ws.Cell(r, 6).Value = e.Direction.ToString();
            ws.Cell(r, 7).Value = e.Type.ToString();
            ws.Cell(r, 8).Value = (double)e.Quantity;
            SetNumberCell(ws.Cell(r, 9), e.Price, FormatPrice);
            ws.Cell(r, 10).Value = (double)e.FillQuantity;
            SetNumberCell(ws.Cell(r, 11), e.FillPrice, FormatPrice);
            ws.Cell(r, 12).Value = e.OrderTag;
            ws.Cell(r, 13).Value = e.Reason ?? string.Empty;
        });
    }

    #endregion

    #region 通用写表与样式辅助方法

    private static void WriteTableWithOverflow<T>(
        XLWorkbook wb,
        HashSet<string> usedNames,
        string baseSheetName,
        string[] headers,
        IReadOnlyList<T> items,
        Action<IXLWorksheet, int, T> writeRow)
    {
        if (items.Count == 0)
        {
            var emptyWs = AddSheet(wb, usedNames, baseSheetName);
            WriteHeaderRow(emptyWs, 1, headers);
            FinalizeTableSheet(emptyWs, freezeHeader: true, autoFilter: false);
            return;
        }

        int total = items.Count;
        int offset = 0;
        int part = 1;

        while (offset < total)
        {
            string sheetName = part == 1 ? baseSheetName : $"{baseSheetName}_{part}";
            var ws = AddSheet(wb, usedNames, sheetName);
            WriteHeaderRow(ws, 1, headers);

            int chunkCount = Math.Min(ExcelMaxDataRows, total - offset);
            for (int i = 0; i < chunkCount; i++)
            {
                writeRow(ws, i + 2, items[offset + i]);
            }

            FinalizeTableSheet(ws, freezeHeader: true, autoFilter: true);
            offset += chunkCount;
            part++;
        }
    }

    private static IXLWorksheet AddSheet(XLWorkbook wb, HashSet<string> usedNames, string rawName)
    {
        string safe = SanitizeSheetName(rawName);
        string candidate = safe;
        int suffix = 2;
        while (!usedNames.Add(candidate))
        {
            string tag = $"_{suffix++}";
            int maxPrefix = Math.Max(1, 31 - tag.Length);
            candidate = (safe.Length > maxPrefix ? safe[..maxPrefix] : safe) + tag;
        }
        return wb.Worksheets.Add(candidate);
    }

    private static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Sheet";
        char[] invalid = { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('\'', ' ');
        if (cleaned.Length == 0) cleaned = "Sheet";
        if (cleaned.Length > 31) cleaned = cleaned[..31].TrimEnd('\'', ' ');
        return cleaned.Length == 0 ? "Sheet" : cleaned;
    }

    private static void WriteHeaderRow(IXLWorksheet ws, int row, string[] headers)
    {
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static void FinalizeTableSheet(IXLWorksheet ws, bool freezeHeader, bool autoFilter)
    {
        if (freezeHeader) ws.SheetView.FreezeRows(1);
        var used = ws.RangeUsed();
        if (autoFilter && used != null && used.RowCount() > 1)
        {
            used.SetAutoFilter();
        }
        ws.Columns().AdjustToContents(1, 50, 8.0, 48.0);
    }

    private static void ApplyRedGreenColorScale(IXLRange range)
    {
        range.AddConditionalFormat()
            .ColorScale()
            .LowestValue(XLColor.FromHtml("#BBF7D0"))
            .Midpoint(XLCFContentType.Number, "0", XLColor.White)
            .HighestValue(XLColor.FromHtml("#FECACA"));
    }

    private static void SetDateTimeCell(IXLCell cell, DateTime dt)
    {
        if (dt.Year < 1900)
        {
            cell.Value = "-";
            return;
        }
        cell.Value = dt;
        cell.Style.DateFormat.Format = FormatDateTime;
    }

    private static void SetNumberCell(IXLCell cell, double value, string format)
    {
        if (double.IsFinite(value))
        {
            cell.Value = value;
            cell.Style.NumberFormat.Format = format;
        }
        else
        {
            cell.Value = "-";
        }
    }

    private static double Sanitize(double value) => double.IsFinite(value) ? value : 0.0;

    private static string FormatTime(DateTime dt) =>
        dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    #endregion
}
