using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Data;
using CarrotBacktesting.NET.Result;
using CarrotBacktesting.NET.Utility;
using ClosedXML.Excel;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Analysis.Exporters
{
    /// <summary>
    /// 专业量化回测 Excel 报告导出器 (v3.1)
    /// 采用“总-分-明细”结构，引入双口径对比与自动化视觉增强。
    /// </summary>
    public class ExcelExporter : IExporter
    {
        public string Name => nameof(ExcelExporter);
        private string _fileName = "report.xlsx";

        public void Init(ExporterConfig config)
        {
            _fileName = config.File;
        }

        public void Export(AnalysisContext context)
        {
            string outputDir = context.Config.ResolvePath(context.Config.Out.Dir);
            string filePath = Path.Combine(outputDir, _fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            using var workbook = new XLWorkbook();

            // A. 建立 Dashboard (策略总览)
            CreateDashboard(workbook, context);

            // B. 建立分组深度分析
            var signalResult = context.GetArtifact<SignalAnalysisResult>();
            if (signalResult != null)
            {
                foreach (var groupName in signalResult.Groups.Keys)
                {
                    CreateGroupSheet(workbook, groupName, signalResult[groupName], context);
                    CreateMonthlyAnalysisSheet(workbook, groupName, signalResult[groupName], context);
                    CreateExitTimingSheet(workbook, groupName, context);
                }
            }

            // C. 建立 All_Trades (全量流水)
            CreateAllTradesSheet(workbook, context);

            workbook.SaveAs(filePath);
            Console.WriteLine($"[ExcelExporter] Excel 整合报表 (v3.2) 已保存至: {Path.GetFullPath(filePath)}");
        }

        private void CreateDashboard(XLWorkbook workbook, AnalysisContext context)
        {
            var ws = workbook.Worksheets.Add("Dashboard");
            ws.Column(1).Width = 35;
            ws.Column(2).Width = 50;

            int currentRow = 1;

            // 1. 数据摘要
            long totalFrames = 0;
            if (context.Data is HistoryStorage hs)
            {
                totalFrames = hs.StockHistories.Values.Sum(h => (long)h.Data.Count);
            }
            else
            {
                totalFrames = (long)context.Data.Symbols.Count * context.Data.TradeDates.Count;
            }

            currentRow = DrawSummarySection(ws, currentRow, "📊 数据摘要 (Data Summary)", new[]
            {
                ("股票数量", context.Data.Symbols.Count.ToString("N0")),
                ("全局交易日", context.Data.TradeDates.Count.ToString("N0")),
                ("时间范围", $"{context.Data.TradeDates.Min():yyyy-MM-dd} 至 {context.Data.TradeDates.Max():yyyy-MM-dd}"),
                ("存储类型", context.Data.GetType().Name),
                ("总数据点 (Frames)", totalFrames.ToString("N0"))
            });

            // 2. 回测结果摘要
            var trades = context.BacktestResult.Trades;
            currentRow = DrawSummarySection(ws, currentRow, "⚙️ 回测结果摘要", new[]
            {
                ("总计产生交易记录", trades.Count.ToString("N0")),
                ("已平仓交易", trades.Count(t => t.IsClosed).ToString("N0")),
                ("未平仓交易/信号", trades.Count(t => !t.IsClosed).ToString("N0"))
            });

            var signalResult = context.GetArtifact<SignalAnalysisResult>();
            if (signalResult != null && signalResult.Groups.ContainsKey("Total"))
            {
                var reports = signalResult["Total"];
                
                // 信号加权分析
                var bestSignal = reports.MaxBy(r => r.Global.AvgReturn)!;
                var bestDaySignal = Array.IndexOf(reports, bestSignal) + 1;
                var maxMedian = reports.MaxBy(r => r.Global.MedianReturn)!;
                var maxMedianDay = Array.IndexOf(reports, maxMedian) + 1;
                var maxWinRate = reports.MaxBy(r => r.Global.WinRate)!;
                var maxWinRateDay = Array.IndexOf(reports, maxWinRate) + 1;

                currentRow = DrawSummarySection(ws, currentRow, "📈 [Total] 策略最佳持有期分析 (信号加权)", new[]
                {
                    ("平均收益率峰值", $"{bestSignal.Global.AvgReturn:P2} (T+{bestDaySignal}, 当日胜率 {bestSignal.Global.WinRate:P2})"),
                    ("收益率中位数峰值", $"{maxMedian.Global.MedianReturn:P2} (T+{maxMedianDay}, 当日胜率 {maxMedian.Global.WinRate:P2})"),
                    ("(参考) 胜率峰值", $"{maxWinRate.Global.WinRate:P2} (T+{maxWinRateDay})")
                });

                currentRow = DrawSummarySection(ws, currentRow, $"🎯 最佳持有期 (T+{bestDaySignal}) 详细指标:", new[]
                {
                    ("平均盈利", bestSignal.Global.AvgWin.ToString("P2")),
                    ("平均亏损", bestSignal.Global.AvgLoss.ToString("P2")),
                    ("盈亏比", bestSignal.Global.WinLossRatio.ToString("F2"))
                });

                // 时间加权分析
                var bestWeighted = reports.MaxBy(r => r.WeightedGlobal.AvgReturn)!;
                var bestDayWeighted = Array.IndexOf(reports, bestWeighted) + 1;
                var maxMedianW = reports.MaxBy(r => r.WeightedGlobal.MedianReturn)!;
                var maxMedianDayW = Array.IndexOf(reports, maxMedianW) + 1;
                var maxWinRateW = reports.MaxBy(r => r.WeightedGlobal.WinRate)!;
                var maxWinRateDayW = Array.IndexOf(reports, maxWinRateW) + 1;

                currentRow = DrawSummarySection(ws, currentRow, "⏳ [Total] 策略最佳持有期分析 (时间加权)", new[]
                {
                    ("平均收益率峰值", $"{bestWeighted.WeightedGlobal.AvgReturn:P2} (T+{bestDayWeighted}, 当日胜率 {bestWeighted.WeightedGlobal.WinRate:P2})"),
                    ("收益率中位数峰值", $"{maxMedianW.WeightedGlobal.MedianReturn:P2} (T+{maxMedianDayW}, 当日胜率 {maxMedianW.WeightedGlobal.WinRate:P2})"),
                    ("(参考) 胜率峰值", $"{maxWinRateW.WeightedGlobal.WinRate:P2} (T+{maxWinRateDayW})")
                });

                currentRow = DrawSummarySection(ws, currentRow, $"🎯 最佳持有期 (T+{bestDayWeighted}) 详细指标:", new[]
                {
                    ("平均盈利", bestWeighted.WeightedGlobal.AvgWin.ToString("P2")),
                    ("平均亏损", bestWeighted.WeightedGlobal.AvgLoss.ToString("P2")),
                    ("盈亏比", bestWeighted.WeightedGlobal.WinLossRatio.ToString("F2"))
                });
            }

            // 3. 核心交易性能指标
            var tradeResult = context.GetArtifact<TradeAnalysisResult>();
            if (tradeResult != null && tradeResult.Groups.TryGetValue("Total", out var totalReport))
            {
                currentRow = DrawSummarySection(ws, currentRow, "🏆 [Total] 核心交易性能指标 (基于已平仓交易)", new[]
                {
                    ("总交易次数", totalReport.TotalTrades.ToString("N0")),
                    ("胜率", totalReport.WinRate.ToString("P2")),
                    ("平均收益率", totalReport.AverageReturn.ToString("P2")),
                    ("中位数收益率", totalReport.MedianReturn.ToString("P2")),
                    ("平均盈利", totalReport.AverageWinReturn.ToString("P2")),
                    ("平均亏损", totalReport.AverageLossReturn.ToString("P2")),
                    ("盈亏比", totalReport.WinLossRatio.ToString("F2")),
                    ("平均持仓周期", $"{totalReport.AverageHoldingPeriod:F1} 天"),
                    ("", ""),
                    ("交易效率评估 (Trade Efficiency)", ""),
                    ("  平均交易效率", totalReport.AverageTradeEfficiency.ToString("P2")),
                    ("  中位数交易效率", totalReport.MedianTradeEfficiency.ToString("P2"))
                });
            }
        }

        private int DrawSummarySection(IXLWorksheet ws, int startRow, string title, IEnumerable<(string Name, string Value)> items)
        {
            var header = ws.Cell(startRow, 1);
            header.Value = title;
            header.Style.Font.Bold = true;
            header.Style.Font.FontSize = 12;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
            header.Style.Font.FontColor = XLColor.White;
            ws.Range(startRow, 1, startRow, 2).Merge();

            int row = startRow + 1;
            foreach (var item in items)
            {
                ws.Cell(row, 1).Value = item.Name;
                ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
                ws.Cell(row, 1).Style.Font.Bold = !string.IsNullOrEmpty(item.Name);
                ws.Cell(row, 2).Value = item.Value;
                
                if (!string.IsNullOrEmpty(item.Name) || !string.IsNullOrEmpty(item.Value))
                {
                    ws.Cell(row, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }
                row++;
            }
            return row + 1;
        }

        private void CreateGroupSheet(XLWorkbook workbook, string groupName, SignalReport[] reports, AnalysisContext context)
        {
            if (reports == null || reports.Length == 0) return;

            var ws = workbook.Worksheets.Add($"{groupName}_Signal");

            // --- 区域1: 双口径并排 T+N 表 ---
            ws.Cell(1, 1).Value = $"[{groupName}] 信号表现深度分析 (并排对比)";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;

            var headers = new List<(string MainHeader, string[] SubHeaders)>
            {
                ("基本信息", new[] { "持有天数" }),
                ("信号加权 (Signal Weighted)", new[] { "平均收益", "中位数", "胜率", "盈亏比", "平均盈利", "平均亏损" }),
                ("时间加权 (Time Weighted)", new[] { "平均收益", "胜率", "盈亏比", "月度稳定性" })
            };

            int nextRow = ExcelHelper.WriteTableWithTwoRowHeaders(ws, 3, 1, headers, reports.Select((r, i) => new { Day = i + 1, r.Global, r.WeightedGlobal, Reports = r }), (row, item) =>
            {
                row.Cell(1).Value = $"T+{item.Day}";
                
                row.Cell(2).Value = item.Global.AvgReturn;
                row.Cell(2).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(3).Value = item.Global.MedianReturn;
                row.Cell(3).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(4).Value = item.Global.WinRate;
                row.Cell(4).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(5).Value = item.Global.WinLossRatio;
                row.Cell(5).Style.NumberFormat.Format = ExcelHelper.FormatNumber;
                row.Cell(6).Value = item.Global.AvgWin;
                row.Cell(6).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(7).Value = item.Global.AvgLoss;
                row.Cell(7).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;

                row.Cell(8).Value = item.WeightedGlobal.AvgReturn;
                row.Cell(8).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(9).Value = item.WeightedGlobal.WinRate;
                row.Cell(9).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(10).Value = item.WeightedGlobal.WinLossRatio;
                row.Cell(10).Style.NumberFormat.Format = ExcelHelper.FormatNumber;

                double stdDev = item.Reports.Monthly.Select(m => m.Perf.AvgReturn).StandardDeviation();
                row.Cell(11).Value = stdDev;
                row.Cell(11).Style.NumberFormat.Format = "0.0000";
            });

            // 应用色阶
            ExcelHelper.ApplyColorScale(ws.Range(5, 2, 5 + reports.Length - 1, 2)); // 信号加权收益
            ExcelHelper.ApplyColorScale(ws.Range(5, 8, 5 + reports.Length - 1, 8)); // 时间加权收益

            // --- 区域2: 可视化 Plots ---
            int plotCol = 14; 
            double plotScale = 0.28; // 稍微调小一点，防止重叠
            int rowGap = 28; // 每张图间隔 28 行

            // 1. 信号加权 T+N 概览
            ExcelHelper.InsertImage(ws, context.GetFileArtifact($"Plot_{groupName}_Overview_Signal"), 3, plotCol, plotScale);
            
            // 2. 时间加权 T+N 概览
            ExcelHelper.InsertImage(ws, context.GetFileArtifact($"Plot_{groupName}_Overview_Weighted"), 3 + rowGap, plotCol, plotScale);
            
            // 3. 信号加权分布热力图
            ExcelHelper.InsertImage(ws, context.GetFileArtifact($"Plot_{groupName}_Heatmap_Signal"), 3 + rowGap * 2, plotCol, plotScale);

            // 4. 时间加权分布热力图
            ExcelHelper.InsertImage(ws, context.GetFileArtifact($"Plot_{groupName}_Heatmap_Weighted"), 3 + rowGap * 3, plotCol, plotScale);

            ws.Columns().AdjustToContents();
            foreach (var col in ws.Columns())
            {
                if (col.Width > 20) col.Width = 20;
            }
        }

        private void CreateMonthlyAnalysisSheet(XLWorkbook workbook, string groupName, SignalReport[] reports, AnalysisContext context)
        {
            var ws = workbook.Worksheets.Add($"{groupName}_Monthly");
            
            var bestReport = reports.MaxBy(r => r.Global.AvgReturn);
            if (bestReport == null) return;

            int bestDay = Array.IndexOf(reports, bestReport) + 1;
            ws.Cell(1, 1).Value = $"[{groupName}] 最佳持有期 (T+{bestDay}) 月度分析";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;

            string[] monthlyHeaders = { "月份", "信号数", "平均收益", "中位数", "胜率", "平均盈利", "平均亏损", "盈亏比" };
            int lastRow = ExcelHelper.WriteTable(ws, 3, 1, monthlyHeaders, bestReport.Monthly, (row, m) =>
            {
                row.Cell(1).Value = m.Month.ToString("yyyy-MM");
                row.Cell(2).Value = m.Perf.SignalCount;
                row.Cell(3).Value = m.Perf.AvgReturn;
                row.Cell(3).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(4).Value = m.Perf.MedianReturn;
                row.Cell(4).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(5).Value = m.Perf.WinRate;
                row.Cell(5).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(6).Value = m.Perf.AvgWin;
                row.Cell(6).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(7).Value = m.Perf.AvgLoss;
                row.Cell(7).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(8).Value = m.Perf.WinLossRatio;
                row.Cell(8).Style.NumberFormat.Format = ExcelHelper.FormatNumber;
            });

            ExcelHelper.ApplyColorScale(ws.Range(4, 3, lastRow - 1, 3));
            
            // 插入月度信号趋势图 (Timeline Plot)，这是用户要求的“信号月度图”
            string? signalTimelinePlot = context.GetFileArtifact($"Plot_{groupName}_Timeline");
            ExcelHelper.InsertImage(ws, signalTimelinePlot, 3, 11, 0.35);

            ws.Columns().AdjustToContents();
        }

        private void CreateAllTradesSheet(XLWorkbook workbook, AnalysisContext context)
        {
            var ws = workbook.Worksheets.Add("All_Trades");
            var trades = context.BacktestResult.Trades;

            if (trades == null || trades.Count == 0)
            {
                ws.Cell(1, 1).Value = "无交易记录";
                return;
            }

            string[] headers = { "股票代码", "入场日期", "入场价格", "出场日期", "出场价格", "收益率", "分组", "是否平仓" };
            
            var tableData = trades.Select(t => new
            {
                t.StockCode,
                t.EntryDate,
                t.EntryPrice,
                ExitDate = t.ExitDate?.ToString("yyyy-MM-dd") ?? "-",
                t.ExitPrice,
                Return = t.Return ?? 0,
                Group = t.EntryGroup ?? "Total",
                IsClosed = t.IsClosed ? "是" : "否"
            }).ToList();

            ExcelHelper.WriteTable(ws, 1, 1, headers, tableData, (row, t) =>
            {
                row.Cell(1).Value = t.StockCode;
                row.Cell(2).Value = t.EntryDate;
                row.Cell(2).Style.DateFormat.Format = "yyyy-MM-dd";
                row.Cell(3).Value = t.EntryPrice;
                row.Cell(4).Value = t.ExitDate;
                row.Cell(5).Value = t.ExitPrice;
                row.Cell(6).Value = t.Return;
                row.Cell(6).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(7).Value = t.Group;
                row.Cell(8).Value = t.IsClosed;
                
                row.Cell(6).Style.Font.FontColor = t.Return >= 0 ? XLColor.Green : XLColor.Red;
            });

            ws.RangeUsed().SetAutoFilter();
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();
        }

        private void CreateExitTimingSheet(XLWorkbook workbook, string groupName, AnalysisContext context)
        {
            var tradeResult = context.GetArtifact<TradeAnalysisResult>();
            if (tradeResult == null || !tradeResult.Groups.TryGetValue(groupName, out var tradeReport)) return;
            if (tradeReport.ExitTimingAvgReturns == null || tradeReport.ExitTimingAvgReturns.Count == 0) return;

            var ws = workbook.Worksheets.Add($"{groupName}_Exit");

            ws.Cell(1, 1).Value = $"[{groupName}] 卖出时机分析 (平仓后T+N日表现)";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;

            string[] exitHeaders = { "平仓后天数", "平均后续收益", "中位数后续收益", "后续上涨概率" };
            int lastRow = ExcelHelper.WriteTable(ws, 3, 1, exitHeaders, tradeReport.ExitTimingAvgReturns.Select((val, i) => new
            {
                Day = i + 1,
                Avg = val,
                Median = tradeReport.ExitTimingMedianReturns[i],
                WinRate = tradeReport.ExitTimingWinRates[i]
            }), (row, item) =>
            {
                row.Cell(1).Value = $"T+{item.Day}";
                row.Cell(2).Value = item.Avg;
                row.Cell(2).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(3).Value = item.Median;
                row.Cell(3).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
                row.Cell(4).Value = item.WinRate;
                row.Cell(4).Style.NumberFormat.Format = ExcelHelper.FormatPercentage;
            });

            ExcelHelper.ApplyColorScale(ws.Range(4, 2, lastRow - 1, 2));

            // 可视化辅助
            ws.Columns().AdjustToContents();
        }
    }
}
