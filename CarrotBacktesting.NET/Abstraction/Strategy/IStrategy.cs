using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Abstraction.Strategy;

/// <summary>
/// 定义统一的量化交易策略核心契约。
/// 作为引擎时间步驱动的统领接口，单一入口，通用无界。
/// 原生支持全市场横向截面排序、单标的时序指标追踪、网格加减仓与多空双向调仓。
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// 获取策略唯一名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取策略执行所需依赖的历史时序最大回溯深度（行数，如 200 行均线窗口）。默认为 200。
    /// 供引擎和数据层自动锁定定长环形内存缓冲区容量，杜绝运行期二次扩容。
    /// </summary>
    int Lookback => 200;

    /// <summary>
    /// 策略启动生命周期回调。由引擎在正式运行循环前按拓扑顺序触发一次，用于预热指标缓存与状态初始化。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    void OnStart(IEngineContext context) { }

    /// <summary>
    /// 核心时间步驱动入口。由引擎按主决策时钟步进触发。
    /// 原生全支持：全市场截面排序、单标的时序、网格调仓与多空双向翻转。
    /// </summary>
    /// <param name="context">引擎黑板与运行上下文。</param>
    void OnUpdate(IEngineContext context);

    /// <summary>
    /// 外部异构市场事件（公告、分红除权、停复牌、龙虎榜等）批量推送入口。
    /// 由引擎在对应离散事件时间戳到达时批量推送。缺省为空实现。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="tableName">触发事件的表名（如 "longhubang", "suspension"）。</param>
    /// <param name="events">当前时刻触发的事件行列表。</param>
    void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }

    /// <summary>
    /// 策略终止生命周期回调。由引擎在回测结束或停机时触发，用于资源清理与最终统计。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    void OnStop(IEngineContext context) { }

    /// <summary>
    /// 订单状态跃迁通知。当策略提交的订单状态发生变化（例如：提交成功、被拒绝、撤销成功）时由引擎调用。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="orderId">订单 ID。</param>
    /// <param name="status">最新的订单生命周期状态。</param>
    void OnOrderStatusChanged(IEngineContext context, string orderId, OrderStatus status) { }

    /// <summary>
    /// 订单物理成交回报通知。当策略提交的订单发生实际物理成交时由引擎调用。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="e">成交执行回报详情（包含实际成交价、成交量、手续费与时间戳）。</param>
    void OnTrade(IEngineContext context, ExecutionReport e) { }

    /// <summary>
    /// 账户持仓变化通知。当账户中品种的持仓数量发生变化时由引擎调用。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="e">持仓变更事件详情。</param>
    void OnPositionChanged(IEngineContext context, PositionEvent e) { }
}
