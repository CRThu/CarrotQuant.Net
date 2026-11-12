using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Abstraction.Strategy;

/// <summary>
/// 定义量化交易策略的基础接口。
/// 策略通过监听引擎上下文（IEngineContext）提供的行情、订单和持仓事件来执行交易逻辑。
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// 获取策略的唯一名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取策略执行所需的行情数据列名。
    /// 默认返回空，表示不需要额外数据。
    /// </summary>
    /// <returns>所需列名的集合。</returns>
    IEnumerable<string> GetRequiredColumns() => Array.Empty<string>();

    /// <summary>
    /// 获取策略执行所需依赖的历史时序最大回溯深度（行数，如 200 行均线窗口）。
    /// 供引擎和数据层自动计算非托管内存常驻分块容量。
    /// 默认为 200。
    /// </summary>
    int Lookback => 200;
    
    /// <summary>
    /// 策略初始化回调。在引擎加载策略后、启动前调用，用于配置参数。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    void OnInitial(IEngineContext context) { }

    /// <summary>
    /// 策略启动回调。在引擎正式开始运行循环前调用。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    void OnStart(IEngineContext context) { }
    
    /// <summary>
    /// 策略更新回调。引擎在每个时间步（如每日、每 Bar）会调用此方法。
    /// 这是执行核心信号计算和交易决策的地方。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    void OnUpdate(IEngineContext context); 

    /// <summary>
    /// 策略停止回调。在引擎停止运行或回测结束时调用，用于释放资源或输出最终状态。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    void OnStop(IEngineContext context) { }

    /// <summary>
    /// 订单状态变更回调。当策略提交的订单状态发生变化（例如：提交成功、被拒绝、撤销成功）时由引擎调用。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="orderId">订单 ID。</param>
    /// <param name="status">最新的订单生命周期状态。</param>
    void OnOrderStatusChanged(IEngineContext context, string orderId, OrderStatus status) { }

    /// <summary>
    /// 成交回报回调。当策略提交的订单发生实际物理成交（部分成交或全部成交）时由引擎调用。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="e">成交执行回报详情（包含实际成交价、量、手续费与时间戳）。</param>
    void OnTrade(IEngineContext context, ExecutionReport e) { }

    /// <summary>
    /// 持仓变更回调。当账户中品种的持仓数量发生变化时由引擎调用。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="e">持仓变更事件详情。</param>
    void OnPositionChanged(IEngineContext context, PositionEvent e) { }
}
