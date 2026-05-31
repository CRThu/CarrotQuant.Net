namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 统一的市场元数据契约。
/// 描述数据源的维度结构（股票、日期、字段）及索引寻址能力。
/// </summary>
public interface IMarketMetadata
{
    /// <summary>
    /// 获取所有股票代码列表（固定顺序）。
    /// 顺序与 Buffer 的 ColumnIndex（列索引）严格一一对应。
    /// </summary>
    IReadOnlyList<string> Symbols { get; }

    /// <summary>
    /// 获取所有可用的字段名称列表。
    /// </summary>
    IReadOnlyList<string> FieldNames { get; }

    /// <summary>
    /// 获取交易日历（升序排列）。
    /// 顺序与 Buffer 的 RowIndex（行索引）严格一一对应。
    /// </summary>
    IReadOnlyList<DateTime> TradeDates { get; }
    
    /// <summary>
    /// 获取指定字段的 CLR 逻辑类型。
    /// </summary>
    /// <param name="fieldName">字段名称。</param>
    /// <returns>该字段对应的 CLR 类型。</returns>
    Type GetFieldType(string fieldName);

    /// <summary>
    /// 获取指定股票代码对应的列索引。
    /// </summary>
    /// <param name="symbol">股票代码。</param>
    /// <returns>列索引，若不存在则返回 -1。</returns>
    int GetSymbolIndex(string symbol);

    /// <summary>
    /// 获取指定交易日对应的行索引。
    /// </summary>
    /// <param name="date">交易日期。</param>
    /// <returns>行索引，若不存在则返回 -1。</returns>
    int GetDateIndex(DateTime date);
}
