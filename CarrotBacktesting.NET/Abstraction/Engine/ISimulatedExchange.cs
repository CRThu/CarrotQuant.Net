using System;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 仅供回测仿真交易所实现的专有驱动契约。
/// 负责在回测离散时间步推进时，根据最新行情批量撮合挂单簿中的未成交限价单。
/// 实盘网关（如 CtpGateway）严禁实现此接口。
/// </summary>
public interface ISimulatedExchange : IExchangeGateway
{
    /// <summary>
    /// 获取指定标的当前时间步的行情切片（包含 Open, High, Low, Close, Volume 等）。
    /// 若当前无可用行情或标的停牌，返回 BarSnapshot.Empty。
    /// </summary>
    /// <param name="symbol">交易标的代码。</param>
    /// <returns>不可变行情切片。</returns>
    BarSnapshot GetBar(string symbol);

    /// <summary>
    /// 设置当前仿真交易所的物理时间戳（由外部引擎或经纪商在时间步驱动时同步）。
    /// </summary>
    /// <param name="currentTime">当前全局物理时间戳。</param>
    void SetCurrentTime(MarketTime currentTime);

    /// <summary>
    /// 回测时间步时钟推进。
    /// 根据当前时间步的最新行情数据，驱动内部挂单簿批量撮合判定。
    /// </summary>
    /// <param name="currentTime">当前时间步的全局物理时间戳。</param>
    /// <param name="timeseries">全局时序注册表，用于获取当前时间步的标的价格与行情数据。</param>
    /// <param name="currentIndex">当前时间步在时序注册表中的行索引。</param>
    void Step(MarketTime currentTime, ITimeseriesRegistry timeseries, int currentIndex);

    /// <summary>
    /// 反应式行情数据切片到达驱动（唯一权威行情灌入入口）。
    /// 纯栈/寄存器传递 48 字节不可变行情切片，零堆分配、零 GC 开销。
    /// </summary>
    /// <param name="symbol">交易标的代码。</param>
    /// <param name="time">行情发生的时间戳。</param>
    /// <param name="snapshot">不可变行情切片。</param>
    void OnMarketData(string symbol, MarketTime time, in BarSnapshot snapshot);

    /// <summary>
    /// 在回测时间步终止时，清理撤销挂单簿中未成交的当日到期（Day）订单与市价单，保留跨日 GTC 挂单以支持检查点状态持久化与断点续跑。
    /// </summary>
    /// <param name="terminalTime">终局物理时间戳。</param>
    void CloseGateway(MarketTime terminalTime) { }
}
