using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Analysis.Analyzers;

namespace CarrotBacktesting.NET.Analysis.Exporters;

/// <summary>
/// 交互式 HTML Tear Sheet 导出器配置选项。
/// 支持通过 YAML / JSON 声明式属性注入。
/// </summary>
public sealed class HtmlReportExporterOptions
{
    /// <summary>
    /// 输出文件路径（默认为 "tearsheet.html"）。
    /// </summary>
    public string OutputPath { get; set; } = "tearsheet.html";

    /// <summary>
    /// 报表页面标题。
    /// </summary>
    public string Title { get; set; } = "CarrotQuant 量化绩效 Tear Sheet";

    /// <summary>
    /// 默认是否启用深色主题（默认 true，网页内支持实时切换）。
    /// </summary>
    public bool DarkTheme { get; set; } = true;

    /// <summary>
    /// 时序图表最大渲染点数（超出时自动执行 LTTB 降采样，默认 5000；设为 0 或负数则不降采样）。
    /// </summary>
    public int MaxChartPoints { get; set; } = 5000;

    /// <summary>
    /// 滚动夏普与滚动波动率计算窗口步数（默认 60）。
    /// </summary>
    public int RollingWindow { get; set; } = 60;

    /// <summary>
    /// 是否在网页底部附带可搜索分页的平仓交易明细表（默认 true）。
    /// </summary>
    public bool IncludeTradeTable { get; set; } = true;

    /// <summary>
    /// 静态 HTML 内嵌平仓交易明细最大行数（默认 2000，防止高频策略文件过大；设为 0 或负数表示不限制）。
    /// </summary>
    public int MaxTradeTableRows { get; set; } = 2000;
}

/// <summary>
/// 交互式 HTML Tear Sheet 报表导出器 (HTML Tear Sheet Exporter)。
/// 内嵌完整离线 ECharts 运行时，生成单文件可交互量化网页报表：
/// 包含 KPI 概览、策略与基准净值/超额对比、水下回撤与极值标注、仓位杠杆敞口、
/// 月度收益热力图、滚动夏普/波动率、标的盈亏贡献榜、ExitTiming、双口径信号评估与交易明细。
/// </summary>
public sealed class HtmlReportExporter : IReportExporter
{
    private static readonly Lazy<string> EChartsScriptCache = new(LoadEmbeddedEChartsScript);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 输出文件路径（默认为 "tearsheet.html"）。
    /// </summary>
    public string OutputPath { get; set; } = "tearsheet.html";

    /// <summary>
    /// 报表页面标题。
    /// </summary>
    public string Title { get; set; } = "CarrotQuant 量化绩效 Tear Sheet";

    /// <summary>
    /// 默认是否启用深色主题。
    /// </summary>
    public bool DarkTheme { get; set; } = true;

    /// <summary>
    /// 时序图表最大渲染点数（默认 5000）。
    /// </summary>
    public int MaxChartPoints { get; set; } = 5000;

    /// <summary>
    /// 滚动夏普与滚动波动率窗口步数（默认 60）。
    /// </summary>
    public int RollingWindow { get; set; } = 60;

    /// <summary>
    /// 是否包含平仓交易明细表（默认 true）。
    /// </summary>
    public bool IncludeTradeTable { get; set; } = true;

    /// <summary>
    /// 静态 HTML 内嵌平仓交易明细最大行数（默认 2000；设为 0 或负数表示不限制）。
    /// </summary>
    public int MaxTradeTableRows { get; set; } = 2000;

    /// <summary>
    /// 无参构造函数（支持 YAML/JSON 配置工厂反射装配）。
    /// </summary>
    public HtmlReportExporter() { }

    /// <summary>
    /// 指定输出路径与配置构造 HTML 导出器。
    /// </summary>
    public HtmlReportExporter(string outputPath, HtmlReportExporterOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("输出路径不可为空。", nameof(outputPath));

        OutputPath = outputPath;
        if (options != null)
        {
            Title = options.Title;
            DarkTheme = options.DarkTheme;
            MaxChartPoints = options.MaxChartPoints;
            RollingWindow = options.RollingWindow;
            IncludeTradeTable = options.IncludeTradeTable;
            MaxTradeTableRows = options.MaxTradeTableRows;
        }
    }

    /// <inheritdoc />
    public void Export(AnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Save(report, OutputPath, ToOptions());
    }

    /// <summary>
    /// 将分析报告渲染为单文件交互式 HTML 并持久化至磁盘。
    /// </summary>
    public static void Save(AnalysisReport report, string filePath, HtmlReportExporterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("目标文件路径不可为空。", nameof(filePath));

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string html = RenderHtml(report, options);
        File.WriteAllText(filePath, html, Encoding.UTF8);
    }

    /// <summary>
    /// 将分析报告渲染为完整单文件交互式 HTML 字符串。
    /// </summary>
    public static string RenderHtml(AnalysisReport report, HtmlReportExporterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        options ??= new HtmlReportExporterOptions();

        var payload = BuildPayload(report, options);
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        // 防御标签注入：避免字符串中出现 "</script>" 截断内联脚本
        json = json.Replace("</", "<\\/", StringComparison.Ordinal);

        return HtmlReportTemplate.Build(
            title: options.Title,
            darkTheme: options.DarkTheme,
            echartsScript: EChartsScriptCache.Value,
            payloadJson: json);
    }

    /// <summary>
    /// 为轻量本地 Server 模式构建 HTML 外壳（数据通过 /api/summary、/api/trades 与 /api/kline 动态加载）。
    /// </summary>
    internal static string RenderServerShellHtml(string title = "CarrotQuant 交互式分析看板", bool darkTheme = true)
    {
        return HtmlReportTemplate.Build(
            title: title,
            darkTheme: darkTheme,
            echartsScript: EChartsScriptCache.Value,
            payloadJson: "{\"serverMode\":true}");
    }

    private HtmlReportExporterOptions ToOptions() => new()
    {
        OutputPath = OutputPath,
        Title = Title,
        DarkTheme = DarkTheme,
        MaxChartPoints = MaxChartPoints,
        RollingWindow = RollingWindow,
        IncludeTradeTable = IncludeTradeTable,
        MaxTradeTableRows = MaxTradeTableRows
    };

    private static string LoadEmbeddedEChartsScript()
    {
        var assembly = typeof(HtmlReportExporter).Assembly;
        const string resourceSuffix = "echarts.min.js";
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException($"未找到内嵌的 ECharts 资源文件: {resourceSuffix}");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"无法读取内嵌资源流: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    #region 数据投影与降采样内核

    internal static TearSheetPayload BuildPayload(AnalysisReport report, HtmlReportExporterOptions options, bool serverMode = false)
    {
        var facts = report.Facts;
        var portfolio = report.Portfolio;
        var capital = report.Capital;
        var trades = report.Trades;
        var cost = report.Cost;
        var signals = report.Signals;

        var seriesData = BuildTimeSeries(facts, portfolio, options.MaxChartPoints, options.RollingWindow);
        var monthlyHeatmap = BuildMonthlyHeatmap(portfolio);
        var tradeDist = BuildTradeDistribution(facts.ClosedTrades);
        var symbolPnl = BuildSymbolPnlRanking(facts.ClosedTrades);
        var monthlyTrades = BuildMonthlyTrades(facts.ClosedTrades);
        var exitTiming = BuildExitTiming(trades?.ExitTiming);
        var signalSection = BuildSignalSection(signals);
        var kpiSections = BuildKpiSections(report);
        var tagBreakdowns = BuildTagBreakdowns(trades?.TagBreakdowns);

        IReadOnlyList<TradeRowDto> tradeRows = Array.Empty<TradeRowDto>();
        bool tradesTruncated = false;
        if (!serverMode && options.IncludeTradeTable)
        {
            if (options.MaxTradeTableRows > 0 && facts.ClosedTrades.Count > options.MaxTradeTableRows)
            {
                int skip = facts.ClosedTrades.Count - options.MaxTradeTableRows;
                tradeRows = BuildTradeRows(facts.ClosedTrades.Skip(skip));
                tradesTruncated = true;
            }
            else
            {
                tradeRows = BuildTradeRows(facts.ClosedTrades);
            }
        }

        return new TearSheetPayload
        {
            ServerMode = serverMode,
            TradesTruncated = tradesTruncated,
            Title = options.Title,
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Status = facts.Status.ToString(),
            StartTime = facts.EquityCurve.Count > 0 ? FormatTime(facts.EquityCurve[0].Time) : "-",
            EndTime = facts.EquityCurve.Count > 0 ? FormatTime(facts.EquityCurve[^1].Time) : "-",
            TotalBars = facts.EquityCurve.Count,
            TotalTrades = facts.ClosedTrades.Count,
            KpiSections = kpiSections,
            TimeSeries = seriesData,
            MonthlyHeatmap = monthlyHeatmap,
            TradeDistribution = tradeDist,
            SymbolPnl = symbolPnl,
            MonthlyTrades = monthlyTrades,
            ExitTiming = exitTiming,
            TagBreakdowns = tagBreakdowns,
            Signals = signalSection,
            TradeRows = tradeRows
        };
    }

    private static List<KpiSectionDto> BuildKpiSections(AnalysisReport report)
    {
        var sections = new List<KpiSectionDto>();
        foreach (var result in report.Results)
        {
            var items = result.GetMetrics()
                .Select(m => new KpiMetricDto(m.Label, m.Value, m.Category ?? "常规"))
                .ToList();
            if (items.Count > 0)
            {
                sections.Add(new KpiSectionDto(result.Name, result.Title, items));
            }
        }
        return sections;
    }

    private static TimeSeriesDto BuildTimeSeries(
        TradingResult facts,
        PortfolioStatistics? portfolio,
        int maxPoints,
        int rollingWindow)
    {
        var rawCurve = facts.EquityCurve;
        if (rawCurve.Count == 0 && facts.DailySnapshots.Count > 0)
        {
            rawCurve = facts.DailySnapshots
                .Select(s => new EquityPoint(s.Time, s.TotalEquity, s.AvailableCash, s.FrozenCash, s.OccupiedMargin, s.TotalMarketValue, s.UnrealizedPnL))
                .ToList();
        }

        if (rawCurve.Count == 0)
        {
            return new TimeSeriesDto();
        }

        int n = rawCurve.Count;
        double startEquity = rawCurve[0].TotalEquity;

        // 构造全量净值、回撤、仓位、杠杆与基准序列
        var times = new string[n];
        var equities = new double?[n];
        var returnsPct = new double?[n];
        var drawdownsPct = new double?[n];
        var positionPct = new double?[n];
        var marginPct = new double?[n];
        var cashPct = new double?[n];

        double runningPeak = startEquity;
        for (int i = 0; i < n; i++)
        {
            var pt = rawCurve[i];
            times[i] = FormatTime(pt.Time);
            equities[i] = Sanitize(pt.TotalEquity);
            returnsPct[i] = startEquity > 0 ? Sanitize((pt.TotalEquity - startEquity) / startEquity * 100.0) : 0.0;

            if (pt.TotalEquity > runningPeak) runningPeak = pt.TotalEquity;
            double dd = runningPeak > 0 ? (pt.TotalEquity - runningPeak) / runningPeak * 100.0 : 0.0;
            drawdownsPct[i] = Sanitize(dd);

            if (pt.TotalEquity > 0)
            {
                positionPct[i] = Sanitize(pt.TotalMarketValue / pt.TotalEquity * 100.0);
                marginPct[i] = Sanitize(pt.OccupiedMargin / pt.TotalEquity * 100.0);
                cashPct[i] = Sanitize(pt.AvailableCash / pt.TotalEquity * 100.0);
            }
            else
            {
                positionPct[i] = 0.0;
                marginPct[i] = 0.0;
                cashPct[i] = 0.0;
            }
        }

        // 对齐基准净值曲线（若存在）
        double?[]? benchmarkReturnPct = null;
        double?[]? excessReturnPct = null;
        if (portfolio?.BenchmarkCurve != null && portfolio.BenchmarkCurve.Count > 1)
        {
            var bMap = new Dictionary<DateTime, double>(portfolio.BenchmarkCurve.Count);
            var bDateMap = new Dictionary<DateTime, double>(portfolio.BenchmarkCurve.Count);
            foreach (var bp in portfolio.BenchmarkCurve)
            {
                DateTime bDt = (DateTime)bp.Time;
                bMap[bDt] = bp.NormalizedValue;
                bDateMap[bDt.Date] = bp.NormalizedValue;
            }

            benchmarkReturnPct = new double?[n];
            excessReturnPct = new double?[n];
            double lastNorm = 1.0;

            for (int i = 0; i < n; i++)
            {
                DateTime dt = (DateTime)rawCurve[i].Time;
                if (bMap.TryGetValue(dt, out double norm) || bDateMap.TryGetValue(dt.Date, out norm))
                {
                    lastNorm = norm;
                }

                double bRet = (lastNorm - 1.0) * 100.0;
                benchmarkReturnPct[i] = Sanitize(bRet);

                double stratNav = startEquity > 0 ? rawCurve[i].TotalEquity / startEquity : 1.0;
                double excess = lastNorm > 0 ? (stratNav / lastNorm - 1.0) * 100.0 : 0.0;
                excessReturnPct[i] = Sanitize(excess);
            }
        }

        // 计算滚动夏普与滚动波动率
        var (rollingSharpe, rollingVol) = ComputeRollingMetrics(rawCurve, rollingWindow);

        // 对高频长序列执行 LTTB 降采样，并强制保留最大回撤关键节点
        int[] indices = SelectSampleIndices(rawCurve, maxPoints, portfolio);
        if (indices.Length < n)
        {
            times = Pick(times, indices);
            equities = Pick(equities, indices);
            returnsPct = Pick(returnsPct, indices);
            drawdownsPct = Pick(drawdownsPct, indices);
            positionPct = Pick(positionPct, indices);
            marginPct = Pick(marginPct, indices);
            cashPct = Pick(cashPct, indices);
            rollingSharpe = Pick(rollingSharpe, indices);
            rollingVol = Pick(rollingVol, indices);
            if (benchmarkReturnPct != null) benchmarkReturnPct = Pick(benchmarkReturnPct, indices);
            if (excessReturnPct != null) excessReturnPct = Pick(excessReturnPct, indices);
        }

        return new TimeSeriesDto
        {
            Times = times,
            Equities = equities,
            ReturnsPct = returnsPct,
            DrawdownsPct = drawdownsPct,
            PositionPct = positionPct,
            MarginPct = marginPct,
            CashPct = cashPct,
            BenchmarkReturnsPct = benchmarkReturnPct,
            ExcessReturnsPct = excessReturnPct,
            RollingSharpe = rollingSharpe,
            RollingVolatilityPct = rollingVol,
            RollingWindow = rollingWindow,
            PeakTime = portfolio != null && portfolio.MaxDrawdown > 1e-9 ? FormatTime(portfolio.MaxDrawdownPeakTime) : null,
            TroughTime = portfolio != null && portfolio.MaxDrawdown > 1e-9 ? FormatTime(portfolio.MaxDrawdownTroughTime) : null,
            RecoveryTime = portfolio?.MaxDrawdownRecoveryTime.HasValue == true ? FormatTime(portfolio.MaxDrawdownRecoveryTime.Value) : null
        };
    }

    private static (double?[] Sharpe, double?[] VolPct) ComputeRollingMetrics(IReadOnlyList<EquityPoint> curve, int window)
    {
        int n = curve.Count;
        var sharpe = new double?[n];
        var volPct = new double?[n];

        if (window <= 2 || n <= window)
        {
            return (sharpe, volPct);
        }

        var stepReturns = new double[n];
        for (int i = 1; i < n; i++)
        {
            double prev = curve[i - 1].TotalEquity;
            double curr = curve[i].TotalEquity;
            stepReturns[i] = prev > 0 ? (curr - prev) / prev : 0.0;
        }

        const double annFactor = 252.0;
        double sqrtAnn = Math.Sqrt(annFactor);

        double sum = 0.0;
        double sumSq = 0.0;
        for (int j = 1; j <= window; j++)
        {
            double r = stepReturns[j];
            sum += r;
            sumSq += r * r;
        }

        for (int i = window; i < n; i++)
        {
            if (i > window)
            {
                double add = stepReturns[i];
                double rem = stepReturns[i - window];
                sum += add - rem;
                sumSq += add * add - rem * rem;
            }

            double mean = sum / window;
            double variance = Math.Max(0.0, (sumSq - window * mean * mean) / (window - 1));
            double std = Math.Sqrt(variance);
            double annVol = std * sqrtAnn;

            volPct[i] = Sanitize(annVol * 100.0);
            sharpe[i] = std > 1e-12 ? Sanitize((mean / std) * sqrtAnn) : 0.0;
        }

        return (sharpe, volPct);
    }

    /// <summary>
    /// LTTB (Largest-Triangle-Three-Buckets) 降采样，保留净值曲线极值与最大回撤关键节点。
    /// </summary>
    private static int[] SelectSampleIndices(IReadOnlyList<EquityPoint> curve, int maxPoints, PortfolioStatistics? portfolio)
    {
        int n = curve.Count;
        if (maxPoints <= 0 || n <= maxPoints || maxPoints < 3)
        {
            var all = new int[n];
            for (int i = 0; i < n; i++) all[i] = i;
            return all;
        }

        var sampled = new int[maxPoints];
        sampled[0] = 0;
        sampled[maxPoints - 1] = n - 1;

        double every = (double)(n - 2) / (maxPoints - 2);
        int a = 0;

        for (int i = 0; i < maxPoints - 2; i++)
        {
            int avgRangeStart = (int)Math.Floor((i + 1) * every) + 1;
            int avgRangeEnd = (int)Math.Floor((i + 2) * every) + 1;
            if (avgRangeEnd >= n) avgRangeEnd = n;

            double avgX = 0.0, avgY = 0.0;
            int avgLen = avgRangeEnd - avgRangeStart;
            if (avgLen > 0)
            {
                for (int j = avgRangeStart; j < avgRangeEnd; j++)
                {
                    avgX += j;
                    avgY += curve[j].TotalEquity;
                }
                avgX /= avgLen;
                avgY /= avgLen;
            }

            int rangeOffs = (int)Math.Floor(i * every) + 1;
            int rangeTo = (int)Math.Floor((i + 1) * every) + 1;
            if (rangeTo >= n) rangeTo = n - 1;

            double pointAX = a;
            double pointAY = curve[a].TotalEquity;
            double maxArea = -1.0;
            int nextA = rangeOffs;

            for (int j = rangeOffs; j < rangeTo; j++)
            {
                double area = Math.Abs((pointAX - avgX) * (curve[j].TotalEquity - pointAY) -
                                       (pointAX - j) * (avgY - pointAY)) * 0.5;
                if (area > maxArea)
                {
                    maxArea = area;
                    nextA = j;
                }
            }

            sampled[i + 1] = nextA;
            a = nextA;
        }

        if (portfolio == null || portfolio.MaxDrawdown <= 1e-9)
        {
            return sampled;
        }

        // 强制保留最大回撤 Peak / Trough / Recovery 索引
        var indexSet = new SortedSet<int>(sampled);
        DateTime peakDt = (DateTime)portfolio.MaxDrawdownPeakTime;
        DateTime troughDt = (DateTime)portfolio.MaxDrawdownTroughTime;
        DateTime? recDt = portfolio.MaxDrawdownRecoveryTime.HasValue ? (DateTime)portfolio.MaxDrawdownRecoveryTime.Value : null;

        for (int i = 0; i < n; i++)
        {
            DateTime dt = (DateTime)curve[i].Time;
            if (dt == peakDt || dt == troughDt || (recDt.HasValue && dt == recDt.Value))
            {
                indexSet.Add(i);
            }
        }

        return indexSet.ToArray();
    }

    private static T[] Pick<T>(T[] source, int[] indices)
    {
        var result = new T[indices.Length];
        for (int i = 0; i < indices.Length; i++) result[i] = source[indices[i]];
        return result;
    }

    private static MonthlyHeatmapDto BuildMonthlyHeatmap(PortfolioStatistics? portfolio)
    {
        if (portfolio == null || portfolio.MonthlyReturns.Count == 0)
            return new MonthlyHeatmapDto();

        var years = portfolio.MonthlyReturns.Select(m => m.Year).Distinct().OrderBy(y => y).ToList();
        var yearIndex = years.Select((y, i) => (y, i)).ToDictionary(x => x.y, x => x.i);
        var cells = new List<object[]>();
        var annualCompound = new double[years.Count];
        for (int i = 0; i < annualCompound.Length; i++) annualCompound[i] = 1.0;

        foreach (var mr in portfolio.MonthlyReturns)
        {
            int yi = yearIndex[mr.Year];
            int mi = mr.Month - 1;
            double pct = SanitizeFinite(mr.ReturnRate * 100.0, 2);
            cells.Add(new object[] { mi, yi, pct });
            if (double.IsFinite(mr.ReturnRate))
                annualCompound[yi] *= (1.0 + mr.ReturnRate);
        }

        for (int yi = 0; yi < years.Count; yi++)
        {
            double yrPct = SanitizeFinite((annualCompound[yi] - 1.0) * 100.0, 2);
            cells.Add(new object[] { 12, yi, yrPct });
        }

        return new MonthlyHeatmapDto
        {
            Years = years.Select(y => y.ToString(CultureInfo.InvariantCulture)).ToArray(),
            Cells = cells
        };
    }

    private static TradeDistributionDto BuildTradeDistribution(IReadOnlyList<Trade> closedTrades)
    {
        if (closedTrades.Count == 0) return new TradeDistributionDto();

        var returnsPct = closedTrades
            .Select(t => t.ReturnRate * 100.0)
            .Where(r => double.IsFinite(r))
            .ToList();

        if (returnsPct.Count == 0) return new TradeDistributionDto();

        double min = returnsPct.Min();
        double max = returnsPct.Max();
        if (Math.Abs(max - min) < 1e-9)
        {
            return new TradeDistributionDto
            {
                Labels = new[] { $"{min:F2}%" },
                Counts = new[] { returnsPct.Count },
                IsPositive = new[] { min >= 0 }
            };
        }

        const int binCount = 20;
        double step = (max - min) / binCount;
        var counts = new int[binCount];
        var labels = new string[binCount];
        var isPositive = new bool[binCount];

        for (int i = 0; i < binCount; i++)
        {
            double lo = min + i * step;
            double hi = lo + step;
            labels[i] = $"{lo:F1}%~{hi:F1}%";
            isPositive[i] = (lo + hi) * 0.5 >= 0;
        }

        foreach (double r in returnsPct)
        {
            int idx = (int)Math.Floor((r - min) / step);
            if (idx >= binCount) idx = binCount - 1;
            if (idx < 0) idx = 0;
            counts[idx]++;
        }

        return new TradeDistributionDto
        {
            Labels = labels,
            Counts = counts,
            IsPositive = isPositive
        };
    }

    private static SymbolPnlDto BuildSymbolPnlRanking(IReadOnlyList<Trade> closedTrades)
    {
        if (closedTrades.Count == 0) return new SymbolPnlDto();

        var grouped = closedTrades
            .GroupBy(t => t.Symbol)
            .Select(g => new
            {
                Symbol = g.Key,
                NetPnL = g.Sum(x => double.IsFinite(x.RealizedPnL) ? x.RealizedPnL : 0.0),
                Count = g.Count(),
                WinRate = g.Count(x => x.RealizedPnL > 0) * 100.0 / g.Count()
            })
            .ToList();

        var topWinners = grouped
            .Where(x => x.NetPnL > 0)
            .OrderByDescending(x => x.NetPnL)
            .Take(10)
            .Reverse()
            .Select(x => new SymbolPnlItemDto(x.Symbol, SanitizeFinite(x.NetPnL, 2), x.Count, SanitizeFinite(x.WinRate, 1)))
            .ToList();

        var topLosers = grouped
            .Where(x => x.NetPnL < 0)
            .OrderBy(x => x.NetPnL)
            .Take(10)
            .Select(x => new SymbolPnlItemDto(x.Symbol, SanitizeFinite(x.NetPnL, 2), x.Count, SanitizeFinite(x.WinRate, 1)))
            .ToList();

        return new SymbolPnlDto
        {
            TopWinners = topWinners,
            TopLosers = topLosers
        };
    }

    private static MonthlyTradesDto BuildMonthlyTrades(IReadOnlyList<Trade> closedTrades)
    {
        if (closedTrades.Count == 0) return new MonthlyTradesDto();

        var grouped = closedTrades
            .GroupBy(t => new { t.ExitTime.Year, t.ExitTime.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .ToList();

        return new MonthlyTradesDto
        {
            Months = grouped.Select(g => $"{g.Key.Year}-{g.Key.Month:D2}").ToArray(),
            Counts = grouped.Select(g => g.Count()).ToArray(),
            WinRatesPct = grouped.Select(g => SanitizeFinite(g.Count(x => x.RealizedPnL > 0) * 100.0 / g.Count(), 2)).ToArray(),
            AvgReturnsPct = grouped.Select(g => SanitizeFinite(g.Average(x => double.IsFinite(x.ReturnRate) ? x.ReturnRate : 0.0) * 100.0, 2)).ToArray()
        };
    }

    private static ExitTimingDto? BuildExitTiming(ExitTimingStatistics? exitTiming)
    {
        if (exitTiming == null || exitTiming.HorizonSteps <= 0 || exitTiming.AverageReturns.Count == 0)
            return null;

        int n = exitTiming.AverageReturns.Count;
        return new ExitTimingDto
        {
            ValidTradesCount = exitTiming.ValidTradesCount,
            Steps = Enumerable.Range(1, n).Select(i => $"T+{i}").ToArray(),
            AvgReturnsPct = exitTiming.AverageReturns.Select(v => Sanitize(v * 100.0)).ToArray(),
            MedianReturnsPct = exitTiming.MedianReturns.Select(v => Sanitize(v * 100.0)).ToArray(),
            WinRatesPct = exitTiming.WinRates.Select(v => Sanitize(v * 100.0)).ToArray()
        };
    }

    private static List<TagBreakdownDto> BuildTagBreakdowns(IReadOnlyDictionary<string, TradeStatistics>? tags)
    {
        if (tags == null || tags.Count == 0) return new List<TagBreakdownDto>();

        return tags
            .OrderByDescending(kv => kv.Value.TotalTrades)
            .Select(kv => new TagBreakdownDto(
                kv.Key,
                kv.Value.TotalTrades,
                SanitizeFinite(kv.Value.WinRate * 100.0, 2),
                SanitizeFinite(kv.Value.AverageReturn * 100.0, 2),
                SanitizeFinite(kv.Value.WinLossRatio, 2),
                SanitizeFinite(kv.Value.ProfitFactor, 2)))
            .ToList();
    }

    private static SignalSectionDto? BuildSignalSection(SignalStatistics? signals)
    {
        if (signals == null || signals.Horizons.Count == 0) return null;

        var groups = new Dictionary<string, SignalGroupDto>
        {
            ["Total"] = ToSignalGroupDto("Total", signals)
        };

        foreach (var kv in signals.GroupBreakdowns)
        {
            groups[kv.Key] = ToSignalGroupDto(kv.Key, kv.Value);
        }

        return new SignalSectionDto
        {
            PeakHorizonStep = signals.PeakHorizonStep,
            TotalSignals = signals.TotalSignals,
            Groups = groups
        };
    }

    private static SignalGroupDto ToSignalGroupDto(string name, SignalStatistics s)
    {
        return new SignalGroupDto
        {
            Name = name,
            TotalSignals = s.TotalSignals,
            PeakHorizonStep = s.PeakHorizonStep,
            Steps = s.Horizons.Select(h => $"T+{h.Step}").ToArray(),
            SampleAvgPct = s.Horizons.Select(h => Sanitize(h.SampleWeighted.AvgReturn * 100.0)).ToArray(),
            SampleMedianPct = s.Horizons.Select(h => Sanitize(h.SampleWeighted.MedianReturn * 100.0)).ToArray(),
            SampleWinRatePct = s.Horizons.Select(h => Sanitize(h.SampleWeighted.WinRate * 100.0)).ToArray(),
            TimeAvgPct = s.Horizons.Select(h => Sanitize(h.TimeWeighted.AvgReturn * 100.0)).ToArray(),
            TimeMedianPct = s.Horizons.Select(h => Sanitize(h.TimeWeighted.MedianReturn * 100.0)).ToArray(),
            TimeWinRatePct = s.Horizons.Select(h => Sanitize(h.TimeWeighted.WinRate * 100.0)).ToArray()
        };
    }

    internal static IReadOnlyList<TradeRowDto> BuildTradeRows(IEnumerable<Trade> closedTrades)
    {
        return closedTrades.Select(t => new TradeRowDto(
            t.Symbol,
            t.Side == TradeSide.Long ? "多" : "空",
            FormatTime(t.EntryTime),
            SanitizeFinite(t.EntryPrice, 4),
            FormatTime(t.ExitTime),
            SanitizeFinite(t.ExitPrice, 4),
            (double)t.Quantity,
            SanitizeFinite(t.RealizedPnL, 2),
            SanitizeFinite(t.ReturnRate * 100.0, 2),
            SanitizeFinite(t.Commission, 2),
            t.HoldingSteps,
            string.IsNullOrEmpty(t.ExitTag) ? t.EntryTag : (string.IsNullOrEmpty(t.EntryTag) ? t.ExitTag : $"{t.EntryTag}/{t.ExitTag}")
        )).ToList();
    }

    private static string FormatTime(MarketTime time)
    {
        DateTime dt = (DateTime)time;
        if (dt.TimeOfDay == TimeSpan.Zero)
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return dt.Second != 0
            ? dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static double? Sanitize(double value) => double.IsFinite(value) ? Math.Round(value, 4) : null;

    private static double SanitizeFinite(double value, int decimals) =>
        double.IsFinite(value) ? Math.Round(value, decimals) : 0.0;

    #endregion

    #region 内部 JSON 传输 DTO

    internal sealed class TearSheetPayload
    {
        public bool ServerMode { get; init; }
        public bool TradesTruncated { get; init; }
        public string Title { get; init; } = string.Empty;
        public string GeneratedAt { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string StartTime { get; init; } = string.Empty;
        public string EndTime { get; init; } = string.Empty;
        public int TotalBars { get; init; }
        public int TotalTrades { get; init; }
        public List<KpiSectionDto> KpiSections { get; init; } = new();
        public TimeSeriesDto TimeSeries { get; init; } = new();
        public MonthlyHeatmapDto MonthlyHeatmap { get; init; } = new();
        public TradeDistributionDto TradeDistribution { get; init; } = new();
        public SymbolPnlDto SymbolPnl { get; init; } = new();
        public MonthlyTradesDto MonthlyTrades { get; init; } = new();
        public ExitTimingDto? ExitTiming { get; init; }
        public List<TagBreakdownDto> TagBreakdowns { get; init; } = new();
        public SignalSectionDto? Signals { get; init; }
        public IReadOnlyList<TradeRowDto> TradeRows { get; init; } = Array.Empty<TradeRowDto>();
    }

    internal sealed record KpiSectionDto(string Name, string Title, List<KpiMetricDto> Metrics);
    internal sealed record KpiMetricDto(string Label, string Value, string Category);

    internal sealed class TimeSeriesDto
    {
        public string[] Times { get; init; } = Array.Empty<string>();
        public double?[] Equities { get; init; } = Array.Empty<double?>();
        public double?[] ReturnsPct { get; init; } = Array.Empty<double?>();
        public double?[] DrawdownsPct { get; init; } = Array.Empty<double?>();
        public double?[] PositionPct { get; init; } = Array.Empty<double?>();
        public double?[] MarginPct { get; init; } = Array.Empty<double?>();
        public double?[] CashPct { get; init; } = Array.Empty<double?>();
        public double?[]? BenchmarkReturnsPct { get; init; }
        public double?[]? ExcessReturnsPct { get; init; }
        public double?[] RollingSharpe { get; init; } = Array.Empty<double?>();
        public double?[] RollingVolatilityPct { get; init; } = Array.Empty<double?>();
        public int RollingWindow { get; init; }
        public string? PeakTime { get; init; }
        public string? TroughTime { get; init; }
        public string? RecoveryTime { get; init; }
    }

    internal sealed class MonthlyHeatmapDto
    {
        public string[] Years { get; init; } = Array.Empty<string>();
        public List<object[]> Cells { get; init; } = new();
    }

    internal sealed class TradeDistributionDto
    {
        public string[] Labels { get; init; } = Array.Empty<string>();
        public int[] Counts { get; init; } = Array.Empty<int>();
        public bool[] IsPositive { get; init; } = Array.Empty<bool>();
    }

    internal sealed class SymbolPnlDto
    {
        public List<SymbolPnlItemDto> TopWinners { get; init; } = new();
        public List<SymbolPnlItemDto> TopLosers { get; init; } = new();
    }

    internal sealed record SymbolPnlItemDto(string Symbol, double NetPnL, int Trades, double WinRatePct);

    internal sealed class MonthlyTradesDto
    {
        public string[] Months { get; init; } = Array.Empty<string>();
        public int[] Counts { get; init; } = Array.Empty<int>();
        public double[] WinRatesPct { get; init; } = Array.Empty<double>();
        public double[] AvgReturnsPct { get; init; } = Array.Empty<double>();
    }

    internal sealed class ExitTimingDto
    {
        public int ValidTradesCount { get; init; }
        public string[] Steps { get; init; } = Array.Empty<string>();
        public double?[] AvgReturnsPct { get; init; } = Array.Empty<double?>();
        public double?[] MedianReturnsPct { get; init; } = Array.Empty<double?>();
        public double?[] WinRatesPct { get; init; } = Array.Empty<double?>();
    }

    internal sealed record TagBreakdownDto(string Tag, int Trades, double WinRatePct, double AvgReturnPct, double WinLossRatio, double ProfitFactor);

    internal sealed class SignalSectionDto
    {
        public int PeakHorizonStep { get; init; }
        public int TotalSignals { get; init; }
        public Dictionary<string, SignalGroupDto> Groups { get; init; } = new();
    }

    internal sealed class SignalGroupDto
    {
        public string Name { get; init; } = string.Empty;
        public int TotalSignals { get; init; }
        public int PeakHorizonStep { get; init; }
        public string[] Steps { get; init; } = Array.Empty<string>();
        public double?[] SampleAvgPct { get; init; } = Array.Empty<double?>();
        public double?[] SampleMedianPct { get; init; } = Array.Empty<double?>();
        public double?[] SampleWinRatePct { get; init; } = Array.Empty<double?>();
        public double?[] TimeAvgPct { get; init; } = Array.Empty<double?>();
        public double?[] TimeMedianPct { get; init; } = Array.Empty<double?>();
        public double?[] TimeWinRatePct { get; init; } = Array.Empty<double?>();
    }

    internal sealed record TradeRowDto(
        string Symbol,
        string Side,
        string EntryTime,
        double EntryPrice,
        string ExitTime,
        double ExitPrice,
        double Quantity,
        double NetPnL,
        double ReturnPct,
        double Commission,
        int HoldingSteps,
        string OrderTag);

    #endregion
}
