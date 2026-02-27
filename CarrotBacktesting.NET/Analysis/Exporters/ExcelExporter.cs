using CarrotBacktesting.NET.Analysis.Model;
using CarrotBacktesting.NET.Config.Model;
using CarrotBacktesting.NET.Result;
using ClosedXML.Excel;
using System;
using System.IO;
using System.Linq;

namespace CarrotBacktesting.NET.Analysis.Exporters
{
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

            CreateDashboardSheet(workbook, context);
            CreateSignalDetailsSheet(workbook, context);
            CreateTradeDetailsSheet(workbook, context);

            workbook.SaveAs(filePath);
            Console.WriteLine($"[ExcelExporter] Excel 报告已保存到: {Path.GetFullPath(filePath)}");
        }

        private void CreateDashboardSheet(XLWorkbook workbook, AnalysisContext context)
        {
            var ws = workbook.Worksheets.Add("Dashboard");

            ws.Cell(1, 1).Value = "分组";
            ws.Cell(1, 2).Value = "信号数";
            ws.Cell(1, 3).Value = "平均收益率";
            ws.Cell(1, 4).Value = "胜率";
            ws.Cell(1, 5).Value = "盈亏比";

            var headerRange = ws.Range(1, 1, 1, 5);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

            var signalResult = context.GetArtifact<SignalAnalysisResult>();
            if (signalResult == null) return;

            int row = 2;
            foreach (var groupName in signalResult.Groups.Keys)
            {
                var reports = signalResult[groupName];
                if (reports == null || reports.Length == 0) continue;

                var bestReport = reports.MaxBy(r => r.Global.AvgReturn)!;
                var perf = bestReport.Global;

                ws.Cell(row, 1).Value = groupName;
                ws.Cell(row, 2).Value = perf.SignalCount;
                ws.Cell(row, 3).Value = perf.AvgReturn;
                ws.Cell(row, 3).Style.NumberFormat.Format = "0.00%";
                ws.Cell(row, 4).Value = perf.WinRate;
                ws.Cell(row, 4).Style.NumberFormat.Format = "0.00%";
                ws.Cell(row, 5).Value = perf.WinLossRatio;

                var returnCell = ws.Cell(row, 3);
                returnCell.Style.Font.FontColor = perf.AvgReturn >= 0 ? XLColor.Green : XLColor.Red;

                var winRateCell = ws.Cell(row, 4);
                winRateCell.Style.Font.FontColor = perf.WinRate >= 0.5 ? XLColor.Green : XLColor.Red;

                row++;
            }

            ws.Columns().AdjustToContents();
        }

        private void CreateSignalDetailsSheet(XLWorkbook workbook, AnalysisContext context)
        {
            var ws = workbook.Worksheets.Add("SignalDetails");

            var signalResult = context.GetArtifact<SignalAnalysisResult>();
            if (signalResult == null) return;

            int currentRow = 1;

            foreach (var groupName in signalResult.Groups.Keys)
            {
                var reports = signalResult[groupName];
                if (reports == null || reports.Length == 0) continue;

                ws.Cell(currentRow, 1).Value = $"[{groupName}] 信号表现详情";
                ws.Cell(currentRow, 1).Style.Font.Bold = true;
                ws.Cell(currentRow, 1).Style.Font.FontSize = 14;
                currentRow++;

                string[] headers = { "持有天数", "信号数", "平均收益率", "中位数收益率", "胜率", "盈亏比", "平均盈利", "平均亏损" };
                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cell(currentRow, i + 1).Value = headers[i];
                }
                var headerRange = ws.Range(currentRow, 1, currentRow, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                currentRow++;

                for (int day = 0; day < reports.Length; day++)
                {
                    var r = reports[day];
                    var g = r.Global;

                    ws.Cell(currentRow, 1).Value = $"T+{day + 1}";
                    ws.Cell(currentRow, 2).Value = g.SignalCount;
                    ws.Cell(currentRow, 3).Value = g.AvgReturn;
                    ws.Cell(currentRow, 3).Style.NumberFormat.Format = "0.00%";
                    ws.Cell(currentRow, 4).Value = g.MedianReturn;
                    ws.Cell(currentRow, 4).Style.NumberFormat.Format = "0.00%";
                    ws.Cell(currentRow, 5).Value = g.WinRate;
                    ws.Cell(currentRow, 5).Style.NumberFormat.Format = "0.00%";
                    ws.Cell(currentRow, 6).Value = g.WinLossRatio;
                    ws.Cell(currentRow, 7).Value = g.AvgWin;
                    ws.Cell(currentRow, 7).Style.NumberFormat.Format = "0.00%";
                    ws.Cell(currentRow, 8).Value = g.AvgLoss;
                    ws.Cell(currentRow, 8).Style.NumberFormat.Format = "0.00%";

                    ws.Cell(currentRow, 3).Style.Font.FontColor = g.AvgReturn >= 0 ? XLColor.Green : XLColor.Red;
                    ws.Cell(currentRow, 4).Style.Font.FontColor = g.MedianReturn >= 0 ? XLColor.Green : XLColor.Red;

                    currentRow++;
                }

                string? heatmapPath = context.GetFileArtifact($"Plot_{groupName}_Heatmap_Signal");
                if (!string.IsNullOrEmpty(heatmapPath) && File.Exists(heatmapPath))
                {
                    currentRow++;
                    ws.Cell(currentRow, 1).Value = "收益率分布热力图:";
                    ws.Cell(currentRow, 1).Style.Font.Bold = true;
                    currentRow++;

                    try
                    {
                        var image = ws.AddPicture(heatmapPath);
                        image.MoveTo(ws.Cell(currentRow, 1));
                        image.Scale(0.5);
                        currentRow += 30;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ExcelExporter] 嵌入图片失败: {ex.Message}");
                        currentRow++;
                        ws.Cell(currentRow, 1).Value = $"图片加载失败: {heatmapPath}";
                    }
                }

                currentRow += 2;
            }

            ws.Columns().AdjustToContents();
        }

        private void CreateTradeDetailsSheet(XLWorkbook workbook, AnalysisContext context)
        {
            var ws = workbook.Worksheets.Add("TradeDetails");

            var trades = context.BacktestResult.Trades;
            if (trades == null || trades.Count == 0) return;

            string[] headers = { "股票代码", "入场日期", "入场价格", "出场日期", "出场价格", "收益率", "分组", "是否平仓" };
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
            }
            var headerRange = ws.Range(1, 1, 1, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

            for (int i = 0; i < trades.Count; i++)
            {
                var t = trades[i];
                int row = i + 2;

                ws.Cell(row, 1).Value = t.StockCode;
                ws.Cell(row, 2).Value = t.EntryDate;
                ws.Cell(row, 3).Value = t.EntryPrice;
                ws.Cell(row, 4).Value = t.ExitDate?.ToString("yyyy-MM-dd") ?? "-";
                ws.Cell(row, 5).Value = t.ExitPrice;
                ws.Cell(row, 6).Value = t.Return ?? 0;
                ws.Cell(row, 6).Style.NumberFormat.Format = "0.00%";
                ws.Cell(row, 7).Value = t.EntryGroup ?? "Total";
                ws.Cell(row, 8).Value = t.IsClosed ? "是" : "否";

                if (t.Return.HasValue)
                {
                    ws.Cell(row, 6).Style.Font.FontColor = t.Return >= 0 ? XLColor.Green : XLColor.Red;
                }
            }

            ws.RangeUsed()?.SetAutoFilter();

            ws.Columns().AdjustToContents();
        }
    }
}
