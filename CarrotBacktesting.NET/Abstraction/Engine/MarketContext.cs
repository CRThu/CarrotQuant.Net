namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 定义市场偏向，用于描述当前宏观市场的整体情绪或活跃度。
/// </summary>
public enum MarketBias
{
    /// <summary>
    /// 看多、市场活跃或处于行情高位。
    /// </summary>
    Up,

    /// <summary>
    /// 中性。
    /// </summary>
    Neutral,

    /// <summary>
    /// 看空、市场低迷或处于行情低点。
    /// </summary>
    Down
}

/// <summary>
/// 定义宏观决策的“信封”基类。
/// 引擎通过该对象读取影响框架执行逻辑的核心控制字段。
/// </summary>
public abstract class MarketContext
{
    /// <summary>
    /// 获取或设置一个值，指示引擎是否应自动跳过后续的 Alpha 信号计算。
    /// 当该值为 true 时，通常表示宏观风险极高，触发系统级熔断。
    /// </summary>
    public bool SkipAlpha { get; set; } = false;

    /// <summary>
    /// 获取或设置当前的市场偏向。
    /// </summary>
    public MarketBias Bias { get; set; } = MarketBias.Neutral;
}

/// <summary>
/// 定义携带强类型自定义业务状态的宏观决策上下文。
/// </summary>
/// <typeparam name="T">自定义状态对象的类型。该类型应为 class。</typeparam>
public class MarketContext<T> : MarketContext where T : class
{
    /// <summary>
    /// 获取或设置由策略生成的自定义业务状态数据。
    /// </summary>
    public T State { get; set; } = default!;
}

/// <summary>
/// 定义市场状态黑板（Blackboard）接口。
/// 用于不同策略（如宏观策略与个股策略）之间进行异步的数据交换。
/// </summary>
public interface IMarketState
{
    /// <summary>
    /// 获取当前记录的宏观决策状态。
    /// </summary>
    /// <typeparam name="T">要获取的状态对象类型。</typeparam>
    /// <returns>若存在指定类型的状态则返回；否则返回 null。</returns>
    T? GetState<T>() where T : class;
    
    /// <summary>
    /// 更新当前的市场决策状态。该方法通常仅由引擎在内部调用。
    /// </summary>
    /// <param name="result">最新的市场决策快照。</param>
    void UpdateState(MarketContext result);
}
