namespace CarrotBacktesting.NET.Abstraction.Strategy;

/// <summary>
/// 定义策略管线接口，支持组合模式。
/// 管线允许将多个策略按顺序（串联）或并行组合在一起，形成复杂的执行流。
/// </summary>
public interface IStrategyPipeline : IStrategy
{
    /// <summary>
    /// 向管线中添加一个子策略或子管线。
    /// </summary>
    /// <param name="strategy">要添加的策略实例。</param>
    /// <returns>返回管线自身，支持链式调用。</returns>
    IStrategyPipeline Add(IStrategy strategy);

    /// <summary>
    /// 对管线进行编译或密封优化。
    /// 在引擎正式运行前调用，用于冻结管线结构并执行可能的性能优化。
    /// </summary>
    /// <returns>返回优化后的管线实例。</returns>
    IStrategyPipeline Compile();

    /// <summary>
    /// 获取管线中的所有子策略。
    /// 实现应支持递归展开嵌套的子管线。
    /// </summary>
    /// <returns>子策略的枚举集合。</returns>
    IEnumerable<IStrategy> GetStrategies();
}
