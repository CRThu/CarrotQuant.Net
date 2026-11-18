using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Analysis.Analyzers;

namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 综合分析报告实体（聚合根）。
/// 基于统一的 IAnalysisResult 结果集构建，
/// 官方核心统计与第三方扩展均遵循统一契约并存。
/// </summary>
public sealed class AnalysisReport
{
    private readonly Dictionary<Type, IAnalysisResult> _byType = new();
    private readonly Dictionary<string, IAnalysisResult> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 底层不可变交易事实账本（交割单、平仓单、净值点、每日快照）。
    /// </summary>
    public TradingResult Facts { get; }

    /// <summary>
    /// 所有分析器产出的统一分析结果集合（完全平权，遵循同一 IAnalysisResult 契约）。
    /// </summary>
    public IReadOnlyList<IAnalysisResult> Results { get; }

    /// <summary>
    /// 第一层：组合净值与宏观风险统计门面 (Portfolio Statistics)。
    /// </summary>
    public PortfolioStatistics? Portfolio => Get<PortfolioStatistics>();

    /// <summary>
    /// 第二层：资本利用与敞口统计门面 (Capital Statistics)。
    /// </summary>
    public CapitalStatistics? Capital => Get<CapitalStatistics>();

    /// <summary>
    /// 第三层：交易行为与执行归因统计门面 (Trade Statistics)。
    /// </summary>
    public TradeStatistics? Trades => Get<TradeStatistics>();

    /// <summary>
    /// 第四层：现实成本与摩擦损耗统计门面 (Cost Statistics)。
    /// </summary>
    public CostStatistics? Cost => Get<CostStatistics>();

    /// <summary>
    /// 第五层：独立 Alpha 信号前向质量统计门面 (Signal Statistics)。
    /// </summary>
    public SignalStatistics? Signals => Get<SignalStatistics>();

    /// <summary>
    /// 标准构造函数：接收不可变交易事实账本与全量平权的 IAnalysisResult 序列。
    /// 构造期一次性固化 Type 与 Name 双哈希索引，保证后续查询绝对 O(1) 确定性。
    /// </summary>
    /// <param name="facts">不可变交易事实账本。</param>
    /// <param name="results">分析器产出的分析结果集合。</param>
    public AnalysisReport(TradingResult facts, IEnumerable<IAnalysisResult>? results)
    {
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));

        var list = new List<IAnalysisResult>();
        if (results != null)
        {
            foreach (var r in results)
            {
                if (r == null) continue;
                list.Add(r);
                _byName[r.Name] = r;
                _byType[r.GetType()] = r;
            }
        }
        Results = list;
    }

    /// <summary>
    /// 显式分层要素构造函数（支持直接以核心模型装配测试或定制管道）。
    /// </summary>
    public AnalysisReport(
        TradingResult facts,
        PortfolioStatistics? portfolio,
        CapitalStatistics? capital,
        TradeStatistics? trades,
        CostStatistics? cost,
        SignalStatistics? signals,
        IEnumerable<IAnalysisResult>? additionalResults = null)
    {
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));

        var list = new List<IAnalysisResult>();
        void AddItem(IAnalysisResult? item)
        {
            if (item != null)
            {
                list.Add(item);
                _byName[item.Name] = item;
                _byType[item.GetType()] = item;
            }
        }

        AddItem(portfolio);
        AddItem(capital);
        AddItem(trades);
        AddItem(cost);
        AddItem(signals);

        if (additionalResults != null)
        {
            foreach (var r in additionalResults)
            {
                AddItem(r);
            }
        }

        Results = list;
    }

    /// <summary>
    /// 统一按名称索引器：通过分析器/结果标识快速检索（不区分大小写，O(1)）。
    /// </summary>
    public IAnalysisResult? this[string name]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _byName.TryGetValue(name, out var val) ? val : null;
        }
    }

    /// <summary>
    /// 统一强类型安全检索契约。
    /// 优先按显式名称检索；缺省名称时按具体类型 O(1) 直接定位。
    /// </summary>
    /// <typeparam name="T">期望的分析产物具体类型。</typeparam>
    /// <param name="name">分析器名称（不区分大小写，可选）。</param>
    /// <returns>若存在且类型匹配则返回分析结果实例，否则返回 null。</returns>
    public T? Get<T>(string? name = null) where T : class, IAnalysisResult
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (_byName.TryGetValue(name, out var byNameObj) && byNameObj is T typed)
            {
                return typed;
            }
            return null;
        }

        // 缺省名称：直接按 Type 精确匹配（O(1)）
        if (_byType.TryGetValue(typeof(T), out var exactObj) && exactObj is T exactTyped)
        {
            return exactTyped;
        }

        // 若直接 Type 未命中（如传入接口或抽象基类），快速多态继承关系探测
        for (int i = 0; i < Results.Count; i++)
        {
            if (Results[i] is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// 探测是否存在指定类型或名称的分析产物。
    /// </summary>
    public bool Has<T>(string? name = null) where T : class, IAnalysisResult => Get<T>(name) != null;
}
