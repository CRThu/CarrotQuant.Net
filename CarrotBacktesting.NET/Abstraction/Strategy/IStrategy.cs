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
    /// 订单状态变更回调。当策略提交的订单状态发生变化或产生执行回报时由引擎调用。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="e">订单执行回报详情。</param>
    void OnOrderChanged(IEngineContext context, ExecutionReport e) { }

    /// <summary>
    /// 持仓变更回调。当账户中品种的持仓数量发生变化时由引擎调用。
    /// </summary>
    /// <param name="context">引擎运行上下文。</param>
    /// <param name="e">持仓变更事件详情。</param>
    void OnPositionChanged(IEngineContext context, PositionEvent e) { }
}
