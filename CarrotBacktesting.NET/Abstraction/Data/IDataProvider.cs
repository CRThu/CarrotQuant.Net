using Carrot.Memory.Abstractions;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 数据层统一输出接口，供引擎层与策略层消费。
/// 聚合市场元数据与各字段的高性能只读 Buffer。
/// 遵循"只读优先"原则：所有暴露的 Buffer 均为 IReadOnlyBuffer2D，禁止引擎层对其写入。
/// </summary>
public interface IDataProvider
{
    /// <summary>
    /// 获取行情元数据（交易日历、股票列表及维度索引）。
    /// </summary>
    IMarketMetadata Metadata { get; }

    /// <summary>
    /// 获取指定字段的只读二维 Buffer。
    /// Buffer 的行维度为 TradeDates（时间），列维度为 Symbols（股票）。
    /// </summary>
    /// <typeparam name="T">
    /// 数据类型，必须为非托管类型（unmanaged），以匹配 Carrot.Memory 的物理内存布局。
    /// 通常为 <see cref="float"/> 或 <see cref="double"/>。
    /// </typeparam>
    /// <param name="fieldName">字段名称，例如 "close"、"open"、"volume"。</param>
    /// <returns>对应字段的只读 Buffer，零拷贝访问。</returns>
    IReadOnlyBuffer2D<T> GetBuffer<T>(string fieldName) where T : unmanaged;

    /// <summary>
    /// 检查是否存在指定字段的数据。
    /// </summary>
    /// <param name="fieldName">字段名称。</param>
    /// <returns>若字段已加载则返回 true，否则返回 false。</returns>
    bool HasField(string fieldName);
}
