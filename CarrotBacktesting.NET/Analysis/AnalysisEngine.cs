using System;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Analysis.Analyzers;

namespace CarrotBacktesting.NET.Analysis;

/// <summary>
/// 纯粹的统一分析管线调度引擎 (Analysis Pipeline Engine)。
/// 单一路径驱动所有分析器 (IAnalyzer)，默认装配官方五大标准分析器，支持任意分析器的动态注册、替换与移除。
/// </summary>
public sealed class AnalysisEngine : IAnalysisEngine
{
    private readonly List<IAnalyzer> _analyzers = new();

    /// <inheritdoc />
    public IReadOnlyList<IAnalyzer> Analyzers => _analyzers;

    /// <summary>
    /// 默认构造函数：自动挂载官方五大标准分析器（Portfolio, Capital, Trades, Cost, Signals）。
    /// </summary>
    public AnalysisEngine()
    {
        _analyzers.Add(new PortfolioAnalyzer());
        _analyzers.Add(new CapitalAnalyzer());
        _analyzers.Add(new TradeAnalyzer());
        _analyzers.Add(new CostAnalyzer());
        _analyzers.Add(new SignalAnalyzer());
    }

    /// <summary>
    /// 自定义分析器管线构造函数。
    /// </summary>
    /// <param name="analyzers">初始分析器序列。</param>
    public AnalysisEngine(IEnumerable<IAnalyzer> analyzers)
    {
        if (analyzers != null)
        {
            _analyzers.AddRange(analyzers);
        }
    }

    /// <inheritdoc />
    public IAnalysisEngine Add(IAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzers.Add(analyzer);
        return this;
    }

    /// <inheritdoc />
    public IAnalysisEngine Remove<T>() where T : IAnalyzer
    {
        _analyzers.RemoveAll(a => a is T);
        return this;
    }

    /// <inheritdoc />
    public AnalysisReport Analyze(
        TradingResult result,
        AnalysisOptions? options = null,
        ITimeseriesRegistry? timeseries = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var opt = options ?? new AnalysisOptions();

        var results = new List<IAnalysisResult>(_analyzers.Count);

        // 单一路径：严格按序调度所有已注册的分析器
        for (int i = 0; i < _analyzers.Count; i++)
        {
            var analyzer = _analyzers[i];
            var res = analyzer.Analyze(result, opt, timeseries);
            if (res != null)
            {
                results.Add(res);
            }
        }

        return new AnalysisReport(result, results);
    }
}
