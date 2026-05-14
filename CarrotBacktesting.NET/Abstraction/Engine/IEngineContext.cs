using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 策略运行上下文。
/// 提供当前行情数据访问、交易接口及市场状态查询。
/// </summary>
public interface IEngineContext
{
    /// <summary>
    /// 当前的时间点（交易日/Bar）。
    /// </summary>
    DateTime CurrentTime { get; }

    /// <summary>
    /// 当前循环的索引（对应 IDataProvider 行索引）。
    /// </summary>
    int CurrentIndex { get; }

    /// <summary>
    /// 获取当前的数据提供器，允许策略在循环内查询任意历史数据。
    /// </summary>
    IDataProvider Data { get; }

    /// <summary>
    /// 账户经纪商接口，用于下单、查询持仓。
    /// </summary>
    IBroker Broker { get; }

    /// <summary>
    /// 市场黑板：策略间通过此对象交换数据。
    /// 宏观策略通过它 Update，微观策略通过它 Get。
    /// </summary>
    IMarketState Market { get; }
}
