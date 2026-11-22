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
    MarketTime CurrentTime { get; }

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
    /// 获取当前加载的全市场标的代码只读列表（固定顺序，与列索引一一对应）。
    /// 作为高频访问门面直通 <see cref="ITimeseriesRegistry.Metadata"/>，消除多层穿透。
    /// </summary>
    IReadOnlyList<string> Symbols => Timeseries.Metadata.Symbols;

    /// <summary>
    /// 获取当前市场标的总数量。
    /// </summary>
    int SymbolCount => Timeseries.Metadata.Symbols.Count;

    /// <summary>
    /// 获取指定股票代码对应的全局列索引。
    /// </summary>
    /// <param name="symbol">股票代码。</param>
    /// <returns>列索引，若不存在则返回 -1。</returns>
    int GetSymbolIndex(string symbol) => Timeseries.Metadata.GetSymbolIndex(symbol);

    /// <summary>
    /// 便捷提交买入订单（默认以市价单形式发出）。
    /// <para>注意：若传入限价价格，价格口径自动锚定策略当期观测坐标系（后复权价），门面内部会自动逆折算为底层物理真实价格报送撮合网关与账本。</para>
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="quantity">买入数量（股数/张数）。</param>
    /// <param name="price">委托价格（市价单缺省为 0.0）。</param>
    /// <param name="type">订单类型（缺省为市价单）。</param>
    /// <param name="orderTag">自定义订单业务标签（可选）。</param>
    /// <param name="timeInForce">订单时效性（可选，缺省由配置 DefaultTimeInForce 确定）。</param>
    /// <param name="reduceOnly">仅减仓/纯平仓标志（可选，缺省为 false）。若为 true，该订单物理上只能减少现有空头持仓，严禁开多。</param>
    /// <returns>分配的唯一订单标识 ID。</returns>
    long Buy(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string orderTag = "", TimeInForce? timeInForce = null, bool reduceOnly = false)
        => Broker.Buy(symbol, quantity, price, type, orderTag, timeInForce, reduceOnly);

    /// <summary>
    /// 便捷提交卖出订单（默认以市价单形式发出）。
    /// <para>注意：若传入限价价格，价格口径自动锚定策略当期观测坐标系（后复权价），门面内部会自动逆折算为底层物理真实价格报送撮合网关与账本。</para>
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="quantity">卖出数量（股数/张数）。</param>
    /// <param name="price">委托价格（市价单缺省为 0.0）。</param>
    /// <param name="type">订单类型（缺省为市价单）。</param>
    /// <param name="orderTag">自定义订单业务标签（可选）。</param>
    /// <param name="timeInForce">订单时效性（可选，缺省由配置 DefaultTimeInForce 确定）。</param>
    /// <param name="reduceOnly">仅减仓/纯平仓标志（可选，缺省为 false）。若为 true，该订单物理上只能减少现有多头持仓，严禁开空。</param>
    /// <returns>分配的唯一订单标识 ID。</returns>
    long Sell(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string orderTag = "", TimeInForce? timeInForce = null, bool reduceOnly = false)
        => Broker.Sell(symbol, quantity, price, type, orderTag, timeInForce, reduceOnly);

    /// <summary>
    /// 便捷全额平仓指定标的持仓。
    /// 若当前无持仓则返回 0，不产生任何订单。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="price">委托价格（市价单缺省为 0.0）。</param>
    /// <param name="type">订单类型（缺省为市价单）。</param>
    /// <param name="orderTag">自定义订单业务标签（可选）。</param>
    /// <param name="timeInForce">订单时效性（可选，缺省由配置 DefaultTimeInForce 确定）。</param>
    /// <returns>分配的唯一订单标识 ID；若无持仓则返回 0。</returns>
    long ClosePosition(string symbol, double price = 0.0, OrderType type = OrderType.Market, string orderTag = "", TimeInForce? timeInForce = null)
        => Broker.ClosePosition(symbol, price, type, orderTag, timeInForce);

    /// <summary>
    /// 便捷提交止损订单（默认以市价单形式发出，默认 TIF 为 GoodTilCanceled 跨日有效）。
    /// </summary>
    long StopLoss(string symbol, double stopPrice, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
        => Broker.StopLoss(symbol, stopPrice, quantity, orderTag, timeInForce);

    /// <summary>
    /// 便捷提交止盈订单（默认以市价单形式发出，默认 TIF 为 GoodTilCanceled 跨日有效）。
    /// </summary>
    long TakeProfit(string symbol, double targetPrice, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
        => Broker.TakeProfit(symbol, targetPrice, quantity, orderTag, timeInForce);

    /// <summary>
    /// 便捷提交成对互斥括号订单（Bracket / OCO）：同时挂出止损单与止盈单，单边触发后联动注销另一方。
    /// </summary>
    (long StopOrderId, long TakeProfitOrderId) Bracket(
        string symbol,
        decimal quantity,
        double stopPrice,
        double takeProfitPrice,
        string orderTag = "",
        TimeInForce? timeInForce = null,
        OrderDirection? closeDirection = null)
        => Broker.Bracket(symbol, quantity, stopPrice, takeProfitPrice, orderTag, timeInForce, closeDirection);

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
    /// 便捷门面：向市场状态黑板写入指定 Key 的状态（自动注入当期逻辑仿真时间戳 <see cref="CurrentTime"/>）。
    /// </summary>
    void SetState<T>(string key, T state) => Market.SetState(key, state, CurrentTime);

    /// <summary>
    /// 便捷门面：从市场状态黑板读取指定 Key 的强类型状态。
    /// </summary>
    T? GetState<T>(string key) => Market.GetState<T>(key);

    /// <summary>
    /// 便捷门面：判断市场状态黑板是否存在指定 Key 且匹配类型 T 的状态。
    /// </summary>
    bool HasState<T>(string key) => Market.HasState<T>(key);

    /// <summary>
    /// 便捷门面：精确清除市场状态黑板中指定 Key 对应的强类型状态。
    /// </summary>
    bool ClearState<T>(string key) => Market.ClearState<T>(key);

    /// <summary>
    /// 便捷门面：按指定 Key 原子获取并移除指定状态（先 key 后 out，读后即焚），并输出最后更新逻辑时间戳（若不存在或未设置则为 null）。
    /// </summary>
    bool TryTakeState<T>(string key, out T? state, out MarketTime? updatedTime) => Market.TryTakeState<T>(key, out state, out updatedTime);

    /// <summary>
    /// 获取全局指标注册中心，统筹增量流式指标池的注册、多策略共享与引擎步进泵送。
    /// </summary>
    IIndicatorRegistry Indicators { get; }

    #region 当期账户与持仓查询门面 (Account & Position Facades)

    /// <summary>
    /// 获取指定标的当前总持仓量（多头为正，空头为负，无持仓为 0）。
    /// 反映底层账本记录的物理实际头寸。
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
    /// 获取指定标的的加权平均持仓成本价（开仓总金额除以总股数，不含手续费；无持仓时返回 0.0）。
    /// 供策略端计算持仓浮动盈亏与动态止盈止损条件。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <returns>标的加权持仓成本价。</returns>
    double GetPositionCost(string symbol);

    /// <summary>
    /// 获取当前账户可用现金余额。
    /// </summary>
    double AvailableCash { get; }

    /// <summary>
    /// 获取当前账户动态盯市总资产。
    /// </summary>
    double TotalEquity { get; }

    #endregion

    #region 时序与复权取价门面 (Unified Market Data Facades with Negative Offset)

    /// <summary>
    /// 获取指定标的的收盘价（支持时序负向回溯与平滑复权）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetClose(string symbol, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引的收盘价（基于标的列索引直接访问）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetClose(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的的开盘价（支持时序负向回溯与平滑复权）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetOpen(string symbol, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引的开盘价（基于标的列索引直接访问）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetOpen(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的的最高价（支持时序负向回溯与平滑复权）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetHigh(string symbol, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引的最高价（基于标的列索引直接访问）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetHigh(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的的最低价（支持时序负向回溯与平滑复权）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetLow(string symbol, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引的最低价（基于标的列索引直接访问）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetLow(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的的成交量（默认保持物理量 Raw）。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（成交量默认保持 Raw）。</param>
    double GetVolume(string symbol, int offset = 0, PriceMode mode = PriceMode.Raw);

    /// <summary>
    /// 获取指定标的列索引的成交量（基于标的列索引直接访问）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（成交量默认保持 Raw）。</param>
    double GetVolume(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Raw);

    /// <summary>
    /// 获取指定标的任意时序字段值。
    /// </summary>
    /// <param name="symbol">标的代码。</param>
    /// <param name="fieldName">字段名（如 "vwap", "turnover", "settlement" 等）。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetField(string symbol, string fieldName, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取指定标的列索引任意时序字段值（基于标的列索引直接访问）。
    /// </summary>
    /// <param name="symbolIndex">标的全局列索引。</param>
    /// <param name="fieldName">字段名。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。取值必须小于等于 0；若大于 0 则抛出 ArgumentOutOfRangeException 以防止前瞻偏差（Look-ahead Bias）。</param>
    /// <param name="mode">价格模式（默认 Post 后复权）。</param>
    double GetField(int symbolIndex, string fieldName, int offset = 0, PriceMode mode = PriceMode.Post);

    /// <summary>
    /// 获取全市场当前时间步有效复权因子切片引用（纯连续内存，零堆分配）。
    /// </summary>
    ReadOnlySpan<double> DailyFactors => ReadOnlySpan<double>.Empty;

    /// <summary>
    /// 判断指定字段是否为价格类字段（如 open, high, low, close 等），以决定在 Post 模式下是否需要乘复权因子。
    /// 由权威上下文依据 PriceFields 配置判定，严禁在此假实现中硬编码。
    /// </summary>
    /// <param name="fieldName">字段名称。</param>
    /// <returns>若为价格类字段则返回 true，否则返回 false。</returns>
    bool IsPriceField(string fieldName) => false;

    /// <summary>
    /// 获取指定时序字段在当前时间步（含可选历史偏移）的全市场标的只读行切片（纯连续内存，零堆分配）。
    /// 供指标批量计算与截面因子提取等热路径直接流式消费，消灭标的循环体内的字符串比对与二维寻址开销。
    /// </summary>
    /// <param name="fieldName">字段名称（如 "close", "open", "volume", "turnover"）。</param>
    /// <param name="offset">时序偏移量（0 为当期，负数表示历史回溯步长）。</param>
    /// <param name="mode">价格复权模式（默认 Post 后复权；若非价格字段或指定 Raw 则直接返回物理真实值切片）。</param>
    /// <returns>全市场标的数据的只读切片；若字段不存在或处于无效时间步则返回空切片。</returns>
    ReadOnlySpan<double> GetFieldRow(string fieldName, int offset = 0, PriceMode mode = PriceMode.Post) => ReadOnlySpan<double>.Empty;

    #endregion

    /// <summary>
    /// 允许策略查询引擎内其他已注册的组件或插件。
    /// </summary>
    T? GetExtension<T>() where T : class;
}
