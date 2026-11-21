using System;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Strategy;
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Engine;
using CarrotBacktesting.NET.Strategy;

namespace CarrotBacktesting.NET;

/// <summary>
/// 回测执行会话构建器（BacktestSessionBuilder）。
/// 遵循 Fluent API 设计范式，为纯 C# 脚本、交互式单测与策略调参提供极简链式组装门面，内置工业级稳健默认配置。
/// 支持全流程纯内存/无磁盘文件的高性能回测组装与一键启动。
/// </summary>
public sealed class BacktestSessionBuilder
{
    private readonly SessionConfig _config = new();
    private IStrategy? _strategy;
    private ITimeseriesRegistry? _customTimeseries;

    /// <summary>
    /// 配置回测底层数据源根目录与起止时间区间。
    /// </summary>
    /// <param name="storageRoot">物理数据根目录。</param>
    /// <param name="startTime">回测起始时间（支持 yyyy-MM-dd [HH:mm:ss] 格式）。</param>
    /// <param name="endTime">回测终止时间（支持 yyyy-MM-dd [HH:mm:ss] 格式）。</param>
    public BacktestSessionBuilder WithData(string storageRoot, string startTime, string endTime)
    {
        _config.Data.Root = storageRoot;
        _config.Engine.StartDate = MarketTime.Parse(startTime);
        _config.Engine.EndDate = MarketTime.Parse(endTime);
        return this;
    }

    /// <summary>
    /// 配置回测底层数据源根目录与起止时间区间。
    /// </summary>
    public BacktestSessionBuilder WithData(string storageRoot, MarketTime startTime, MarketTime endTime)
    {
        _config.Data.Root = storageRoot;
        _config.Engine.StartDate = startTime;
        _config.Engine.EndDate = endTime;
        return this;
    }

    /// <summary>
    /// 配置回测底层数据源根目录。
    /// </summary>
    public BacktestSessionBuilder WithStorageRoot(string storageRoot)
    {
        _config.Data.Root = storageRoot;
        return this;
    }

    /// <summary>
    /// 配置回测起止时间。
    /// </summary>
    public BacktestSessionBuilder WithPeriod(string startTime, string endTime)
    {
        _config.Engine.StartDate = MarketTime.Parse(startTime);
        _config.Engine.EndDate = MarketTime.Parse(endTime);
        return this;
    }

    /// <summary>
    /// 配置回测起止时间。
    /// </summary>
    public BacktestSessionBuilder WithPeriod(MarketTime startTime, MarketTime endTime)
    {
        _config.Engine.StartDate = startTime;
        _config.Engine.EndDate = endTime;
        return this;
    }

    /// <summary>
    /// 配置虚拟经纪商资金、做空权限与交易成本摩擦参数。
    /// </summary>
    /// <param name="initialCash">初始可用现金，默认为 100,000 元。</param>
    /// <param name="allowShort">是否允许融券做空，默认为 false。</param>
    /// <param name="feeRate">双边佣金费率，默认为万三（0.0003）。</param>
    /// <param name="minFee">单笔最低佣金门槛，默认为 5 元。</param>
    /// <param name="slippage">成交固定滑点，默认为 0.0。</param>
    /// <param name="settleDays">资金与持仓结算周期（A股为 T+1，美股/加密为 T+0），默认为 1。</param>
    public BacktestSessionBuilder WithBroker(
        double initialCash = 100_000.0,
        bool allowShort = false,
        double feeRate = 0.0003,
        double minFee = 5.0,
        double slippage = 0.0,
        int settleDays = 1)
    {
        _config.Broker.InitialCash = initialCash;
        _config.Broker.AllowShort = allowShort;
        _config.Broker.FeeRate = feeRate;
        _config.Broker.MinFee = minFee;
        _config.Broker.Slippage = slippage;
        _config.Broker.SettleDays = settleDays;
        return this;
    }

    /// <summary>
    /// 快捷配置初始现金与做空权限。
    /// </summary>
    public BacktestSessionBuilder WithCash(double initialCash, bool allowShort = false)
    {
        _config.Broker.InitialCash = initialCash;
        _config.Broker.AllowShort = allowShort;
        return this;
    }

    /// <summary>
    /// 绑定待执行的单策略或复合策略管线实例。
    /// </summary>
    public BacktestSessionBuilder WithStrategy(IStrategy strategy)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        return this;
    }

    /// <summary>
    /// 显式挂载预初始化的纯内存时序行情注册中心（用于单元测试、内存蒙特卡洛仿真或定制数据源，彻底免除磁盘文件与 Hive 分区依赖）。
    /// </summary>
    public BacktestSessionBuilder WithTimeseries(ITimeseriesRegistry timeseries)
    {
        _customTimeseries = timeseries ?? throw new ArgumentNullException(nameof(timeseries));
        return this;
    }

    /// <summary>
    /// 允许通过委托直接深度定制底层全局会话配置模型。
    /// </summary>
    public BacktestSessionBuilder Configure(Action<SessionConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_config);
        return this;
    }

    /// <summary>
    /// 最终构建并装配回测会话实例。
    /// </summary>
    public BacktestSession Build()
    {
        if (_customTimeseries == null && _config.Data.Timeseries.Count == 0)
        {
            _config.Data.Timeseries.Add(new TimeseriesConfig { TableId = "daily" });
        }

        IStrategyPipeline? pipeline = null;
        if (_strategy != null)
        {
            pipeline = _strategy is IStrategyPipeline p
                ? p
                : StrategyPipeline.Sequential().Add(_strategy);
        }

        var engine = _customTimeseries != null
            ? BacktestingEngine.Create(_config, timeseries: _customTimeseries, pipeline: pipeline)
            : BacktestingEngine.Create(_config, pipeline: pipeline);

        return new BacktestSession(_config, engine);
    }

    /// <summary>
    /// 构建并直接同步运行回测主循环，返回原始交易事实账本。
    /// </summary>
    public TradingResult Run()
    {
        var session = Build();
        return session.Run();
    }

    /// <summary>
    /// 构建、同步运行并无缝执行量化绩效分析。
    /// </summary>
    /// <param name="export">是否按配置自动导出分析报表至控制台或终端，默认为 false。</param>
    /// <returns>量化绩效分析报告。</returns>
    public AnalysisReport RunAndAnalyze(bool export = false)
    {
        var session = Build();
        return session.RunAndAnalyze(export);
    }
}
