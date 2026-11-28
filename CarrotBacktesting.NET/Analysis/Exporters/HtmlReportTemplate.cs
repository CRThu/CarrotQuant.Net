using System.Net;
using System.Text;

namespace CarrotBacktesting.NET.Analysis.Exporters;

/// <summary>
/// 交互式 HTML Tear Sheet 单文件模板构建器。
/// </summary>
internal static class HtmlReportTemplate
{
    public static string Build(string title, bool darkTheme, string echartsScript, string payloadJson)
    {
        string safeTitle = WebUtility.HtmlEncode(title);
        string themeAttr = darkTheme ? "dark" : "light";

        var sb = new StringBuilder(echartsScript.Length + payloadJson.Length + 32768);
        sb.Append(HeadPrefix);
        sb.Append(safeTitle);
        sb.Append(HeadStyle);
        sb.Append($"<body data-theme=\"{themeAttr}\">");
        sb.Append(BodyMarkup);
        sb.Append("<script>\n");
        sb.Append(echartsScript);
        sb.Append("\n</script>\n<script>\nwindow.__TEARSHEET_DATA__ = ");
        sb.Append(payloadJson);
        sb.Append(";\n");
        sb.Append(AppScript);
        sb.Append("\n</script>\n</body>\n</html>");
        return sb.ToString();
    }

    private const string HeadPrefix = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<title>
""";

    private const string HeadStyle = """
</title>
<style>
:root {
  --bg: #0f172a;
  --surface: #1e293b;
  --surface-2: #273449;
  --border: #334155;
  --text: #e2e8f0;
  --text-muted: #94a3b8;
  --accent: #38bdf8;
  --pos: #ef4444;
  --neg: #22c55e;
  --warn: #f59e0b;
  --shadow: 0 4px 20px rgba(0,0,0,0.35);
}
body[data-theme="light"] {
  --bg: #f1f5f9;
  --surface: #ffffff;
  --surface-2: #f8fafc;
  --border: #cbd5e1;
  --text: #0f172a;
  --text-muted: #64748b;
  --accent: #0284c7;
  --pos: #dc2626;
  --neg: #16a34a;
  --warn: #d97706;
  --shadow: 0 2px 12px rgba(15,23,42,0.08);
}
* { box-sizing: border-box; }
body {
  margin: 0;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif;
  background: var(--bg);
  color: var(--text);
  line-height: 1.5;
}
.container { max-width: 1480px; margin: 0 auto; padding: 20px 24px 48px; }
.topbar {
  display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center;
  gap: 12px; padding: 16px 20px; background: var(--surface); border: 1px solid var(--border);
  border-radius: 12px; box-shadow: var(--shadow); margin-bottom: 18px;
}
.topbar h1 { margin: 0; font-size: 20px; font-weight: 700; letter-spacing: 0.2px; }
.meta { font-size: 12px; color: var(--text-muted); margin-top: 4px; }
.actions { display: flex; gap: 8px; align-items: center; }
.btn {
  padding: 6px 12px; border-radius: 8px; border: 1px solid var(--border);
  background: var(--surface-2); color: var(--text); cursor: pointer; font-size: 12px; font-weight: 600;
}
.btn.active, .btn:hover { border-color: var(--accent); color: var(--accent); }
.alert-banner {
  padding: 12px 16px; border-radius: 10px; margin-bottom: 16px;
  border: 1px solid var(--warn); background: rgba(245, 158, 11, 0.12); color: var(--warn);
  font-weight: 600; font-size: 13px;
}
.tabs { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }
.tab-btn {
  padding: 8px 16px; border-radius: 8px; border: 1px solid var(--border);
  background: var(--surface); color: var(--text-muted); cursor: pointer; font-size: 13px; font-weight: 600;
}
.tab-btn.active { background: var(--accent); color: #fff; border-color: var(--accent); }
.panel { display: none; }
.panel.active { display: block; }
.card {
  background: var(--surface); border: 1px solid var(--border); border-radius: 12px;
  padding: 16px 18px; box-shadow: var(--shadow); margin-bottom: 16px;
}
.card-header {
  display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center;
  gap: 8px; margin-bottom: 10px;
}
.card-title { font-size: 15px; font-weight: 700; margin: 0; }
.grid-2 { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }
.kpi-sections { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 14px; margin-bottom: 16px; }
.kpi-card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 14px 16px; box-shadow: var(--shadow); }
.kpi-card h3 { margin: 0 0 10px; font-size: 14px; color: var(--accent); border-bottom: 1px solid var(--border); padding-bottom: 6px; }
.kpi-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px 12px; }
.kpi-item { display: flex; flex-direction: column; }
.kpi-label { font-size: 11px; color: var(--text-muted); }
.kpi-val { font-size: 14px; font-weight: 700; font-variant-numeric: tabular-nums; }
.chart { width: 100%; height: 340px; }
.chart-tall { width: 100%; height: 400px; }
.table-wrap { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: 12px; font-variant-numeric: tabular-nums; }
th, td { padding: 8px 10px; border-bottom: 1px solid var(--border); text-align: right; white-space: nowrap; }
th:first-child, td:first-child, th:nth-child(2), td:nth-child(2) { text-align: left; }
th { color: var(--text-muted); font-weight: 600; background: var(--surface-2); position: sticky; top: 0; cursor: pointer; }
.pos { color: var(--pos); font-weight: 600; }
.neg { color: var(--neg); font-weight: 600; }
.toolbar { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; margin-bottom: 10px; }
.input {
  padding: 6px 10px; border-radius: 8px; border: 1px solid var(--border);
  background: var(--surface-2); color: var(--text); font-size: 12px; min-width: 220px;
}
select.input { min-width: 140px; }
.empty-msg { padding: 28px; text-align: center; color: var(--text-muted); font-size: 13px; }
@media (max-width: 960px) { .grid-2 { grid-template-columns: 1fr; } }
</style>
</head>
""";

    private const string BodyMarkup = """
<div class="container">
  <header class="topbar">
    <div>
      <h1 id="report-title">CarrotQuant 量化绩效 Tear Sheet</h1>
      <div class="meta" id="report-meta"></div>
    </div>
    <div class="actions">
      <button class="btn" id="theme-toggle">切换主题</button>
    </div>
  </header>

  <div id="status-banner" class="alert-banner" style="display:none;"></div>

  <nav class="tabs">
    <button class="tab-btn active" data-tab="overview">组合与风险总览</button>
    <button class="tab-btn" data-tab="trades">交易归因与分布</button>
    <button class="tab-btn" data-tab="signals" id="tab-btn-signals" style="display:none;">Alpha 信号评估</button>
    <button class="tab-btn" data-tab="ledger" id="tab-btn-ledger" style="display:none;">平仓交易明细</button>
    <button class="tab-btn" data-tab="kline" id="tab-btn-kline" style="display:none;">K 线买卖点</button>
  </nav>

  <section id="panel-overview" class="panel active">
    <div id="kpi-sections" class="kpi-sections"></div>

    <div class="card">
      <div class="card-header">
        <h2 class="card-title">净值与基准对比曲线</h2>
        <div class="actions">
          <button class="btn active" data-eq-mode="return">累计收益率 (%)</button>
          <button class="btn" data-eq-mode="equity">绝对净值</button>
          <button class="btn" data-eq-mode="excess" id="btn-excess-mode" style="display:none;">相对超额 (%)</button>
        </div>
      </div>
      <div id="chart-equity" class="chart-tall"></div>
    </div>

    <div class="grid-2">
      <div class="card">
        <div class="card-header"><h2 class="card-title">水下回撤曲线 (Underwater Drawdown)</h2></div>
        <div id="chart-drawdown" class="chart"></div>
      </div>
      <div class="card">
        <div class="card-header"><h2 class="card-title">仓位与保证金敞口时序</h2></div>
        <div id="chart-exposure" class="chart"></div>
      </div>
    </div>

    <div class="grid-2">
      <div class="card">
        <div class="card-header"><h2 class="card-title">自然月度收益率热力图 (%)</h2></div>
        <div id="chart-heatmap" class="chart"></div>
      </div>
      <div class="card">
        <div class="card-header"><h2 class="card-title" id="rolling-title">滚动夏普与滚动波动率</h2></div>
        <div id="chart-rolling" class="chart"></div>
      </div>
    </div>
  </section>

  <section id="panel-trades" class="panel">
    <div class="grid-2">
      <div class="card">
        <div class="card-header"><h2 class="card-title">平仓收益率分布直方图</h2></div>
        <div id="chart-trade-dist" class="chart"></div>
      </div>
      <div class="card">
        <div class="card-header"><h2 class="card-title">月度交易笔数与胜率</h2></div>
        <div id="chart-monthly-trades" class="chart"></div>
      </div>
    </div>

    <div class="grid-2">
      <div class="card">
        <div class="card-header"><h2 class="card-title">标的净盈亏贡献 Top 10 (盈利 vs 亏损)</h2></div>
        <div id="chart-symbol-pnl" class="chart"></div>
      </div>
      <div class="card">
        <div class="card-header"><h2 class="card-title">平仓后卖出时机反思 (Exit Timing)</h2></div>
        <div id="chart-exit-timing" class="chart"></div>
      </div>
    </div>

    <div class="card" id="card-tag-breakdown" style="display:none;">
      <div class="card-header"><h2 class="card-title">策略标签 (Tag) 分组归因</h2></div>
      <div class="table-wrap">
        <table id="table-tags">
          <thead><tr><th>标签</th><th>交易笔数</th><th>胜率</th><th>平均收益</th><th>盈亏比</th><th>盈利因子</th></tr></thead>
          <tbody></tbody>
        </table>
      </div>
    </div>
  </section>

  <section id="panel-signals" class="panel">
    <div class="card">
      <div class="card-header">
        <h2 class="card-title">Alpha 信号前向 T+1..T+N 收益与胜率</h2>
        <div class="actions">
          <select id="signal-group-select" class="input"></select>
          <button class="btn active" data-sig-weight="sample">样本等权</button>
          <button class="btn" data-sig-weight="time">时间等权</button>
        </div>
      </div>
      <div id="chart-signals" class="chart-tall"></div>
    </div>
  </section>

  <section id="panel-ledger" class="panel">
    <div class="card">
      <div class="card-header"><h2 class="card-title">已平仓交易明细流水</h2></div>
      <div class="toolbar">
        <input id="trade-search" class="input" placeholder="按标的代码或标签过滤..." />
        <select id="trade-sort" class="input">
          <option value="exit_desc">按平仓时间倒序</option>
          <option value="pnl_desc">按净盈亏降序</option>
          <option value="pnl_asc">按净盈亏升序</option>
          <option value="ret_desc">按收益率降序</option>
        </select>
        <span id="trade-count-info" class="meta"></span>
      </div>
      <div class="table-wrap">
        <table id="table-trades">
          <thead>
            <tr><th>代码</th><th>方向</th><th>开仓时间</th><th>开仓价</th><th>平仓时间</th><th>平仓价</th><th>数量</th><th>净盈亏</th><th>收益率</th><th>手续费</th><th>持仓步数</th><th>标签</th></tr>
          </thead>
          <tbody></tbody>
        </table>
      </div>
      <div class="toolbar" style="margin-top:10px; justify-content:flex-end;">
        <button class="btn" id="page-prev">上一页</button>
        <span id="page-info" class="meta"></span>
        <button class="btn" id="page-next">下一页</button>
      </div>
    </div>
  </section>

  <section id="panel-kline" class="panel">
    <div class="card">
      <div class="card-header">
        <h2 class="card-title">个股 K 线与买卖点复盘</h2>
        <div class="toolbar" style="margin:0;">
          <select id="kline-symbol-select" class="input"></select>
          <input id="kline-symbol-input" class="input" style="min-width:140px;" placeholder="或手动输入标的代码" />
          <button class="btn active" id="kline-load-btn">加载 K 线</button>
        </div>
      </div>
      <div id="kline-msg" class="meta" style="margin-bottom:8px;"></div>
      <div id="chart-kline" class="chart-tall" style="height:500px;"></div>
    </div>
    <div class="card">
      <div class="card-header"><h2 class="card-title">该标的买卖点与平仓明细</h2></div>
      <div class="table-wrap">
        <table id="table-kline-trades">
          <thead><tr><th>开仓时间</th><th>开仓价</th><th>平仓时间</th><th>平仓价</th><th>方向</th><th>净盈亏</th><th>收益率</th></tr></thead>
          <tbody></tbody>
        </table>
      </div>
    </div>
  </section>
</div>
""";

    private const string AppScript = """
(function() {
  let data = window.__TEARSHEET_DATA__ || {};
  const charts = {};
  let eqMode = 'return';
  let sigWeight = 'sample';
  let sigGroup = 'Total';
  let tradePage = 1;
  let tradeTotalPages = 1;
  const pageSize = 50;

  function cssVar(name) {
    return getComputedStyle(document.body).getPropertyValue(name).trim();
  }

  function escapeHtml(str) {
    return String(str == null ? '' : str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function initChart(id) {
    const el = document.getElementById(id);
    if (!el) return null;
    if (charts[id]) charts[id].dispose();
    const chart = echarts.init(el);
    charts[id] = chart;
    return chart;
  }

  function showEmpty(id, msg) {
    const el = document.getElementById(id);
    if (el) el.innerHTML = '<div class="empty-msg">' + msg + '</div>';
  }

  function hydrateReport() {
  // Header & status
  document.getElementById('report-title').textContent = data.title || 'CarrotQuant Tear Sheet';
  document.getElementById('report-meta').textContent =
    '区间: ' + (data.startTime || '-') + ' ~ ' + (data.endTime || '-') +
    ' | 净值点: ' + (data.totalBars || 0) +
    ' | 平仓笔数: ' + (data.totalTrades || 0) +
    ' | 生成时间: ' + (data.generatedAt || '-');

  if (data.status && data.status !== 'Normal') {
    const banner = document.getElementById('status-banner');
    banner.style.display = 'block';
    banner.textContent = data.status === 'Bankrupt'
      ? '交易状态警告：账户触发穿仓 (Bankrupt)，在途委托已撤销并执行强平。'
      : '交易状态提示：当前会话终局状态为 ' + data.status;
  }

  // KPI cards
  const kpiRoot = document.getElementById('kpi-sections');
  kpiRoot.innerHTML = '';
  (data.kpiSections || []).forEach(sec => {
    const card = document.createElement('div');
    card.className = 'kpi-card';
    const h3 = document.createElement('h3');
    h3.textContent = sec.title || sec.name;
    card.appendChild(h3);
    const grid = document.createElement('div');
    grid.className = 'kpi-grid';
    (sec.metrics || []).forEach(m => {
      const item = document.createElement('div');
      item.className = 'kpi-item';
      const lbl = document.createElement('span');
      lbl.className = 'kpi-label';
      lbl.textContent = m.label;
      const val = document.createElement('span');
      val.className = 'kpi-val';
      val.textContent = m.value;
      item.appendChild(lbl);
      item.appendChild(val);
      grid.appendChild(item);
    });
    card.appendChild(grid);
    kpiRoot.appendChild(card);
  });

  function renderEquityChart() {
    const ts = data.timeSeries || {};
    if (!ts.times || ts.times.length === 0) { showEmpty('chart-equity', '暂无净值时序数据'); return; }
    const chart = initChart('chart-equity');
    const textColor = cssVar('--text');
    const muted = cssVar('--text-muted');
    const border = cssVar('--border');

    const hasBench = Array.isArray(ts.benchmarkReturnsPct) && ts.benchmarkReturnsPct.length === ts.times.length;
    if (hasBench) document.getElementById('btn-excess-mode').style.display = 'inline-block';

    const series = [];
    let yLabel = '%';
    if (eqMode === 'equity') {
      yLabel = '权益';
      series.push({ name: '策略净值', type: 'line', showSymbol: false, data: ts.equities, lineStyle: { width: 2, color: '#38bdf8' } });
    } else if (eqMode === 'excess' && hasBench) {
      series.push({ name: '相对基准超额 (%)', type: 'line', showSymbol: false, data: ts.excessReturnsPct, lineStyle: { width: 2, color: '#f59e0b' }, areaStyle: { opacity: 0.12 } });
    } else {
      series.push({ name: '策略累计收益 (%)', type: 'line', showSymbol: false, data: ts.returnsPct, lineStyle: { width: 2, color: '#38bdf8' }, areaStyle: { opacity: 0.08 } });
      if (hasBench) {
        series.push({ name: '基准累计收益 (%)', type: 'line', showSymbol: false, data: ts.benchmarkReturnsPct, lineStyle: { width: 1.6, type: 'dashed', color: '#94a3b8' } });
      }
    }

    chart.setOption({
      tooltip: { trigger: 'axis' },
      legend: { top: 0, textStyle: { color: textColor } },
      grid: { left: 60, right: 24, top: 36, bottom: 56 },
      xAxis: { type: 'category', data: ts.times, axisLabel: { color: muted } },
      yAxis: { type: 'value', name: yLabel, axisLabel: { color: muted }, splitLine: { lineStyle: { color: border, type: 'dashed' } } },
      dataZoom: [{ type: 'inside' }, { type: 'slider', height: 20, bottom: 10 }],
      series
    });
  }

  function renderDrawdownChart() {
    const ts = data.timeSeries || {};
    if (!ts.times || ts.times.length === 0) { showEmpty('chart-drawdown', '暂无回撤数据'); return; }
    const chart = initChart('chart-drawdown');
    const muted = cssVar('--text-muted');
    const border = cssVar('--border');

    const markPoints = [];
    if (ts.peakTime) markPoints.push({ name: 'Peak', xAxis: ts.peakTime, yAxis: 0, value: 'Peak', itemStyle: { color: '#f59e0b' } });
    if (ts.troughTime) {
      const idx = ts.times.indexOf(ts.troughTime);
      const y = idx >= 0 ? ts.drawdownsPct[idx] : 0;
      markPoints.push({ name: 'MaxDD', xAxis: ts.troughTime, yAxis: y, value: (y != null ? y.toFixed(2) + '%' : 'Trough'), itemStyle: { color: '#ef4444' } });
    }
    if (ts.recoveryTime) markPoints.push({ name: 'Recovery', xAxis: ts.recoveryTime, yAxis: 0, value: 'Recovery', itemStyle: { color: '#22c55e' } });

    chart.setOption({
      tooltip: { trigger: 'axis', valueFormatter: v => (v != null ? v.toFixed(2) + '%' : '-') },
      grid: { left: 56, right: 20, top: 24, bottom: 48 },
      xAxis: { type: 'category', data: ts.times, axisLabel: { color: muted } },
      yAxis: { type: 'value', max: 0, axisLabel: { color: muted, formatter: '{value}%' }, splitLine: { lineStyle: { color: border, type: 'dashed' } } },
      dataZoom: [{ type: 'inside' }],
      series: [{
        name: '回撤', type: 'line', showSymbol: false, data: ts.drawdownsPct,
        lineStyle: { width: 1.5, color: '#ef4444' },
        areaStyle: { color: 'rgba(239,68,68,0.22)' },
        markPoint: markPoints.length ? { symbolSize: 42, data: markPoints } : undefined
      }]
    });
  }

  function renderExposureChart() {
    const ts = data.timeSeries || {};
    if (!ts.times || ts.times.length === 0) { showEmpty('chart-exposure', '暂无仓位敞口数据'); return; }
    const chart = initChart('chart-exposure');
    const textColor = cssVar('--text');
    const muted = cssVar('--text-muted');
    const border = cssVar('--border');

    chart.setOption({
      tooltip: { trigger: 'axis' },
      legend: { top: 0, textStyle: { color: textColor } },
      grid: { left: 56, right: 20, top: 34, bottom: 40 },
      xAxis: { type: 'category', data: ts.times, axisLabel: { color: muted } },
      yAxis: { type: 'value', axisLabel: { color: muted, formatter: '{value}%' }, splitLine: { lineStyle: { color: border, type: 'dashed' } } },
      dataZoom: [{ type: 'inside' }],
      series: [
        { name: '持仓市值占比 (%)', type: 'line', showSymbol: false, data: ts.positionPct, lineStyle: { width: 1.8, color: '#38bdf8' }, areaStyle: { opacity: 0.1 } },
        { name: '保证金占用 (%)', type: 'line', showSymbol: false, data: ts.marginPct, lineStyle: { width: 1.5, color: '#f59e0b' } },
        { name: '可用现金占比 (%)', type: 'line', showSymbol: false, data: ts.cashPct, lineStyle: { width: 1.3, type: 'dashed', color: '#22c55e' } }
      ]
    });
  }

  function renderHeatmapChart() {
    const hm = data.monthlyHeatmap || {};
    if (!hm.years || hm.years.length === 0) { showEmpty('chart-heatmap', '暂无月度收益数据'); return; }
    const chart = initChart('chart-heatmap');
    const textColor = cssVar('--text');
    const muted = cssVar('--text-muted');
    const months = ['1月','2月','3月','4月','5月','6月','7月','8月','9月','10月','11月','12月','全年'];

    chart.setOption({
      tooltip: {
        position: 'top',
        formatter: p => hm.years[p.value[1]] + ' ' + months[p.value[0]] + ': <b>' + p.value[2].toFixed(2) + '%</b>'
      },
      grid: { left: 54, right: 20, top: 18, bottom: 36 },
      xAxis: { type: 'category', data: months, axisLabel: { color: muted }, splitArea: { show: true } },
      yAxis: { type: 'category', data: hm.years, axisLabel: { color: muted }, splitArea: { show: true } },
      visualMap: {
        min: -15, max: 15, show: false,
        inRange: { color: ['#16a34a', '#475569', '#dc2626'] }
      },
      series: [{
        type: 'heatmap',
        data: hm.cells,
        label: { show: true, fontSize: 10, color: textColor, formatter: p => p.value[2].toFixed(1) }
      }]
    });
  }

  function renderRollingChart() {
    const ts = data.timeSeries || {};
    const valid = (ts.rollingSharpe || []).some(v => v != null);
    if (!valid) { showEmpty('chart-rolling', '样本点数不足滚动窗口 (' + (ts.rollingWindow || 60) + ' 步)'); return; }
    document.getElementById('rolling-title').textContent = '滚动夏普与年化波动率 (' + ts.rollingWindow + ' 步)';
    const chart = initChart('chart-rolling');
    const textColor = cssVar('--text');
    const muted = cssVar('--text-muted');
    const border = cssVar('--border');

    chart.setOption({
      tooltip: { trigger: 'axis' },
      legend: { top: 0, textStyle: { color: textColor } },
      grid: { left: 52, right: 52, top: 34, bottom: 40 },
      xAxis: { type: 'category', data: ts.times, axisLabel: { color: muted } },
      yAxis: [
        { type: 'value', name: 'Sharpe', axisLabel: { color: muted }, splitLine: { lineStyle: { color: border, type: 'dashed' } } },
        { type: 'value', name: 'Vol %', axisLabel: { color: muted, formatter: '{value}%' }, splitLine: { show: false } }
      ],
      series: [
        { name: '滚动夏普', type: 'line', showSymbol: false, data: ts.rollingSharpe, lineStyle: { width: 1.8, color: '#a855f7' } },
        { name: '滚动波动率 (%)', type: 'line', yAxisIndex: 1, showSymbol: false, data: ts.rollingVolatilityPct, lineStyle: { width: 1.4, color: '#f59e0b' } }
      ]
    });
  }

  function renderTradesTab() {
    const dist = data.tradeDistribution || {};
    if (!dist.labels || dist.labels.length === 0) showEmpty('chart-trade-dist', '暂无平仓交易记录');
    else {
      const c1 = initChart('chart-trade-dist');
      c1.setOption({
        tooltip: { trigger: 'axis' },
        grid: { left: 48, right: 20, top: 20, bottom: 52 },
        xAxis: { type: 'category', data: dist.labels, axisLabel: { color: cssVar('--text-muted'), rotate: 30, fontSize: 10 } },
        yAxis: { type: 'value', axisLabel: { color: cssVar('--text-muted') } },
        series: [{
          type: 'bar',
          data: dist.counts.map((v, i) => ({ value: v, itemStyle: { color: dist.isPositive[i] ? '#ef4444' : '#22c55e' } }))
        }]
      });
    }

    const mt = data.monthlyTrades || {};
    if (!mt.months || mt.months.length === 0) showEmpty('chart-monthly-trades', '暂无月度交易记录');
    else {
      const c2 = initChart('chart-monthly-trades');
      c2.setOption({
        tooltip: { trigger: 'axis' },
        legend: { top: 0, textStyle: { color: cssVar('--text') } },
        grid: { left: 48, right: 48, top: 34, bottom: 40 },
        xAxis: { type: 'category', data: mt.months, axisLabel: { color: cssVar('--text-muted') } },
        yAxis: [
          { type: 'value', name: '笔数', axisLabel: { color: cssVar('--text-muted') } },
          { type: 'value', name: '%', axisLabel: { color: cssVar('--text-muted'), formatter: '{value}%' }, splitLine: { show: false } }
        ],
        series: [
          { name: '交易笔数', type: 'bar', data: mt.counts, itemStyle: { color: '#38bdf8' } },
          { name: '胜率 (%)', type: 'line', yAxisIndex: 1, data: mt.winRatesPct, lineStyle: { color: '#22c55e' } },
          { name: '平均收益 (%)', type: 'line', yAxisIndex: 1, data: mt.avgReturnsPct, lineStyle: { color: '#f59e0b' } }
        ]
      });
    }

    const sp = data.symbolPnl || {};
    const combined = [...(sp.topLosers || []), ...(sp.topWinners || [])];
    if (combined.length === 0) showEmpty('chart-symbol-pnl', '暂无标的盈亏贡献数据');
    else {
      const c3 = initChart('chart-symbol-pnl');
      c3.setOption({
        tooltip: { trigger: 'axis' },
        grid: { left: 90, right: 24, top: 16, bottom: 28 },
        xAxis: { type: 'value', axisLabel: { color: cssVar('--text-muted') } },
        yAxis: { type: 'category', data: combined.map(x => x.symbol), axisLabel: { color: cssVar('--text-muted') } },
        series: [{
          type: 'bar',
          data: combined.map(x => ({ value: x.netPnL, itemStyle: { color: x.netPnL >= 0 ? '#ef4444' : '#22c55e' } }))
        }]
      });
    }

    const et = data.exitTiming;
    if (!et || !et.steps || et.steps.length === 0) showEmpty('chart-exit-timing', '未启用或无 Exit Timing 数据');
    else {
      const c4 = initChart('chart-exit-timing');
      c4.setOption({
        tooltip: { trigger: 'axis' },
        legend: { top: 0, textStyle: { color: cssVar('--text') } },
        grid: { left: 48, right: 48, top: 34, bottom: 36 },
        xAxis: { type: 'category', data: et.steps, axisLabel: { color: cssVar('--text-muted') } },
        yAxis: [
          { type: 'value', name: '后续收益 %', axisLabel: { color: cssVar('--text-muted'), formatter: '{value}%' } },
          { type: 'value', name: '上涨概率 %', axisLabel: { color: cssVar('--text-muted'), formatter: '{value}%' }, splitLine: { show: false } }
        ],
        series: [
          { name: '平均后续收益 (%)', type: 'line', data: et.avgReturnsPct, lineStyle: { color: '#38bdf8' } },
          { name: '中位数后续收益 (%)', type: 'line', data: et.medianReturnsPct, lineStyle: { color: '#f59e0b' } },
          { name: '后续上涨概率 (%)', type: 'line', yAxisIndex: 1, data: et.winRatesPct, lineStyle: { type: 'dashed', color: '#22c55e' } }
        ]
      });
    }

    const tags = data.tagBreakdowns || [];
    if (tags.length > 0) {
      document.getElementById('card-tag-breakdown').style.display = 'block';
      const tbody = document.querySelector('#table-tags tbody');
      tbody.innerHTML = tags.map(t =>
        '<tr><td>' + escapeHtml(t.tag) + '</td><td>' + t.trades + '</td><td>' + (t.winRatePct ?? 0).toFixed(2) + '%</td><td class="' +
        ((t.avgReturnPct ?? 0) >= 0 ? 'pos' : 'neg') + '">' + (t.avgReturnPct ?? 0).toFixed(2) + '%</td><td>' +
        (t.winLossRatio ?? 0).toFixed(2) + '</td><td>' + (t.profitFactor ?? 0).toFixed(2) + '</td></tr>'
      ).join('');
    }
  }

  function renderSignalsTab() {
    const sig = data.signals;
    if (!sig || !sig.groups) return;
    const grp = sig.groups[sigGroup] || sig.groups['Total'];
    if (!grp) return;

    const c = initChart('chart-signals');
    const avg = sigWeight === 'time' ? grp.timeAvgPct : grp.sampleAvgPct;
    const med = sigWeight === 'time' ? grp.timeMedianPct : grp.sampleMedianPct;
    const win = sigWeight === 'time' ? grp.timeWinRatePct : grp.sampleWinRatePct;

    c.setOption({
      tooltip: { trigger: 'axis' },
      legend: { top: 0, textStyle: { color: cssVar('--text') } },
      grid: { left: 52, right: 52, top: 36, bottom: 36 },
      xAxis: { type: 'category', data: grp.steps, axisLabel: { color: cssVar('--text-muted') } },
      yAxis: [
        { type: 'value', name: '收益 %', axisLabel: { color: cssVar('--text-muted'), formatter: '{value}%' } },
        { type: 'value', name: '胜率 %', axisLabel: { color: cssVar('--text-muted'), formatter: '{value}%' }, splitLine: { show: false } }
      ],
      series: [
        { name: '平均收益率 (%)', type: 'line', data: avg, lineStyle: { width: 2, color: '#38bdf8' } },
        { name: '中位数收益率 (%)', type: 'line', data: med, lineStyle: { width: 2, color: '#f59e0b' } },
        { name: '胜率 (%)', type: 'line', yAxisIndex: 1, data: win, lineStyle: { width: 1.8, type: 'dashed', color: '#22c55e' } }
      ]
    });
  }

  async function renderTradeTable() {
    const q = (document.getElementById('trade-search').value || '').trim();
    const sortMode = document.getElementById('trade-sort').value;

    let slice = [];
    let totalCount = 0;

    if (data.serverMode) {
      const url = '/api/trades?page=' + tradePage + '&size=' + pageSize + '&q=' + encodeURIComponent(q) + '&sort=' + encodeURIComponent(sortMode);
      const resp = await fetch(url);
      const res = await resp.json();
      totalCount = res.total || 0;
      slice = res.items || [];
      tradeTotalPages = Math.max(1, Math.ceil(totalCount / pageSize));
      document.getElementById('trade-count-info').textContent = '共 ' + totalCount + ' 笔（点击行可查看 K 线）';
    } else {
      const rows = data.tradeRows || [];
      const ql = q.toLowerCase();
      let filtered = rows.filter(r => !ql || r.symbol.toLowerCase().includes(ql) || (r.orderTag || '').toLowerCase().includes(ql));
      if (sortMode === 'pnl_desc') filtered.sort((a, b) => b.netPnL - a.netPnL);
      else if (sortMode === 'pnl_asc') filtered.sort((a, b) => a.netPnL - b.netPnL);
      else if (sortMode === 'ret_desc') filtered.sort((a, b) => b.returnPct - a.returnPct);
      else filtered.sort((a, b) => b.exitTime.localeCompare(a.exitTime));

      totalCount = filtered.length;
      tradeTotalPages = Math.max(1, Math.ceil(totalCount / pageSize));
      if (tradePage > tradeTotalPages) tradePage = tradeTotalPages;
      slice = filtered.slice((tradePage - 1) * pageSize, tradePage * pageSize);
      document.getElementById('trade-count-info').textContent = data.tradesTruncated
        ? ('共 ' + totalCount + ' 笔（静态页仅保留最近 ' + rows.length + ' 笔）')
        : ('共 ' + totalCount + ' 笔');
    }

    document.getElementById('page-info').textContent = tradePage + ' / ' + tradeTotalPages;
    const tbody = document.querySelector('#table-trades tbody');
    tbody.innerHTML = slice.map(r =>
      '<tr data-symbol="' + escapeHtml(r.symbol) + '" style="' + (data.serverMode ? 'cursor:pointer;' : '') + '"><td>' + escapeHtml(r.symbol) + '</td><td>' + escapeHtml(r.side) + '</td><td>' + escapeHtml(r.entryTime) + '</td><td>' + (r.entryPrice ?? 0).toFixed(4) +
      '</td><td>' + escapeHtml(r.exitTime) + '</td><td>' + (r.exitPrice ?? 0).toFixed(4) + '</td><td>' + (r.quantity ?? 0) +
      '</td><td class="' + ((r.netPnL ?? 0) >= 0 ? 'pos' : 'neg') + '">' + (r.netPnL ?? 0).toFixed(2) +
      '</td><td class="' + ((r.returnPct ?? 0) >= 0 ? 'pos' : 'neg') + '">' + (r.returnPct ?? 0).toFixed(2) + '%' +
      '</td><td>' + (r.commission ?? 0).toFixed(2) + '</td><td>' + (r.holdingSteps ?? 0) + '</td><td>' + escapeHtml(r.orderTag || '-') + '</td></tr>'
    ).join('');

    if (data.serverMode) {
      tbody.querySelectorAll('tr').forEach(tr => {
        tr.addEventListener('click', () => {
          const sym = tr.getAttribute('data-symbol');
          if (sym) openKlineForSymbol(sym);
        });
      });
    }
  }

  async function initKlineSymbols() {
    if (!data.serverMode) return;
    document.getElementById('tab-btn-kline').style.display = 'inline-block';
    const resp = await fetch('/api/symbols');
    const res = await resp.json();
    const sel = document.getElementById('kline-symbol-select');
    sel.innerHTML = '';
    (res.symbols || []).forEach(s => {
      const opt = document.createElement('option');
      opt.value = s;
      opt.textContent = s;
      sel.appendChild(opt);
    });
  }

  function openKlineForSymbol(sym) {
    document.getElementById('kline-symbol-input').value = sym;
    const sel = document.getElementById('kline-symbol-select');
    if (Array.from(sel.options).some(o => o.value === sym)) sel.value = sym;
    document.querySelector('[data-tab="kline"]').click();
    loadKline(sym);
  }

  async function loadKline(symbol) {
    if (!symbol) return;
    const msgEl = document.getElementById('kline-msg');
    msgEl.textContent = '正在加载 ' + symbol + ' ...';
    const resp = await fetch('/api/kline?symbol=' + encodeURIComponent(symbol));
    const res = await resp.json();

    const kTbody = document.querySelector('#table-kline-trades tbody');
    kTbody.innerHTML = (res.trades || []).map(t =>
      '<tr><td>' + escapeHtml(t.entryTime) + '</td><td>' + (t.entryPrice ?? 0).toFixed(4) +
      '</td><td>' + escapeHtml(t.exitTime) + '</td><td>' + (t.exitPrice ?? 0).toFixed(4) +
      '</td><td>' + escapeHtml(t.side) + '</td><td class="' + ((t.pnl ?? 0) >= 0 ? 'pos' : 'neg') + '">' + (t.pnl ?? 0).toFixed(2) +
      '</td><td class="' + ((t.returnPct ?? 0) >= 0 ? 'pos' : 'neg') + '">' + (t.returnPct ?? 0).toFixed(2) + '%</td></tr>'
    ).join('');

    if (!res.dates || res.dates.length === 0) {
      msgEl.textContent = res.message || '无 K 线行情数据';
      showEmpty('chart-kline', msgEl.textContent);
      return;
    }

    msgEl.textContent = symbol + ' 共 ' + res.dates.length + ' 根 K 线 | 交割信号 ' + (res.settlements || []).length + ' 次';
    const chart = initChart('chart-kline');
    const textColor = cssVar('--text');
    const muted = cssVar('--text-muted');
    const border = cssVar('--border');

    const kData = res.ohlcv.map(x => [x[0], x[1], x[2], x[3]]);
    const vData = res.ohlcv.map((x, idx) => ({ value: x[4], itemStyle: { color: x[1] >= x[0] ? '#ef4444' : '#22c55e' } }));

    const markPoints = (res.settlements || []).map(s => {
      const idx = res.dates.indexOf(s.time);
      const yVal = idx >= 0 ? (s.direction === 'B' ? res.ohlcv[idx][2] : res.ohlcv[idx][3]) : s.price;
      return {
        name: s.direction,
        coord: [s.time, yVal],
        value: s.direction + ' @ ' + s.price.toFixed(2),
        symbol: s.direction === 'B' ? 'arrow' : 'pin',
        symbolSize: 28,
        itemStyle: { color: s.direction === 'B' ? '#ef4444' : '#22c55e' }
      };
    });

    chart.setOption({
      tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
      axisPointer: { link: [{ xAxisIndex: 'all' }] },
      grid: [
        { left: 60, right: 24, top: 28, height: '58%' },
        { left: 60, right: 24, top: '72%', height: '16%' }
      ],
      xAxis: [
        { type: 'category', data: res.dates, axisLabel: { color: muted } },
        { type: 'category', gridIndex: 1, data: res.dates, axisLabel: { show: false } }
      ],
      yAxis: [
        { scale: true, axisLabel: { color: muted }, splitLine: { lineStyle: { color: border, type: 'dashed' } } },
        { scale: true, gridIndex: 1, splitNumber: 2, axisLabel: { color: muted } }
      ],
      dataZoom: [
        { type: 'inside', xAxisIndex: [0, 1] },
        { type: 'slider', xAxisIndex: [0, 1], bottom: 8, height: 18 }
      ],
      series: [
        {
          name: symbol,
          type: 'candlestick',
          data: kData,
          itemStyle: { color: '#ef4444', color0: '#22c55e', borderColor: '#ef4444', borderColor0: '#22c55e' },
          markPoint: markPoints.length ? { data: markPoints, label: { color: '#fff', fontSize: 10 } } : undefined
        },
        { name: '成交量', type: 'bar', xAxisIndex: 1, yAxisIndex: 1, data: vData }
      ]
    });
  }

  function setupTabsAndData() {
  // Conditional tabs
  if (data.signals && data.signals.groups) {
    document.getElementById('tab-btn-signals').style.display = 'inline-block';
    const sel = document.getElementById('signal-group-select');
    sel.innerHTML = '';
    Object.keys(data.signals.groups).forEach(k => {
      const opt = document.createElement('option');
      opt.value = k;
      opt.textContent = k + ' (' + data.signals.groups[k].totalSignals + ')';
      sel.appendChild(opt);
    });
    sel.addEventListener('change', e => { sigGroup = e.target.value; renderSignalsTab(); });
  }
  if (data.serverMode || (data.tradeRows || []).length > 0) {
    document.getElementById('tab-btn-ledger').style.display = 'inline-block';
  }
  }

  // Events
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.panel').forEach(p => p.classList.remove('active'));
      btn.classList.add('active');
      const tab = btn.dataset.tab;
      document.getElementById('panel-' + tab).classList.add('active');
      if (tab === 'overview') { renderEquityChart(); renderDrawdownChart(); renderExposureChart(); renderHeatmapChart(); renderRollingChart(); }
      else if (tab === 'trades') renderTradesTab();
      else if (tab === 'signals') renderSignalsTab();
      else if (tab === 'ledger') renderTradeTable();
      else if (tab === 'kline') {
        const sym = (document.getElementById('kline-symbol-input').value || document.getElementById('kline-symbol-select').value || '').trim();
        if (sym && !charts['chart-kline']) loadKline(sym);
      }
      Object.values(charts).forEach(c => c && c.resize());
    });
  });

  document.getElementById('kline-load-btn').addEventListener('click', () => {
    const sym = (document.getElementById('kline-symbol-input').value || document.getElementById('kline-symbol-select').value || '').trim();
    if (sym) loadKline(sym);
  });
  document.getElementById('kline-symbol-select').addEventListener('change', e => {
    document.getElementById('kline-symbol-input').value = e.target.value;
    loadKline(e.target.value);
  });

  document.querySelectorAll('[data-eq-mode]').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('[data-eq-mode]').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      eqMode = btn.dataset.eqMode;
      renderEquityChart();
    });
  });

  document.querySelectorAll('[data-sig-weight]').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('[data-sig-weight]').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      sigWeight = btn.dataset.sigWeight;
      renderSignalsTab();
    });
  });

  document.getElementById('theme-toggle').addEventListener('click', () => {
    const next = document.body.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    document.body.setAttribute('data-theme', next);
    renderEquityChart(); renderDrawdownChart(); renderExposureChart(); renderHeatmapChart(); renderRollingChart();
    if (document.getElementById('panel-trades').classList.contains('active')) renderTradesTab();
    if (document.getElementById('panel-signals').classList.contains('active')) renderSignalsTab();
  });

  document.getElementById('trade-search').addEventListener('input', () => { tradePage = 1; renderTradeTable(); });
  document.getElementById('trade-sort').addEventListener('change', () => { tradePage = 1; renderTradeTable(); });
  document.getElementById('page-prev').addEventListener('click', () => { if (tradePage > 1) { tradePage--; renderTradeTable(); } });
  document.getElementById('page-next').addEventListener('click', () => { if (tradePage < tradeTotalPages) { tradePage++; renderTradeTable(); } });
  window.addEventListener('resize', () => Object.values(charts).forEach(c => c && c.resize()));

  // Initial render
  async function bootstrap() {
    if (data.serverMode) {
      const resp = await fetch('/api/summary');
      data = await resp.json();
      await initKlineSymbols();
    }
    hydrateReport();
    setupTabsAndData();
    renderEquityChart();
    renderDrawdownChart();
    renderExposureChart();
    renderHeatmapChart();
    renderRollingChart();
  }
  bootstrap();
})();
""";
}
