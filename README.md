# CarrotQuant.Net

[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4.svg)](https://dotnet.microsoft.com/download)
[![Python](https://img.shields.io/badge/Python-3.8+-3776AB.svg)](https://www.python.org/)

CarrotQuant.Net 是一个基于 .NET 开发的轻量级、高性能量化交易与回测框架。它旨在为开发者提供一个从行情数据下载、处理到策略回测及结果分析的完整工具链。

---

## 🚀 核心特性

- **流式会话 API**: 提供类似 `BacktestingSession.Create().LoadData().Run().Analyze()` 的流畅开发体验。
- **灵活的数据模型**:
  - **HistoryStorage (纵向存储)**: 按股票代码组织的时间序列数据，适合深度技术分析。
  - **MarketStorage (横向存储)**: 按交易日组织的截面数据，适合多因子选股回测。
- **多策略支持**: 支持基于信号（Signal）的策略和显式交易逻辑（Trade）的策略。
- **集成 Python 动力**: 利用 Python 生态系统（BaoStock, EastMoney）进行高效的行情数据抓取，并通过 C# 进行高性能回测。
- **可视化分析**: 集成 `ScottPlot` 生成直观的回测收益曲线及指标分析。
- **高性能引擎**: 支持多线程数据处理，优化的内存管理以应对大规模历史数据。

---

## 📂 项目结构

| 目录/项目 | 说明 |
| :--- | :--- |
| **CarrotBacktesting.NET** | **核心引擎**。包含回测调度、策略接口、数据解析、技术指标及分析组件。 |
| **CarrotQuant.DataLib** | **数据中心库**。负责 C# 与 Python 的交互，管理行情数据的生命周期。 |
| **CarrotQuant.Data.Python** | **数据抓取脚本**。基于 Python 实现，负责从各大金融数据源下载原始 CSV/JSON 数据。 |
| **CarrotBacktesting.NET.Demo** | **回测示例**。展示了如何加载配置、运行策略并产出分析结果。 |
| **CarrotQuant.DataLib.Demo** | **下载示例**。演示如何通过交互式命令行启动行情数据下载流程。 |
| **CarrotQuant.Data** | **默认存储目录**。用于存放下载的原始行情、缓存文件及回测报告。 |

---

## 🛠️ 快速开始

### 1. 环境准备
- 安装 [.NET 8.0 SDK](https://dotnet.microsoft.com/download) 或更高版本。
- 安装 [Python 3.8+](https://www.python.org/) 并安装必要依赖（如 `baostock`, `pandas`）。

### 2. 配置环境
复制并修改 `env.yaml`（或在 `CarrotQuant.Data/v3/yaml/env.yaml` 中配置）：
```yaml
runtime:
  project_dir: "D:/Projects/CarrotQuant.Net"
  thread_count: 8
data:
  raw_path: "CarrotQuant.Data/csv"
```

### 3. 行情下载
运行 `CarrotQuant.DataLib.Demo` 来初始化数据：
```bash
dotnet run --project CarrotQuant.DataLib.Demo
```

### 4. 运行回测
在 `CarrotBacktesting.NET.Demo` 中定义你的策略并运行：
```csharp
BacktestingSession.Create("env.yaml")
    .LoadData()
    .Run(new MySignalStrategy())
    .Analyze();
```

---

## 📊 回测分析图示

回测完成后，框架会产出详细的分析摘要，并支持导出可视化图表。

---

## 📜 开源协议

本项目采用 **Apache License 2.0** 协议开源。详情请参阅 [LICENSE](LICENSE) 文件。

---

*本 README 及其技术建议由 Gemini 3 Flash 提供支持。*