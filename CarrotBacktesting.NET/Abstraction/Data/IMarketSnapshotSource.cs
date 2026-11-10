using System;

namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 市场宽表横向流式截面数据源契约（ETL / 实盘行情流层）。
/// 主要供 LivingEngine（实盘/仿真引擎）或低内存流式回测消费，按时间截面（逐 Bar / 逐日）流式提供全市场快照。
/// <para>
/// 物理行（Row）= 当前时间截面（CurrentDate），物理列（Column）= 全市场股票聚合状态（Symbols）。
/// 消费端通过 MoveNext() 逐时间步推进，并调用 ReadFieldSnapshot&lt;T&gt; 将当前截面数据零拷贝写入目标内存块。
/// </para>
/// <para>
/// 遵循流式读取语义，禁止全量数据一次性进入内存。
/// </para>
/// </summary>
public interface IMarketSnapshotSource : IMarketMetadata, IDisposable
{
    /// <summary>
    /// 获取当前游标所在截面快照对应的交易时间点（兼容日线日期与分钟级/高频时间戳）。
    /// 在调用 MoveNext() 并返回 true 后，该属性更新为当前快照的最新时间。
    /// </summary>
    DateTime CurrentDate { get; }

    /// <summary>
    /// 将游标移动到下一个时间截面（下一行/下一个 Bar 快照）。
    /// </summary>
    /// <returns>若存在下一个截面快照则返回 true；数据源耗尽或停止时返回 false。</returns>
    bool MoveNext();

    /// <summary>
    /// 读取当前时间截面下，指定字段对应的全市场截面数据，零拷贝写入目标 Span。
    /// <para>
    /// 遵循统一缺失值契约：对于当日未上市、已退市或停牌无成交的标的，目标单元格必须严格置为标准缺失值（浮点为 IEEE 754 NaN，整型为 MinValue 哨兵）。
    /// </para>
    /// </summary>
    /// <typeparam name="T">
    /// 目标数据类型，必须为非托管类型（unmanaged），以支持极速内存块拷贝。
    /// </typeparam>
    /// <param name="fieldName">要读取的字段名称，例如 "close"、"volume"。</param>
    /// <param name="destination">
    /// 目标内存块。长度必须等于 <see cref="IMarketMetadata.Symbols"/> 中的股票数量，且 destination[i] 严格对应 Symbols[i] 在当前快照的字段值。
    /// </param>
    void ReadFieldSnapshot<T>(string fieldName, Span<T> destination) where T : unmanaged;
}
