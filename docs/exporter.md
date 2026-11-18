# 报表导出与呈现子系统开发指南 (Report & Exporter Subsystem)

本文档系统介绍 CarrotQuant.NET 中**报表导出与呈现子系统**（`CarrotBacktesting.NET/Analysis/Exporters/` 与 `CarrotBacktesting.NET/Abstraction/Analysis/IReportExporter.cs`）的架构设计、核心契约、自描述指标流驱动机制、内置 `ConsoleExporter` 的配置与使用，以及自定义导出器的开发范式。

---

## 目录
- [一、架构总览与解耦设计](#一架构总览与解耦设计)
- [二、核心抽象契约 (IReportExporter)](#二核心抽象契约-ireportexporter)
- [三、自描述指标流与零耦合渲染机制](#三自描述指标流与零耦合渲染机制)
- [四、控制台与深色 HTML 导出器 (ConsoleExporter)](#四控制台与深色-html-导出器-consoleexporter)
  - [1. 快速使用与典型范式](#1-快速使用与典型范式)
  - [2. 完整配置选项对照 (ConsoleExporterOptions)](#2-完整配置选项对照-consoleexporteroptions)
  - [3. 预设模式 (SummaryOnly)](#3-预设模式-summaryonly)
  - [4. 深色 HTML 报表持久化](#4-深色-html-报表持久化)
- [五、自定义导出器扩展实现范式](#五自定义导出器扩展实现范式)
  - [1. JSON 结构化报表导出器 (JsonReportExporter)](#1-json-结构化报表导出器-jsonreportexporter)
  - [2. CSV 明细导出器 (CsvReportExporter)](#2-csv-明细导出器-csvreportexporter)

---

## 一、架构总览与解耦设计

在现代量化框架中，绩效分析计算与报表呈现属于两个不同维度的关注点：
- **分析层（Analysis Engine）**：专注于纯数学统计、风险归因与特征分析，输入 `TradingResult`，输出只读聚合根 `AnalysisReport`；
- **表现层（Exporter Subsystem）**：专注于各种交付介质（终端控制台、静态 HTML 网页、Excel/CSV 表格、Webhook 推送）的版式排版、着色与持久化。

CarrotQuant.NET 遵循**表现层物理隔离**原则，分析内核完全不依赖任何具体的控制台库、文件 I/O 或绘图组件：

```mermaid
graph TD
    AnalysisEngine[AnalysisEngine 纯分析引擎] -->|产出不可变报告| Report[AnalysisReport 综合分析报告]

    subgraph Exporters [报表导出与呈现子系统 (IReportExporter)]
        Report --> Console[ConsoleExporter<br>Spectre.Console 彩色终端仪表盘]
        Report --> Html[ConsoleExporter<br>深色主题 HTML 报表持久化]
        Report --> Json[JsonReportExporter<br>结构化 JSON 交付]
        Report --> Custom[第三方自定义 Exporter<br>CSV / PDF / 钉钉 Webhook]
    end
```

---

## 二、核心抽象契约 (IReportExporter)

报表导出契约位于 `CarrotBacktesting.NET.Abstraction.Analysis`，仅暴露单一消费入口：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 报表导出器统一抽象契约。
/// 实现类仅拥有对不可变 AnalysisReport 的只读消费权，负责将指标数据投射到具体介质。
/// </summary>
public interface IReportExporter
{
    void Export(AnalysisReport report);
}
```

### 设计准则
1. **只读消费与零副作用**：导出器严禁反向修改 `AnalysisReport` 或底层 `TradingResult` 的任何状态，确保分析事实不可变。
2. **介质自由可插拔**：上层工作流（CLI、自动化调度、脚本）可通过依赖注入无缝切换导出目标，核心分析代码保持零改动。

---

## 三、自描述指标流与零耦合渲染机制

传统量化系统的报表渲染器往往需要为每个新指标硬编码一行取值与格式化逻辑。当策略增加自定义指标（如 Brinson 行业归因或流动性分析）时，必须修改渲染器源码。

CarrotQuant.NET 采用**自描述指标流（Self-Describing Metrics Stream）**机制彻底消除该耦合：

### 1. 指标自描述契约 (`MetricItem` & `GetMetrics`)
所有实现 `IAnalysisResult` 的分析产物（包括官方核心五大统计与外部扩展）均通过 `GetMetrics()` 输出自描述键值序列：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Analysis;

public readonly record struct MetricItem(
    string Label,        // 指标显示名称（如 "夏普比率 (Sharpe Ratio)"）
    string Value,        // 格式化后的指标文本（如 "1.85"）
    string? Category = null // 指标分组归属（如 "风险收益比"）
);

public interface IAnalysisResult
{
    string Name { get; }
    string Title { get; }
    IEnumerable<MetricItem> GetMetrics();
}
```

### 2. 导出器通用网格化渲染
导出器渲染外部扩展模型时，只需遍历 `result.GetMetrics()` 并按 `Category` 进行网格分组，即可自适应完成排版渲染：

```csharp
// 任何实现了 IAnalysisResult 的第三方分析结果，均可自动在导出器中展示：
foreach (var result in report.Results)
{
    if (result is PortfolioStatistics or CapitalStatistics or TradeStatistics or CostStatistics or SignalStatistics)
    {
        continue; // 官方指标走定制化多维布局
    }

    // 外部扩展模型自动网格通排通显：
    var grid = new Grid();
    foreach (var group in result.GetMetrics().GroupBy(m => m.Category ?? "常规指标"))
    {
        // 自动按分组构建行与列
    }
}
```

---

## 四、控制台与深色 HTML 导出器 (ConsoleExporter)

`ConsoleExporter`（位于 `CarrotBacktesting.NET.Analysis.Exporters`）是系统内置的高性能交互式导出器，基于 `Spectre.Console` 构建，支持 ANSI 终端彩色排版与深色主题 HTML 报表文件持久化。

### 1. 快速使用与典型范式

```csharp
using CarrotBacktesting.NET.Analysis;
using CarrotBacktesting.NET.Analysis.Exporters;

// 1. 全局默认导出：终端打印全量四层量化与信号评估表格
ConsoleExporter.Default.Export(report);

// 2. 按需开关控制：关闭不关心的模块，并生成独立 HTML 报表文件
var options = new ConsoleExporterOptions
{
    ShowCapital = false,       // 屏蔽资本利用模块
    ShowCost = false,          // 屏蔽交易成本模块
    SaveHtml = true,           // 开启 HTML 持久化
    HtmlOutputPath = "output/summary.html"
};
new ConsoleExporter(options).Export(report);
```

### 2. 完整配置选项对照 (ConsoleExporterOptions)

`ConsoleExporterOptions` 采用纯属性记录定义，天然兼容 YAML / JSON 声明式反序列化映射：

| 配置属性 (Property) | YAML 键名 (Snake Case) | 默认值 | 对应层级与说明 |
| :--- | :--- | :---: | :--- |
| `ShowPortfolio` | `show_portfolio` | `true` | 第一层：组合净值与风险统计（累计收益、CAGR、波动率、夏普、索提诺、卡玛、VaR、Beta、Alpha 等） |
| `ShowMonthlyReturns` | `show_monthly_returns` | `true` | 第一层扩展：逐月收益率分布矩阵（依赖 `ShowPortfolio`） |
| `ShowCapital` | `show_capital` | `true` | 第二层：资本利用与敞口分析（平均/最大仓位、保证金占用、现金闲置率、年化换手率） |
| `ShowTrades` | `show_trades` | `true` | 第三层：交易行为与执行归因（交易笔数、胜率、多空对比、盈亏比、盈利因子、连胜连败、持仓周期） |
| `ShowTradeEfficiency` | `show_trade_efficiency` | `true` | 第三层扩展：多空对称最大有利偏离度 (MFE) 捕获效率（依赖 `ShowTrades`） |
| `ShowExitTiming` | `show_exit_timing` | `true` | 第三层扩展：卖点时机反思与踏空走势 T+1..T+N（依赖 `ShowTrades`） |
| `ShowTradeTags` | `show_trade_tags` | `true` | 第三层扩展：策略标签 (Tag) 下钻分组归因（依赖 `ShowTrades`） |
| `ShowCost` | `show_cost` | `true` | 第四层：现实成本与摩擦损耗（手续费、滑点、综合摩擦、费毛比、单笔摩擦） |
| `ShowSignals` | `show_signals` | `true` | 第五层：Alpha 信号前向收益分布评估（最优前向持有周期、每日收益均值/中位数/胜率/盈亏比） |
| `ShowSignalGroups` | `show_signal_groups` | `true` | 第五层扩展：Alpha 信号分组下钻分析（依赖 `ShowSignals`） |
| `ShowExtensions` | `show_extensions` | `true` | 第三方外部扩展分析器自描述结果网格渲染开关 |
| `SaveHtml` | `save_html` | `false` | 是否在渲染的同时生成深色主题 HTML 报表文件 |
| `HtmlOutputPath` | `html_output_path` | `"summary.html"` | HTML 报表持久化文件路径 |

### 3. 预设模式 (SummaryOnly)

对于高频网格搜索或多进程参数优化场景，终端不宜输出过多下钻细节。可直接选用 `SummaryOnly` 极简预设：

```csharp
// 自动关闭逐月收益矩阵、MFE 效率、Exit Timing 及 Tag 分桶，仅输出核心概要
new ConsoleExporter(ConsoleExporterOptions.SummaryOnly).Export(report);
```

### 4. 深色 HTML 报表持久化

当 `SaveHtml = true` 时，`ConsoleExporter` 会通过 `AnsiConsole.CreateRecorder()` 捕获全量渲染字节，并在控制台输出完成后将富文本转录为自带独立 CSS 样式的单文件深色 HTML：
- 零外部 Web 依赖（无外部 CDN、无 JS 运行时，邮件或本地浏览器双击即可查看）；
- 完整保留终端的字体、颜色、边框与表格布局。

---

## 五、自定义导出器扩展实现范式

当需要将分析结果对接下游数据库、数据管道或告警通知时，只需实现 `IReportExporter` 接口。

### 1. JSON 结构化报表导出器 (JsonReportExporter)

以下代码展示了如何消费 `AnalysisReport.Results` 与 `GetMetrics()` 快速构建通用的 JSON 报表落盘器：

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CarrotBacktesting.NET.Abstraction.Analysis;

namespace CarrotBacktesting.NET.Analysis.Exporters;

public sealed class JsonReportExporter : IReportExporter
{
    private readonly string _outputPath;

    public JsonReportExporter(string outputPath = "report.json")
    {
        _outputPath = outputPath;
    }

    public void Export(AnalysisReport report)
    {
        var exportDict = new Dictionary<string, object>();

        foreach (var result in report.Results)
        {
            var metricsDict = new Dictionary<string, string>();
            foreach (var metric in result.GetMetrics())
            {
                metricsDict[metric.Label] = metric.Value;
            }

            exportDict[result.Name] = new
            {
                Title = result.Title,
                Metrics = metricsDict
            };
        }

        string json = JsonSerializer.Serialize(exportDict, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_outputPath, json);
    }
}
```

### 2. CSV 明细导出器 (CsvReportExporter)

将所有指标平铺为 CSV 键值对，便于直接导入 Excel 或数据分析工具：

```csharp
using System.IO;
using System.Text;
using CarrotBacktesting.NET.Abstraction.Analysis;

namespace CarrotBacktesting.NET.Analysis.Exporters;

public sealed class CsvReportExporter : IReportExporter
{
    private readonly string _outputPath;

    public CsvReportExporter(string outputPath = "metrics.csv")
    {
        _outputPath = outputPath;
    }

    public void Export(AnalysisReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Module,Category,Metric,Value");

        foreach (var result in report.Results)
        {
            foreach (var metric in result.GetMetrics())
            {
                sb.AppendLine($"\"{result.Name}\",\"{metric.Category ?? "常规"}\",\"{metric.Label}\",\"{metric.Value}\"");
            }
        }

        File.WriteAllText(_outputPath, sb.ToString(), Encoding.UTF8);
    }
}
```
