# CarrotQuant.Net

[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0+-512BD4.svg)](https://dotnet.microsoft.com/download)

CarrotQuant.Net 是一个基于 .NET 开发的轻量级、高性能量化交易与回测框架，采用 **三层分离架构**（数据层 / 引擎层 / 策略层），支持回测与实盘双引擎。

---

## 核心特性

- **三层分离架构**: 数据层 / 引擎层 / 策略层通过接口契约解耦，各层可独立演进。
- **双格式数据源**: 统一支持 CSV 与 Parquet 格式，通过 `IStorageResolver` 屏蔽物理存储差异。
- **稠密矩阵 + 稀疏事件**: `IDataProvider` 提供高性能二维 Buffer 访问，`IEventProvider<T>` 提供 O(1) 点查。
- **泛型事件系统**: 用户只需定义 record 类型，框架自动完成 CSV/Parquet → T 的零配置映射。
- **策略管线**: 支持串联/并联组合模式，引擎启动前通过 `Compile()` 优化拓扑。
- **Context 黑板模式**: 策略间通过 `IEngineContext` 异步通信，实现无感协作与物理隔离。

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
var provider = EventProviderBuilder.Build<AdjustmentFactor>(resolver, "ashare.adj_factor.baostock");

// O(1) 点查
provider.TryGet(new DateTime(2021, 7, 21), "sh.600000", out var val);

// 注册到事件注册表
var registry = new EventRegistry();
registry.Register("adjustments", provider);
```

---

## 架构概览

```
Data Layer          Engine Layer         Strategy Layer
┌──────────────┐    ┌─────────────┐    ┌─────────────┐
│ IDataProvider│──▶│   IEngine   │──▶│  IStrategy   │
│ IEventReg.   │    │IEngineCtx   │    │ IStrategyPipe│
└──────────────┘    └─────────────┘    └─────────────┘
```

详细架构规范请参阅 [AGENTS.md](AGENTS.md)。

---

## 开源协议

本项目采用 **Apache License 2.0** 协议开源。详情请参阅 [LICENSE](LICENSE) 文件。