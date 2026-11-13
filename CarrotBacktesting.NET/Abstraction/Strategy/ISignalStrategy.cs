namespace CarrotBacktesting.NET.Abstraction.Strategy;

/// <summary>
/// 纯信号/因子策略语义标记契约。
/// 继承自 <see cref="IStrategy"/>，专注于信号生产（Alpha Generation 与特征分析），
/// 通过运行上下文黑板（context.Market）向后置模块发布打分、概率分布或状态向量。
/// 严格解耦物理交易逻辑，不直接触碰报单与账户接口，支持纯信号学术打点、胜率与 IC/IR 零损耗评估。
/// </summary>
public interface ISignalStrategy : IStrategy
{
}
