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

    /// <summary>
    /// 原子追加新的交易时间戳（时步推进）。
    /// 默认抛出 <see cref="NotSupportedException"/>，由支持动态推进的元数据实现重写。
    /// </summary>
    /// <param name="date">新增的交易日或时间戳。</param>
    /// <returns>新增时间戳对应的行索引。</returns>
    int AppendDate(DateTime date) => throw new NotSupportedException("当前元数据实例不支持动态追加交易时间戳。");

    /// <summary>
    /// 原子追加新的标的代码（新标的上市扩列）。
    /// 默认抛出 <see cref="NotSupportedException"/>，由支持动态拓宽的元数据实现重写。
    /// 遵循 SSOT 原则：标的代码只增不减，已有索引绝对守恒。
    /// </summary>
    /// <param name="symbol">新增的品种代码。</param>
    /// <returns>新增标的对应的列索引。</returns>
    int AppendSymbol(string symbol) => throw new NotSupportedException("当前元数据实例不支持动态追加标的代码。");
}
