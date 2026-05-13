namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 市场快照数据源的元信息契约。
/// 描述数据源的维度结构，包括股票列表与可用字段名称，供 Loader 在加载前进行预检查。
/// </summary>
public interface IMarketSnapshotMetadata
{
    /// <summary>
    /// 获取数据源中所有可用的股票代码列表（源端定义，加载前不保证全局对齐）。
    /// </summary>
    IReadOnlyList<string> Symbols { get; }

    /// <summary>
    /// 获取数据源中所有可用的字段名称列表。
    /// 例如："Date"、"Close"、"Open"、"Volume"。
    /// </summary>
    IReadOnlyList<string> FieldNames { get; }

    /// <summary>
    /// 获取指定字段的 CLR 逻辑类型。
    /// 例如：typeof(double)、typeof(int)、typeof(DateTime)。
    /// </summary>
    /// <param name="fieldName">字段名称。</param>
    /// <returns>该字段对应的 CLR 类型。</returns>
    Type GetFieldType(string fieldName);
}

/// <summary>
/// 市场宽表快照数据源契约（ETL 层）。
/// 将物理格式（CSV 行 / Parquet Chunk）抽象为统一的流式快照接口。
/// <para>
/// 物理行（Row）= 交易日（CurrentDate），物理列（Column）= 全市场股票聚合状态。
/// Loader 通过 MoveNext() 逐日遍历，并调用 ReadFieldSnapshot&lt;T&gt; 将当日数据写入目标内存块。
/// </para>
/// <para>
/// 禁止在此接口中设计任何导致全量数据一次性进入内存的方法，必须保持流式读取语义。
/// </para>
/// </summary>
public interface IMarketSnapshotSource : IMarketSnapshotMetadata
{
    /// <summary>
    /// 获取当前游标所在快照对应的交易日期。
    /// 在调用 MoveNext() 并返回 true 后，该属性的值被更新为当前行的日期。
    /// </summary>
    DateTime CurrentDate { get; }

    /// <summary>
    /// 将游标移动到下一个交易日（下一行/下一个 Chunk）。
    /// </summary>
    /// <returns>若存在下一个交易日则返回 true；数据源耗尽时返回 false。</returns>
    bool MoveNext();

    /// <summary>
    /// 读取当前交易日下，指定字段对应的全市场截面数据，写入目标 Span。
    /// 实现细节（CSV 列读取 / Parquet 字段解码）由具体实现类封装，调用方无需关心物理格式。
    /// </summary>
    /// <typeparam name="T">
    /// 目标数据类型，必须为非托管类型（unmanaged），以支持直接写入 Span 内存块。
    /// </typeparam>
    /// <param name="fieldName">要读取的字段名称，例如 "Close"。</param>
    /// <param name="destination">
    /// 目标内存块。长度必须等于 <see cref="IMarketSnapshotMetadata.Symbols"/> 中的股票数量。
    /// 方法执行后，destination[i] 对应 Symbols[i] 在当前日期的字段值。
    /// </param>
    void ReadFieldSnapshot<T>(string fieldName, Span<T> destination) where T : unmanaged;
}
