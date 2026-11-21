using System;
using System.Threading;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Strategy;

namespace CarrotBacktesting.NET;

/// <summary>
/// 回测执行会话门面（BacktestSession）。
/// 协调配置解析、数据与经纪商装配、回测时间步推进以及绩效分析导出全流程。
/// </summary>
public sealed class BacktestSession : IDisposable
{
    private bool _disposed = false;

    /// <summary>
    /// 获取当前会话的全局配置根。
    /// </summary>
    public SessionConfig Config { get; }

    /// <summary>
    /// 获取当前会话底层运行的回测引擎实例。
    /// </summary>
    public BacktestingEngine Engine { get; }

    /// <summary>
    /// 获取回测完成后的不可变交易事实账本（在调用 <see cref="Run"/> 或 <see cref="RunAsync"/> 之前为 null）。
    /// </summary>
    public TradingResult? Result { get; private set; }

    /// <summary>
    /// 获取绩效归因分析报告（在调用 <see cref="Analyze(bool)"/> 之前为 null）。
    /// </summary>
    public AnalysisReport? Report { get; private set; }

    /// <summary>
    /// 创建独立自描述的回测会话构建器（<see cref="BacktestSessionBuilder"/>），为纯代码回测提供极简链式组装门面。
    /// </summary>
    public static BacktestSessionBuilder CreateBuilder() => new();

    /// <summary>
    /// 默认构建器入口：创建流畅回测会话构建器（等价于 <see cref="CreateBuilder"/>）。
    /// </summary>
    public static BacktestSessionBuilder Create() => new();

    /// <summary>
    /// 基于 YAML 配置文件直接执行回测流程并输出量化分析报告。
    /// </summary>
    /// <param name="configPath">会话 YAML 配置文件物理路径。</param>
    /// <param name="export">是否按配置自动导出分析报表，默认为 true。</param>
    /// <returns>量化绩效分析报告。</returns>
    public static AnalysisReport Run(string configPath, bool export = true)
    {
        using var session = Load(configPath);
        return session.RunAndAnalyze(export);
    }

    /// <summary>
    /// 从 YAML 配置文件创建回测会话实例，支持注入自定义策略实例替换配置中的策略管线。
    /// </summary>
    /// <param name="configPath">会话 YAML 配置文件物理路径。</param>
    /// <param name="strategy">自定义策略实例（可选）。</param>
    /// <returns>已装配完成的会话实例。</returns>
    public static BacktestSession Load(string configPath, IStrategy? strategy = null)
    {
        var config = SessionConfig.Load(configPath);
        return FromConfig(config, strategy);
    }

    /// <summary>
    /// 基于内存中的 <see cref="SessionConfig"/> 实例创建回测会话。
    /// </summary>
    /// <param name="config">强类型全局会话配置。</param>
    /// <param name="strategy">用户自定义实现的 C# 策略实例（可选）。</param>
    /// <returns>已装配就绪的会话实例。</returns>
    public static BacktestSession FromConfig(SessionConfig config, IStrategy? strategy = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        IStrategyPipeline? pipeline = null;
        if (strategy != null)
        {
            pipeline = strategy is IStrategyPipeline p
                ? p
                : StrategyPipeline.Sequential().Add(strategy);
        }

        var engine = BacktestingEngine.Create(config, pipeline: pipeline);
        return new BacktestSession(config, engine);
    }

    /// <summary>
    /// 显式构造函数，绑定配置模型与底层引擎内核。
    /// </summary>
    public BacktestSession(SessionConfig config, BacktestingEngine engine)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// 同步执行回测主时钟循环，推进所有时间步并沉淀交易事实账本。
    /// </summary>
    /// <returns>不可变交易事实数据集。</returns>
    public TradingResult Run()
    {
        Engine.Run();
        Result = Engine.GetResult();
        return Result;
    }

    /// <summary>
    /// 异步执行回测主时钟循环。
    /// </summary>
    public async Task<TradingResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await Engine.RunAsync(cancellationToken).ConfigureAwait(false);
        Result = Engine.GetResult();
        return Result;
    }

    /// <summary>
    /// 对交易事实执行量化指标分析，并根据配置导出报表。
    /// </summary>
    /// <param name="export">是否按配置自动导出分析报表，默认为 true。</param>
    /// <returns>量化绩效分析报告。</returns>
    public AnalysisReport Analyze(bool export = true)
    {
        if (Result == null)
        {
            throw new InvalidOperationException("必须先执行回测（调用 Run() 或 RunAsync()）沉淀出交易事实后方可执行分析。");
        }

        var analysisEngine = AnalysisEngineFactory.Create(Config.Analysis);
        if (export)
        {
            Report = analysisEngine.AnalyzeAndExport(Result, Config.Analysis, Engine.Context.Timeseries);
        }
        else
        {
            Report = analysisEngine.Analyze(Result, Config.Analysis, Engine.Context.Timeseries);
        }

        return Report;
    }

    /// <summary>
    /// 依次执行回测循环与绩效分析。
    /// </summary>
    /// <param name="export">是否按配置自动导出报表，默认为 true。</param>
    /// <returns>量化绩效分析报告。</returns>
    public AnalysisReport RunAndAnalyze(bool export = true)
    {
        Run();
        return Analyze(export);
    }

    /// <summary>
    /// 异步执行回测循环与绩效分析。
    /// </summary>
    public async Task<AnalysisReport> RunAndAnalyzeAsync(bool export = true, CancellationToken cancellationToken = default)
    {
        await RunAsync(cancellationToken).ConfigureAwait(false);
        return Analyze(export);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            Engine.Dispose();
            _disposed = true;
        }
    }
}
