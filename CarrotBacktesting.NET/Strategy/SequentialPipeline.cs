using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Strategy;

/// <summary>
/// 串行组合策略管线（Sequential Strategy Pipeline）。
/// 继承自 <see cref="StrategyPipeline"/>，严格按照子策略添加顺序依次执行 <see cref="OnUpdate"/> 与 <see cref="OnEvent"/>。
/// 适用于具备前后因果依赖的管线阶段（如特征分析 -> 信号生产 -> 仓位规划 -> 风控报单）。
/// </summary>
public class SequentialPipeline : StrategyPipeline
{
    /// <summary>
    /// 初始化 <see cref="SequentialPipeline"/> 实例。
    /// </summary>
    public SequentialPipeline()
    {
        Name = "SequentialPipeline";
    }

    /// <summary>
    /// 串行时间步核心驱动入口。严格按序依次执行各子策略。
    /// </summary>
    public override void OnUpdate(IEngineContext context)
    {
        EnsureCompiled();
        var strategies = CompiledStrategies;
        for (int i = 0; i < strategies.Length; i++)
        {
            strategies[i].OnUpdate(context);
        }
    }

    /// <summary>
    /// 外部异构事件串行分发入口。严格按序依次向各子策略分发事件。
    /// </summary>
    public override void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events)
    {
        EnsureCompiled();
        var strategies = CompiledStrategies;
        for (int i = 0; i < strategies.Length; i++)
        {
            strategies[i].OnEvent(context, tableName, events);
        }
    }
}
