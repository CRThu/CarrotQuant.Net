using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义策略运行上下文接口。
/// 该接口为策略层提供当前行情数据访问、交易执行（Broker）及宏观状态查询（Market）的统一入口。
/// </summary>
public interface IEngineContext
{
    /// <summary>
    /// 获取当前回测或运行的逻辑时间点（例如交易日或 Bar 的开始时间）。
    /// </summary>
    DateTime CurrentTime { get; }

    /// <summary>
    /// 获取当前循环的索引位置（对应 <see cref="ITimeseriesRegistry"/> 的行索引）。
    /// </summary>
    int CurrentIndex { get; }

    /// <summary>
    /// 获取全局时序注册表，提供稠密时序矩阵与市场维度坐标。
    /// </summary>
    ITimeseriesRegistry Timeseries { get; }

    /// <summary>
    /// 便捷时序访问入口（等价于 <see cref="Timeseries"/>）。
    /// </summary>
    ITimeseriesRegistry Data => Timeseries;

    /// <summary>
    /// 获取事件注册表，用于访问外部异构事件流表（如复权因子、公告、龙虎榜等）。
    /// </summary>
    IEventRegistry Events { get; }

    /// <summary>
    /// 获取经纪商接口，用于执行下单、撤单及查询账户资金持仓。
    /// </summary>
    IBroker Broker { get; }

    /// <summary>
    /// 获取市场状态黑板。宏观策略可通过此接口更新全局结论，微观策略通过此接口获取过滤条件。
    /// </summary>
    IMarketState Market { get; }

    /// <summary>
    /// 允许策略查询引擎内其他已注册的组件或插件。
    /// </summary>
    T? GetExtension<T>() where T : class;
}
