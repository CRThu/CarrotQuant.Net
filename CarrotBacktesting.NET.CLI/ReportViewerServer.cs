using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarrotBacktesting.NET.Abstraction.Analysis;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Analysis.Exporters;
using CarrotBacktesting.NET.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CarrotBacktesting.NET.CLI;

/// <summary>
/// 轻量级本地交互式分析报表与 K 线买卖点复盘 HTTP Server。
/// 基于 ASP.NET Core Minimal API 构建，按需懒加载单只标的 OHLCV 行情，
/// 并支持十万级交易流水的服务端分页检索。
/// </summary>
public sealed class ReportViewerServer : IAsyncDisposable
{
    private readonly AnalysisReport _report;
    private readonly string? _dataRoot;
    private readonly string _tableId;
    private readonly string _title;
    private readonly bool _darkTheme;
    private readonly int _port;

    private readonly object _sourceLock = new();
    private ITimeseriesSource? _timeseriesSource;
    private bool _sourceInitialized;
    private WebApplication? _app;

    /// <summary>
    /// Server 启动后的监听地址（如 http://127.0.0.1:54321）。
    /// </summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public ReportViewerServer(
        AnalysisReport report,
        string? dataRoot = null,
        string tableId = "daily",
        string title = "CarrotQuant 交互式分析看板",
        bool darkTheme = true,
        int port = 0)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _dataRoot = string.IsNullOrWhiteSpace(dataRoot) ? null : dataRoot;
        _tableId = string.IsNullOrWhiteSpace(tableId) ? "daily" : tableId;
        _title = title;
        _darkTheme = darkTheme;
        _port = port;
    }

    /// <summary>
    /// 异步启动本地 Kestrel HTTP Server。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app != null) return;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{_port}");

        var app = builder.Build();
        MapEndpoints(app);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _app = app;
        BaseUrl = app.Urls.FirstOrDefault() ?? $"http://127.0.0.1:{_port}";
    }

    /// <summary>
    /// 停止本地 Server。
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app != null)
        {
            await _app.StopAsync(cancellationToken).ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_sourceLock)
        {
            _timeseriesSource?.Dispose();
            _timeseriesSource = null;
        }
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/", () =>
        {
            string html = HtmlReportExporter.RenderServerShellHtml(_title, _darkTheme);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/api/summary", () =>
        {
            var payload = HtmlReportExporter.BuildPayload(_report, new HtmlReportExporterOptions
            {
                Title = _title,
                DarkTheme = _darkTheme
            }, serverMode: true);

            string json = JsonSerializer.Serialize(payload, HtmlReportExporter.JsonOptions);
            return Results.Content(json, "application/json; charset=utf-8");
        });

        app.MapGet("/api/trades", (int? page, int? size, string? q, string? sort) =>
        {
            int p = Math.Max(1, page ?? 1);
            int pageSize = Math.Clamp(size ?? 50, 1, 500);
            string query = (q ?? string.Empty).Trim();

            IEnumerable<Trade> seq = _report.Facts.ClosedTrades;
            if (!string.IsNullOrEmpty(query))
            {
                seq = seq.Where(t =>
                    t.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.EntryTag.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.ExitTag.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            seq = sort switch
            {
                "pnl_desc" => seq.OrderByDescending(t => t.RealizedPnL),
                "pnl_asc" => seq.OrderBy(t => t.RealizedPnL),
                "ret_desc" => seq.OrderByDescending(t => t.ReturnRate),
                _ => seq.OrderByDescending(t => t.ExitTime)
            };

            var filtered = seq.ToList();
            int total = filtered.Count;
            var items = HtmlReportExporter.BuildTradeRows(filtered.Skip((p - 1) * pageSize).Take(pageSize));

            string json = JsonSerializer.Serialize(new
            {
                total,
                page = p,
                pageSize,
                items
            }, HtmlReportExporter.JsonOptions);

            return Results.Content(json, "application/json; charset=utf-8");
        });

        app.MapGet("/api/symbols", () =>
        {
            var symbols = _report.Facts.ClosedTrades.Select(t => t.Symbol)
                .Concat(_report.Facts.Settlements.Select(s => s.Symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string json = JsonSerializer.Serialize(new
            {
                hasDataSource = _dataRoot != null,
                symbols
            }, HtmlReportExporter.JsonOptions);

            return Results.Content(json, "application/json; charset=utf-8");
        });

        app.MapGet("/api/kline", (string? symbol, string? start, string? end) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { error = "缺少 symbol 参数" });
            }

            var response = BuildKlineResponse(symbol.Trim(), start, end);
            string json = JsonSerializer.Serialize(response, HtmlReportExporter.JsonOptions);
            return Results.Content(json, "application/json; charset=utf-8");
        });
    }

    private object BuildKlineResponse(string symbol, string? startStr, string? endStr)
    {
        var settlements = _report.Facts.Settlements
            .Where(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .Select(s => new
            {
                time = FormatTime((DateTime)s.Time),
                direction = s.Direction == OrderDirection.Buy ? "B" : "S",
                price = Math.Round(s.Price, 4),
                quantity = (double)s.Quantity,
                pnl = Math.Round(s.RealizedPnL, 2),
                tag = s.OrderTag
            })
            .ToList();

        var trades = _report.Facts.ClosedTrades
            .Where(t => string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .Select(t => new
            {
                entryTime = FormatTime((DateTime)t.EntryTime),
                entryPrice = Math.Round(t.EntryPrice, 4),
                exitTime = FormatTime((DateTime)t.ExitTime),
                exitPrice = Math.Round(t.ExitPrice, 4),
                side = t.Side == TradeSide.Long ? "多" : "空",
                pnl = Math.Round(t.RealizedPnL, 2),
                returnPct = Math.Round(t.ReturnRate * 100.0, 2)
            })
            .ToList();

        var source = GetOrCreateTimeseriesSource();
        if (source == null)
        {
            return new
            {
                symbol,
                hasDataSource = false,
                message = "未指定行情数据目录 (--data)，仅展示买卖点与交易记录。",
                dates = Array.Empty<string>(),
                ohlcv = Array.Empty<double[]>(),
                settlements,
                trades
            };
        }

        int symbolIndex = source.GetSymbolIndex(symbol);
        if (symbolIndex < 0)
        {
            return new
            {
                symbol,
                hasDataSource = true,
                message = $"数据源中未找到标的 '{symbol}'。",
                dates = Array.Empty<string>(),
                ohlcv = Array.Empty<double[]>(),
                settlements,
                trades
            };
        }

        var allDates = source.TradeDates;
        if (allDates.Count == 0)
        {
            return new
            {
                symbol,
                hasDataSource = true,
                message = "数据源交易日历为空。",
                dates = Array.Empty<string>(),
                ohlcv = Array.Empty<double[]>(),
                settlements,
                trades
            };
        }

        DateTime? reqStart = DateTime.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sDt) ? sDt : null;
        DateTime? reqEnd = DateTime.TryParse(endStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var eDt) ? eDt : null;

        int startIdx = 0;
        int endIdx = allDates.Count - 1;
        if (reqStart.HasValue)
        {
            for (int i = 0; i < allDates.Count; i++)
            {
                if ((DateTime)allDates[i] >= reqStart.Value) { startIdx = i; break; }
            }
        }
        if (reqEnd.HasValue)
        {
            for (int i = allDates.Count - 1; i >= 0; i--)
            {
                if ((DateTime)allDates[i] <= reqEnd.Value) { endIdx = i; break; }
            }
        }

        if (endIdx < startIdx)
        {
            startIdx = 0;
            endIdx = allDates.Count - 1;
        }

        int len = endIdx - startIdx + 1;
        var open = new double[len];
        var high = new double[len];
        var low = new double[len];
        var close = new double[len];
        var volume = new double[len];

        source.ReadSymbolSeries<double>(symbol, "open", startIdx, len, open);
        source.ReadSymbolSeries<double>(symbol, "high", startIdx, len, high);
        source.ReadSymbolSeries<double>(symbol, "low", startIdx, len, low);
        source.ReadSymbolSeries<double>(symbol, "close", startIdx, len, close);

        bool hasVol = source.HasField("volume");
        if (hasVol)
        {
            source.ReadSymbolSeries<double>(symbol, "volume", startIdx, len, volume);
        }

        var datesOut = new List<string>(len);
        // ECharts candlestick 顺序: [open, close, low, high, volume]
        var ohlcvOut = new List<double[]>(len);

        for (int i = 0; i < len; i++)
        {
            if (!double.IsFinite(open[i]) || !double.IsFinite(close[i]) || !double.IsFinite(low[i]) || !double.IsFinite(high[i]))
            {
                continue; // 跳过停牌/缺失日
            }

            datesOut.Add(FormatTime((DateTime)allDates[startIdx + i]));
            double volVal = hasVol && double.IsFinite(volume[i]) ? volume[i] : 0.0;
            ohlcvOut.Add(new[] { Math.Round(open[i], 4), Math.Round(close[i], 4), Math.Round(low[i], 4), Math.Round(high[i], 4), Math.Round(volVal, 2) });
        }

        return new
        {
            symbol,
            hasDataSource = true,
            dates = datesOut,
            ohlcv = ohlcvOut,
            settlements,
            trades
        };
    }

    private ITimeseriesSource? GetOrCreateTimeseriesSource()
    {
        if (_dataRoot == null) return null;
        if (_sourceInitialized) return _timeseriesSource;

        lock (_sourceLock)
        {
            if (_sourceInitialized) return _timeseriesSource;

            MarketTime? start = _report.Facts.EquityCurve.Count > 0 ? _report.Facts.EquityCurve[0].Time : null;
            MarketTime? end = _report.Facts.EquityCurve.Count > 0 ? _report.Facts.EquityCurve[^1].Time : null;

            // 前后各扩展 30 天方便看买卖点上下文
            MarketTime? bufferedStart = start.HasValue ? ((DateTime)start.Value).AddDays(-30) : null;
            MarketTime? bufferedEnd = end.HasValue ? ((DateTime)end.Value).AddDays(30) : null;

            _timeseriesSource = TimeseriesSourceFactory.Create(_dataRoot, _tableId, bufferedStart, bufferedEnd);
            _sourceInitialized = true;
            return _timeseriesSource;
        }
    }

    private static string FormatTime(DateTime dt) =>
        dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
