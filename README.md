# CarrotQuant.Net

[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0+-512BD4.svg)](https://dotnet.microsoft.com/download)

CarrotQuant.Net 是一个基于 .NET 开发的轻量级、高性能量化交易与回测框架，采用 **三层分离架构**（数据层 / 引擎层 / 策略层），支持回测与实盘双引擎。

---

## 核心特性

- **三层分离架构**: 数据层 / 引擎层 / 策略层通过接口契约解耦，各层可独立演进。
- **双格式数据源**: 统一支持 CSV 与 Parquet 格式，通过 `IStorageResolver` 屏蔽物理存储差异。
- **稠密矩阵 + 稀疏事件**: `ITimeseriesRegistry` 提供高性能二维时序矩阵（`GetMatrix`）与单标的一维序列（`GetSeries`）访问，`IEventTable` 提供点查与强类型流式 LINQ。
- **泛型事件系统**: 用户只需定义 record 类型，框架自动完成 CSV/Parquet → T 的零配置映射。
- **策略管线**: 支持串联/并联组合模式，引擎启动前通过 `Compile()` 优化拓扑。
- **Context 黑板模式**: 策略间通过 `IEngineContext` 共享状态与数据，实现策略逻辑与交易执行的解耦。

---

## 项目结构

| 目录/项目 | 说明 |
| :--- | :--- |
| **CarrotBacktesting.NET** | **核心引擎**。包含数据层接口与实现、引擎抽象、策略接口。 |
| **CarrotBackTesting.Net.UnitTest** | **单元测试**。覆盖数据层全部组件，含真实测试数据校验。 |
| **CarrotBackTesting.NET.TestData** | **测试数据集**。CSV/Parquet 格式的 A 股行情与复权因子数据。 |
| **CarrotBacktesting.NET.Demo** | **回测示例**。 |

---

## 快速开始

### 1. 环境准备
- 安装 [.NET 10.0 SDK](https://dotnet.microsoft.com/download) 或更高版本。

### 2. 运行测试
```bash
dotnet test CarrotBackTesting.Net.UnitTest
```

### 3. 使用事件系统
```csharp
// 定义事件数据模型
public record AdjustmentFactor(double BackAdjFactor);

// 自动加载（CSV/Parquet 格式自动检测）
var resolver = new StorageResolver("path/to/data");
var table = EventSourceFactory.Create<AdjustmentFactor>(resolver, "ashare.adj_factor.baostock");

// O(1) 点查
table.TryGet(new DateTime(2021, 7, 21), "sh.600000", out var val);

// 注册到事件注册表
var registry = new EventRegistry();
registry.Register("adjustments", table);
```

### 4. 使用静态维表系统
```csharp
// 加载静态维表
var industryTable = FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney");

// 链式点查与全表倒排秒级选股
string industry = industryTable["sh.601958"]["board_name"];
var metalStocks = industryTable.GetKeys("钼"); // 无需记忆列名，全表秒级反查
```

---

## 架构概览

```
Data Layer          Engine Layer         Strategy Layer
┌──────────────┐    ┌─────────────┐    ┌─────────────┐
│ITimeseriesReg│──▶│   IEngine   │──▶│  IStrategy   │
│IEventRegistry│    │IEngineCtx   │    │ IStrategyPipe│
│IFieldRegistry│    └─────────────┘    └─────────────┘
└──────────────┘
```

## 文档导航 (Wiki)

- **[标准研发工作流 (Workflow)](docs/workflow.md)**: 演示从数据准备、YAML 配置、策略编写到驱动引擎的标准流程。
- **[数据存储与格式规范](docs/data_format.md)**: 物理目录组织、Table ID 规范、双时间轴契约与自描述缺省回退机制。
- **[配置管理与环境组织](docs/configuration.md)**: 掌握多态数据外链、跨环境基准路径级联解析与组织规范。
- **[API 契约速查字典](docs/API.md)**: 提供方法级精确签名、参数定义与异常规范（供日常查阅）。
- **[系统架构与研发红线](AGENTS.md)**: 面向 Agent 与核心开发者，涵盖三层分离设计约束与工程研发红线。
- **[v3 历史报表规范归档](CarrotBacktesting.NET/V3/README.md)**: 用于迁移对照历史分析器与专业 Excel 报表规范。

---

## 开源协议

本项目采用 **Apache License 2.0** 协议开源。详情请参阅 [LICENSE](LICENSE) 文件。