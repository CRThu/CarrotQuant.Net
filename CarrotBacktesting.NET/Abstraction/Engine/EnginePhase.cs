namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 引擎时间步内部推进的微观时序相位（单一事实来源 SSOT）。
/// </summary>
public enum EnginePhase : byte
{
    /// <summary>
    /// 盘前准备阶段：跨日持仓解冻 (T+1 释放)、资金对账与环境重置。当日所有行情尚未产生。
    /// </summary>
    PreMarket = 1,

    /// <summary>
    /// 开盘阶段：执行 Bar 初始切片撮合（仅依据 bar.Open）、可选触发 Open 相位策略决策。
    /// </summary>
    Open = 2,

    /// <summary>
    /// 盘中阶段：离散异构事件到达推送 (OnEvent)、盘中条件单判定。
    /// </summary>
    Intraday = 3,

    /// <summary>
    /// 收盘阶段：当期 OHLCV 数据完整就绪、指标增量步进更新、默认触发 Close 相位策略决策、执行收盘撮合。
    /// </summary>
    Close = 4,

    /// <summary>
    /// 盘后结算阶段：记录日度资产与持仓净值快照、在途挂单有效性检查与日终归档。
    /// </summary>
    PostMarket = 5
}
