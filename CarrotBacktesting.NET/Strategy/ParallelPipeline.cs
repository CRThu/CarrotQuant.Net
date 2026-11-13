using System.Collections.Generic;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Strategy;

/// <summary>
/// 并行组合策略管线（Parallel Strategy Pipeline）。
/// 继承自 <see cref="StrategyPipeline"/>，在 <see cref="OnUpdate"/> 与 <see cref="OnEvent"/> 时通过多核并发（<see cref="Parallel.For(int, int, System.Action{int})"/>）并行驱动所有子策略。
/// 适用于阶段内相互独立、无时序因果依赖的密集计算（如多因子特征提取、独立 Alpha 信号生产）。
/// 各子策略应向黑板（<see cref="IMarketState"/>）写入带有各自 Key/Tag 隔离的信号状态，确保并发线程安全。
/// </summary>
public class ParallelPipeline : StrategyPipeline
{
    private readonly System.Action<int> _updateAction;
    private readonly System.Action<int> _eventAction;
    private IEngineContext? _currentContext;
    private string? _currentEventTable;
    private IReadOnlyList<IDataRow>? _currentEvents;

    /// <summary>
    /// 是否开启多核并发驱动。默认为 false（默认采用确定性单线程顺序执行，规避小任务调度反噬与便于断点单步调试）。
    /// 仅当显式置为 true 且子策略数大于 1 时，才启用多核并发加速。
    /// </summary>
    public bool Concurrent { get; set; } = false;

    /// <summary>
    /// 初始化 <see cref="ParallelPipeline"/> 实例。
    /// </summary>
    public ParallelPipeline()
    {
        Name = "ParallelPipeline";
        _updateAction = ExecuteUpdateItem;
        _eventAction = ExecuteEventItem;
    }

    /// <summary>
    /// 并行时间步核心驱动入口。
    /// 仅在 Concurrent 为 true 且子策略数大于 1 时启用多核并发驱动；否则采用极速单线程顺序执行。
    /// 热路径采用实例级固定委托复用，实现绝对零堆分配（0 GC Allocations）。
    /// </summary>
    public override void OnUpdate(IEngineContext context)
    {
        EnsureCompiled();
        var strategies = CompiledStrategies;
        if (Concurrent && strategies.Length > 1)
        {
            _currentContext = context;
            try
            {
                System.Threading.Tasks.Parallel.For(0, strategies.Length, _updateAction);
            }
            finally
            {
                _currentContext = null;
            }
        }
        else
        {
            for (int i = 0; i < strategies.Length; i++)
            {
                strategies[i].OnUpdate(context);
            }
        }
    }

    /// <summary>
    /// 外部异构事件并行分发入口。
    /// 仅在 Concurrent 为 true 且子策略数大于 1 时启用多核并发分发；否则采用极速单线程顺序分发。
    /// 热路径采用实例级固定委托复用，实现绝对零堆分配（0 GC Allocations）。
    /// </summary>
    public override void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events)
    {
        EnsureCompiled();
        var strategies = CompiledStrategies;
        if (Concurrent && strategies.Length > 1)
        {
            _currentContext = context;
            _currentEventTable = tableName;
            _currentEvents = events;
            try
            {
                System.Threading.Tasks.Parallel.For(0, strategies.Length, _eventAction);
            }
            finally
            {
                _currentContext = null;
                _currentEventTable = null;
                _currentEvents = null;
            }
        }
        else
        {
            for (int i = 0; i < strategies.Length; i++)
            {
                strategies[i].OnEvent(context, tableName, events);
            }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteUpdateItem(int index)
    {
        CompiledStrategies[index].OnUpdate(_currentContext!);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteEventItem(int index)
    {
        CompiledStrategies[index].OnEvent(_currentContext!, _currentEventTable!, _currentEvents!);
    }
}
