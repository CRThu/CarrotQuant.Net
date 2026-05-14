namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 市场偏向枚举
/// </summary>
public enum MarketBias
{
    Up,      // 看多 / 活跃
    Neutral, // 中性
    Down     // 看空 / 冰点
}

/// <summary>
/// 宏观决策的“信封”。引擎通过这个信封读取风险控制信息。
/// </summary>
public abstract class MarketContext
{
    // 引擎必须读到的字段，用于执行框架级逻辑
    public bool SkipAlpha { get; set; } = false;

    /// <summary>
    /// 市场偏向
    /// </summary>
    public MarketBias Bias { get; set; } = MarketBias.Neutral;
}

/// <summary>
/// 宏观决策的“信件内容”。策略层通过这个泛型类携带业务状态。
/// </summary>
public class MarketContext<T> : MarketContext where T : class
{
    public T State { get; set; } = default!;
}

/// <summary>
/// 市场状态黑板，用于策略间交换宏观分析结果。
/// </summary>
public interface IMarketState
{
    /// <summary>
    /// 获取当前记录的宏观决策结果
    /// </summary>
    T? GetState<T>() where T : class;
    
    /// <summary>
    /// 内部设置方法 (只允许 Engine 调用)
    /// </summary>
    void UpdateState(MarketContext result);
}
