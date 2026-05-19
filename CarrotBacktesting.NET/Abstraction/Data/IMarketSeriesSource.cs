using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 市场时间序列（纵向/按股票）数据源契约（ETL 层）。
/// 用于以股票为基础的纵向批量导入通道，支持极速将数据载入内存的二维矩阵 IBuffer2D。
/// <para>
/// 物理列（Column）= 股票的历史序列，物理行（Row）= 交易日（TradeDates）。
/// 调用方通过遍历股票代码，调用 ReadSymbolSeries&lt;T&gt; 批量拷贝单只股票在特定交易日区间内的历史数据。
/// </para>
/// </summary>
public interface IMarketSeriesSource : IMarketSnapshotMetadata
{
    /// <summary>
    /// 获取数据源中所有可用的交易日期列表（已排序，用于确定纵向序列的行索引）。
    /// </summary>
    IReadOnlyList<DateTime> TradeDates { get; }

    /// <summary>
    /// 读取指定股票在特定交易日区间内，特定字段对应的历史数据，写入目标 Span（支持分块/分段读取）。
    /// </summary>
    /// <typeparam name="T">
    /// 目标数据类型，必须为非托管类型（unmanaged），以支持极速块拷贝。
    /// </typeparam>
    /// <param name="symbol">股票代码，例如 "sh.600000"。</param>
    /// <param name="fieldName">要读取的字段名称，例如 "Close"。</param>
    /// <param name="startIndex">读取的起始交易日索引（对应 <see cref="TradeDates"/> 中的索引，从 0 开始）。</param>
    /// <param name="length">要读取的连续交易日长度（必须等于 <paramref name="destination"/> 的长度）。</param>
    /// <param name="destination">
    /// 目标内存块。长度必须等于 <paramref name="length"/>。
    /// 方法执行后，destination[i] 对应该股票在 TradeDates[startIndex + i] 日期的字段值。
    /// </param>
    void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged;
}
