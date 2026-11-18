using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Analysis;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 账户资产与持仓全景快照提供者能力契约。
/// 允许引擎定期沉淀时间步净值点、日终全景快照与在手持仓清单，实现引擎与具体经纪商（Broker）实现的彻底解耦。
/// 遵循接口隔离原则（ISP），任何实现此接口的经纪商均可被回测记录器无损追踪。
/// </summary>
public interface IAccountSnapshotProvider
{
    /// <summary>
    /// 生成当前时间步的资产与保证金净值点。
    /// </summary>
    /// <param name="time">当前时间戳。</param>
    /// <param name="priceResolver">可选的价格解析委托。</param>
    /// <returns>时间步净值点。</returns>
    EquityPoint CreateEquityPoint(DateTime time, Func<string, double>? priceResolver = null);

    /// <summary>
    /// 权威生成当前时点的日终全景资产与持仓快照。
    /// </summary>
    /// <param name="time">快照时间戳。</param>
    /// <param name="priceResolver">可选的价格解析委托。</param>
    /// <returns>日终全景资产与持仓快照。</returns>
    DailySnapshot CreateDailySnapshot(DateTime time, Func<string, double>? priceResolver = null);

    /// <summary>
    /// 获取当前账户中所有活跃持仓（非零）的快照列表。
    /// </summary>
    /// <param name="priceResolver">可选的价格解析委托。</param>
    /// <returns>活跃持仓快照列表。</returns>
    IReadOnlyList<PositionSnapshot> GetActivePositions(Func<string, double>? priceResolver = null);

    /// <summary>
    /// 当发生每日结算时触发，提供完整的结算事实记录。
    /// </summary>
    event Action<SettlementRecord>? OnSettlement;

    /// <summary>
    /// 当平仓交易发生并确认时触发，提供闭环往返交易事实记录。
    /// </summary>
    event Action<Trade>? OnTradeClosed;
}
