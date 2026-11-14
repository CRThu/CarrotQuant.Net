using System;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 纵向时间序列市场数据源契约（底层物理格式抽取层）。
/// 负责针对指定标的，将历史时序指标按需切片提取并零拷贝写入目标内存块。
/// 所有的可用标的代码与全局交易日历通过继承自 <see cref="IMarketMetadata"/> 统一暴露。
/// </summary>
public interface ITimeseriesSource : IMarketMetadata, IDisposable
{
    /// <summary>
    /// 读取指定标的在指定交易日区间内的指标时序序列，直接写入目标内存块。
    /// 遵循统一对齐与缺失值契约：对于停牌、未上市或漏采的交易日，目标单元格必须严格置为标准 NaN 哨兵值。
    /// </summary>
    /// <typeparam name="T">
    /// 目标数据类型，必须为非托管类型（unmanaged），以支持非托管内存块直接拷贝。
    /// </typeparam>
    /// <param name="symbol">要读取的标的代码。</param>
    /// <param name="fieldName">字段名称，例如 "close"、"volume"。</param>
    /// <param name="startIndex">
    /// 起始交易日对应在 <see cref="IMarketMetadata.TradeDates"/> 中的索引位置。
    /// </param>
    /// <param name="length">需要读取的连续交易日跨度长度。</param>
    /// <param name="destination">
    /// 目标内存块，长度必须精确等于 <paramref name="length"/>。
    /// </param>
    void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged;
}
