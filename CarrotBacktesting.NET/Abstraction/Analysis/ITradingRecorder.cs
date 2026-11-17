using System;
using System.Collections.Generic;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 交易事实记录器接口，负责接收运行期的事实账目事件并导出交易账本结果。
/// 回测与实盘通用。
/// </summary>
public interface ITradingRecorder
{
    /// <summary>
    /// 记录时间步资产净值点（含现金、冻结、保证金与总浮动盈亏）。
    /// </summary>
    /// <param name="point">资产点数据。</param>
    void RecordStep(EquityPoint point);

    /// <summary>
    /// 记录日终全景资产与持仓快照（含标的未实现收益与保证金占用）。
    /// </summary>
    /// <param name="snapshot">日终快照数据。</param>
    void RecordDailySnapshot(DailySnapshot snapshot);

    /// <summary>
    /// 记录一笔交割单明细。
    /// </summary>
    /// <param name="settlement">交割单记录。</param>
    void RecordSettlement(SettlementRecord settlement);

    /// <summary>
    /// 记录一笔已平仓交易结算。
    /// </summary>
    /// <param name="trade">平仓交易记录。</param>
    void RecordClosedTrade(Trade trade);

    /// <summary>
    /// 记录策略信号打点数据。
    /// </summary>
    /// <param name="signal">信号记录。</param>
    void RecordSignal(SignalRecord signal);

    /// <summary>
    /// 生成并返回交易账本综合结果集。
    /// </summary>
    /// <param name="endingPositions">当前或结束时的未平仓持仓明细（含未实现收益）。</param>
    /// <returns>交易账本结果集。</returns>
    TradingResult GetResult(IReadOnlyList<PositionSnapshot> endingPositions);
}
