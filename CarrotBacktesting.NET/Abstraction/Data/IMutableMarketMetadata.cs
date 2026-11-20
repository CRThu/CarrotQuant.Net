namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 支持动态追加扩展的市场元数据契约（写端）。
/// 供实盘增量写入与动态拓宽场景使用，与只读描述契约 <see cref="IMarketMetadata"/> 严格物理隔离。
/// 标的代码单调追加且不可变更已有列映射，退市标的保留占位，确保时间序列矩阵索引严格单调一致。
/// </summary>
public interface IMutableMarketMetadata : IMarketMetadata
{
    /// <summary>
    /// 原子追加新的交易时间戳（时步推进）。
    /// </summary>
    /// <param name="date">新增的交易时间戳。</param>
    /// <returns>新增时间戳对应的行索引。</returns>
    int AppendDate(MarketTime date);

    /// <summary>
    /// 原子追加新的标的代码（新标的上市扩列）。
    /// </summary>
    /// <param name="symbol">新增的品种代码。</param>
    /// <returns>新增标的对应的列索引。</returns>
    int AppendSymbol(string symbol);
}
