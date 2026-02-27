using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CarrotBacktesting.NET.Utility
{
    /// <summary>
    /// Excel 布局与样式辅助类
    /// </summary>
    public static class ExcelHelper
    {
        public const string FormatPercentage = "0.00%";
        public const string FormatNumber = "0.00";

        /// <summary>
        /// 应用 Header 样式（灰色背景、加粗、自动换行）
        /// </summary>
        public static void ApplyHeaderStyle(IXLRange range, bool wrapText = false)
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = XLColor.FromHtml("#E7E6E6"); // 稍浅的灰色，更显专业
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            
            if (wrapText)
            {
                range.Style.Alignment.WrapText = true;
            }
            
            // 添加边框
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        /// <summary>
        /// 对指定区域应用三色阶条件格式（红-白-绿）
        /// </summary>
        public static void ApplyColorScale(IXLRange range)
        {
            if (range == null || range.RowCount() == 0) return;

            range.AddConditionalFormat().ColorScale()
                .LowestValue(XLColor.FromHtml("#F8696B"))         // 最小值颜色（浅红）
                .Midpoint(XLCFContentType.Number, "0", XLColor.White) // 中间点：数值为0，颜色为白
                .HighestValue(XLColor.FromHtml("#63BE7B"));       // 最大值颜色（浅绿）
        }

        /// <summary>
        /// 从指定单元格开始写入带有两层表头的表格数据
        /// </summary>
        public static int WriteTableWithTwoRowHeaders<T>(
            IXLWorksheet ws, 
            int startRow, 
            int startCol, 
            List<(string MainHeader, string[] SubHeaders)> headers, 
            IEnumerable<T> data, 
            Action<IXLRow, T> rowMapper)
        {
            int currentCol = startCol;
            
            // 写入表头
            foreach (var group in headers)
            {
                int groupWidth = group.SubHeaders.Length;
                
                // 第一行：主表头
                var mainCell = ws.Cell(startRow, currentCol);
                mainCell.Value = group.MainHeader;
                if (groupWidth > 1)
                {
                    ws.Range(startRow, currentCol, startRow, currentCol + groupWidth - 1).Merge();
                }
                
                // 第二行：子表头
                for (int i = 0; i < groupWidth; i++)
                {
                    ws.Cell(startRow + 1, currentCol + i).Value = group.SubHeaders[i];
                }
                
                currentCol += groupWidth;
            }

            int totalWidth = headers.Sum(h => h.SubHeaders.Length);
            var headerRange = ws.Range(startRow, startCol, startRow + 1, startCol + totalWidth - 1);
            ApplyHeaderStyle(headerRange);

            // 写入数据
            int currentRow = startRow + 2;
            foreach (var item in data)
            {
                var row = ws.Row(currentRow);
                rowMapper(row, item);
                currentRow++;
            }

            return currentRow;
        }

        /// <summary>
        /// 从指定单元格开始写入表格数据
        /// </summary>
        public static int WriteTable<T>(IXLWorksheet ws, int startRow, int startCol, string[] headers, IEnumerable<T> data, Action<IXLRow, T> rowMapper)
        {
            // 写入表头
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(startRow, startCol + i).Value = headers[i];
            }
            var headerRange = ws.Range(startRow, startCol, startRow, startCol + headers.Length - 1);
            ApplyHeaderStyle(headerRange);

            // 写入数据
            int currentRow = startRow + 1;
            foreach (var item in data)
            {
                var row = ws.Row(currentRow);
                rowMapper(row, item);
                currentRow++;
            }

            return currentRow; // 返回下一行行号
        }

        /// <summary>
        /// 插入图片并缩放到适合指定的单元格范围
        /// </summary>
        /// <param name="ws">工作表</param>
        /// <param name="imagePath">图片路径</param>
        /// <param name="fromRow">起始行</param>
        /// <param name="fromCol">起始列</param>
        /// <param name="toRow">结束行 (可选)</param>
        /// <param name="toCol">结束列 (可选)</param>
        public static void InsertImage(IXLWorksheet ws, string? imagePath, int fromRow, int fromCol, int? toRow = null, int? toCol = null)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                ws.Cell(fromRow, fromCol).Value = $"[图片缺失]: {imagePath ?? "null"}";
                return;
            }

            try
            {
                var image = ws.AddPicture(imagePath);
                var anchorCell = ws.Cell(fromRow, fromCol);
                
                if (toRow.HasValue && toCol.HasValue)
                {
                    // 如果指定了结束范围，锚定到范围
                    image.MoveTo(anchorCell, ws.Cell(toRow.Value, toCol.Value));
                }
                else
                {
                    // 默认缩放到一个大概范围 (比如占据 15 行 x 6 列)
                    image.MoveTo(anchorCell, ws.Cell(fromRow + 20, fromCol + 8));
                }
            }
            catch (Exception ex)
            {
                ws.Cell(fromRow, fromCol).Value = $"[图片插入失败]: {ex.Message}";
            }
        }
    }
}
