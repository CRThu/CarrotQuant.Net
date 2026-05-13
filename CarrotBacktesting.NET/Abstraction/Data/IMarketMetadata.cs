using System;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 市场元数据契约。
/// 描述经过全局对齐后的维度信息，包括交易日历与股票列表。
/// Buffer 的行索引与 TradeDates 一一对应，列索引与 Symbols 一一对应。
/// </summary>
public interface IMarketMetadata
{
    /// <summary>
    /// 获取所有股票代码列表（固定顺序）。
    /// 顺序与 Buffer 的 ColumnIndex（列索引）严格一一对应。
    /// </summary>
    IReadOnlyList<string> Symbols { get; }

    /// <summary>
    /// 获取交易日历（升序排列）。
    /// 顺序与 Buffer 的 RowIndex（行索引）严格一一对应。
    /// </summary>
    IReadOnlyList<DateTime> TradeDates { get; }

    /// <summary>
    /// 获取指定股票代码在 Buffer 中对应的列索引。
    /// </summary>
    /// <param name="symbol">股票代码。</param>
    /// <returns>列索引，若不存在则返回 -1。</returns>
    int GetSymbolIndex(string symbol);

    /// <summary>
    /// 获取指定交易日在 Buffer 中对应的行索引。
    /// </summary>
    /// <param name="date">交易日期。</param>
    /// <returns>行索引，若不存在则返回 -1。</returns>
    int GetDateIndex(DateTime date);
}
