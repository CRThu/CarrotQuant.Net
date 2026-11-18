using System;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Indicators;

namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义策略运行上下文接口。
/// 该接口为策略层提供当前行情数据访问、交易执行（Broker）、指标计算（Indicators）及宏观状态查询（Market）的统一入口。
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
    /// 获取事件注册表，用于访问外部异构事件流表（如复权因子、公告、龙虎榜等）。
    /// </summary>
    IEventRegistry Events { get; }

    /// <summary>
    /// 获取字段与标的静态属性注册表，提供时序指标元数据自省与静态维表（如分类、标签、属性）的双向索引查询。
    /// </summary>
    IFieldRegistry Fields { get; }

    /// <summary>
    /// 获取经纪商接口，用于执行下单、撤单及查询账户资金持仓。
    /// </summary>
    IBroker Broker { get; }

    /// <summary>
    /// 获取交易事实记录器接口（可选），用于沉淀策略信号打点或扩展归因。
    /// </summary>
    ITradingRecorder? Recorder { get; }

    /// <summary>
    /// 便捷记录策略决策信号打点数据。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="signalValue">信号值（如打分、概率或目标权重）。</param>
    /// <param name="signalGroup">信号分组。</param>
    /// <param name="reason">信号触发理由。</param>
    void RecordSignal(string symbol, double signalValue, string? signalGroup = null, string? reason = null);

    /// <summary>
    /// 获取市场状态黑板。宏观策略可通过此接口更新全局结论，微观策略通过此接口获取过滤条件。
    /// </summary>
    IMarketState Market { get; }

    /// <summary>
    /// 获取全局指标注册中心，统筹增量流式指标池的注册、多策略共享与引擎步进泵送。
    /// </summary>
    IIndicatorRegistry Indicators { get; }

    #region 当期账户与持仓权威门面 (Account & Position SSOT Facades)

    /// <summary>
    /// 获取指定标的当前总持仓量（多头为正，空头为负，无持仓为 0）。
    /// 作为策略判断单一事实来源（SSOT），严禁在策略内部私存或自行推算仓位。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <returns>物理总持仓数量。</returns>
    decimal GetPosition(string symbol);

    /// <summary>
    /// 获取指定标的当前可用持仓量（已剔除挂单冻结及 T+N 在途锁定，多头为正，空头为负）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <returns>可用持仓数量。</returns>
    decimal GetAvailablePosition(string symbol);

    /// <summary>
    /// 获取当前账户可用现金余额。
    /// </summary>
    double AvailableCash { get; }

    /// <summary>
    /// 获取当前账户动态盯市总资产。
    /// </summary>
    double TotalEquity { get; }

    #endregion

    #region 当期极简取价门面 (Unified Current Bar Market Data Facades)

    /// <summary>
    /// 获取指定标的的当期收盘价（默认提供平滑后复权价）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetClose(string symbol, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引的当期收盘价（热路径零字典哈希）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetClose(int symbolIndex, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的的当期开盘价（默认提供平滑后复权价）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetOpen(string symbol, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引的当期开盘价（热路径零字典哈希）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetOpen(int symbolIndex, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的的当期最高价（默认提供平滑后复权价）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetHigh(string symbol, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引的当期最高价（热路径零字典哈希）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetHigh(int symbolIndex, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的的当期最低价（默认提供平滑后复权价）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetLow(string symbol, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引的当期最低价（热路径零字典哈希）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetLow(int symbolIndex, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的的当期成交量（默认保持物理量 Raw）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="mode">价格模式（成交量默认保持 Raw）。</param>
    double GetVolume(string symbol, PriceMode mode = PriceMode.Raw);

    /// <summary>
    /// 获取指定标的列索引的当期成交量（热路径零字典哈希）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="mode">价格模式（成交量默认保持 Raw）。</param>
    double GetVolume(int symbolIndex, PriceMode mode = PriceMode.Raw);

    /// <summary>
    /// 获取指定标的当期任意时序字段值。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="fieldName">字段名（如 "vwap", "turnover", "settlement" 等）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetField(string symbol, string fieldName, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引当期任意时序字段值（热路径零字典哈希）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="fieldName">字段名。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetField(int symbolIndex, string fieldName, PriceMode mode = PriceMode.Post);

    #endregion

    /// <summary>
    /// 允许策略查询引擎内其他已注册的组件或插件。
    /// </summary>
    T? GetExtension<T>() where T : class;
}
