using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Scripting;

namespace CarrotBacktesting.NET.Strategy;

/// <summary>
/// 组合策略管线（Composite Strategy Pipeline）抽象基类。
/// 基于组合模式（Composite Pattern）将特征分析、多信号生产、仓位规划与风控报单统一编排为单一标准的 <see cref="IStrategy"/>。
/// 通过 <see cref="Compile"/> 密封优化将执行拓扑固化为不可变数组，消除运行期拓扑变动并促进 JIT 循环边界检查消除（BCE）。
/// 具体执行拓扑由派生类 <see cref="SequentialPipeline"/>（串行）与 <see cref="ParallelPipeline"/>（并行）分别实现。
/// </summary>
public abstract class StrategyPipeline : IStrategyPipeline, IDisposable
{
    private readonly List<IStrategy> _strategies = new();
    private readonly List<AssemblyLoadContext> _dynamicContexts = new();
    private IStrategy[] _compiledStrategies = Array.Empty<IStrategy>();
    private bool _isCompiled = false;
    private int _sealedLookback = 200;

    /// <summary>
    /// 创建串行拓扑组合管线。
    /// </summary>
    public static SequentialPipeline Sequential() => new();

    /// <summary>
    /// 创建并行多核拓扑组合管线。
    /// </summary>
    public static ParallelPipeline Parallel() => new();

    /// <summary>
    /// 获取当前管线已编译固化的不可变子策略数组。
    /// </summary>
    protected IStrategy[] CompiledStrategies => _compiledStrategies;

    /// <summary>
    /// 获取当前管线是否已完成密封编译。
    /// </summary>
    protected bool IsCompiled => _isCompiled;

    /// <inheritdoc />
    public string Name { get; set; } = "StrategyPipeline";

    /// <summary>
    /// 管线所需历史回溯深度（所有子策略 Lookback 的最大值）。
    /// 编译后固化锁定，杜绝运行期二次扩容。
    /// </summary>
    public int Lookback => _isCompiled ? _sealedLookback : CalculateMaxLookback();

    /// <inheritdoc />
    public IStrategyPipeline Add(IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        if (_isCompiled)
        {
            throw new InvalidOperationException("策略管线已被 Compile 密封固化，禁止在运行期动态追加子策略。");
        }

        _strategies.Add(strategy);
        return this;
    }

    /// <summary>
    /// 从 C# 源码文件或代码字符串动态编译并加载策略，热插拔添加至当前管线。
    /// </summary>
    public IStrategyPipeline AddFromSource(string sourceOrFilePath, string? typeName = null, params object[] constructorArgs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceOrFilePath);

        var (strategy, alc) = File.Exists(sourceOrFilePath)
            ? RoslynStrategyCompiler.CompileFile(sourceOrFilePath, typeName, constructorArgs)
            : RoslynStrategyCompiler.CompileSource(sourceOrFilePath, typeName, constructorArgs);

        _dynamicContexts.Add(alc);
        return Add(strategy);
    }

    /// <summary>
    /// 注册由外部（如 YAML 工厂或脚本加载器）动态编译的程序集上下文，纳入当前管线的统一生命周期管理。
    /// </summary>
    /// <param name="alc">可回收程序集加载上下文。</param>
    public void RegisterDynamicContext(AssemblyLoadContext alc)
    {
        ArgumentNullException.ThrowIfNull(alc);
        _dynamicContexts.Add(alc);
    }

    /// <summary>
    /// 对策略管线执行轻量密封优化（Sealing & Freezing）。
    /// 1. 递归编译并密封嵌套的子管线；
    /// 2. 不可变数组固化：消除 .NET JIT 遍历时的数组边界检查（BCE）；
    /// 3. 锁定最大 Lookback 回溯深度；
    /// 4. 锁定拓扑结构，杜绝运行期竞态。
    /// </summary>
    public virtual IStrategyPipeline Compile()
    {
        // 递归预编译嵌套子管线
        foreach (var s in _strategies)
        {
            if (s is IStrategyPipeline childPipeline)
            {
                childPipeline.Compile();
            }
        }

        _sealedLookback = CalculateMaxLookback();
        _compiledStrategies = _strategies.ToArray();
        _isCompiled = true;
        return this;
    }

    /// <inheritdoc />
    public IEnumerable<IStrategy> GetStrategies()
    {
        IEnumerable<IStrategy> source = _isCompiled ? _compiledStrategies : _strategies;
        foreach (var s in source)
        {
            if (s is IStrategyPipeline childPipeline)
            {
                foreach (var child in childPipeline.GetStrategies())
                {
                    yield return child;
                }
            }
            else
            {
                yield return s;
            }
        }
    }

    /// <inheritdoc />
    public virtual void OnStart(IEngineContext context)
    {
        EnsureCompiled();
        for (int i = 0; i < _compiledStrategies.Length; i++)
        {
            _compiledStrategies[i].OnStart(context);
        }
    }

    /// <inheritdoc />
    public abstract void OnUpdate(IEngineContext context);

    /// <inheritdoc />
    public abstract void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events);

    /// <inheritdoc />
    public virtual void OnStop(IEngineContext context)
    {
        EnsureCompiled();
        for (int i = 0; i < _compiledStrategies.Length; i++)
        {
            _compiledStrategies[i].OnStop(context);
        }
    }

    /// <inheritdoc />
    public virtual void OnOrderStatusChanged(IEngineContext context, string orderId, OrderStatus status)
    {
        EnsureCompiled();
        for (int i = 0; i < _compiledStrategies.Length; i++)
        {
            _compiledStrategies[i].OnOrderStatusChanged(context, orderId, status);
        }
    }

    /// <inheritdoc />
    public virtual void OnTrade(IEngineContext context, ExecutionReport e)
    {
        EnsureCompiled();
        for (int i = 0; i < _compiledStrategies.Length; i++)
        {
            _compiledStrategies[i].OnTrade(context, e);
        }
    }

    /// <inheritdoc />
    public virtual void OnPositionChanged(IEngineContext context, PositionEvent e)
    {
        EnsureCompiled();
        for (int i = 0; i < _compiledStrategies.Length; i++)
        {
            _compiledStrategies[i].OnPositionChanged(context, e);
        }
    }

    /// <summary>
    /// 确保管线在执行前已完成密封编译。
    /// </summary>
    protected void EnsureCompiled()
    {
        if (!_isCompiled)
        {
            Compile();
        }
    }

    private int CalculateMaxLookback()
    {
        int max = 0;
        foreach (var s in GetStrategies())
        {
            if (s.Lookback > max) max = s.Lookback;
        }
        return max > 0 ? max : 200;
    }

    /// <summary>
    /// 释放动态 Roslyn 编译加载的上下文资源（CollectibleAssemblyLoadContext）及子策略资源。
    /// </summary>
    public void Dispose()
    {
        // 1. 递归释放实现了 IDisposable 的子策略与嵌套子管线
        foreach (var strategy in _strategies)
        {
            if (strategy is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        // 2. 卸载由当前管线持有的动态编译程序集上下文
        foreach (var alc in _dynamicContexts)
        {
            try
            {
                alc.Unload();
            }
            catch (InvalidOperationException)
            {
                // 忽略已处于卸载状态的上下文异常
            }
        }
        _dynamicContexts.Clear();

        // 3. 清理策略引用集合，解除对已卸载类型的托管强引用以协助 GC 完全回收程序集
        _strategies.Clear();
        _compiledStrategies = Array.Empty<IStrategy>();
    }
}
