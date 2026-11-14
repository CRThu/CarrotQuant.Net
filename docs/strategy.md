# 策略子系统与管线开发指南 (Strategy & Pipeline Guide)

本文档系统介绍 CarrotQuant .NET 的策略子系统架构、核心契约分层、通用强类型黑板协作机制、串并行组合管线编排、Roslyn 动态免构建加载以及基于 YAML 配置文件驱动的装配流程。

---

## 目录
- [一、策略架构总览与分层设计](#一策略架构总览与分层设计)
- [二、核心契约体系 (Core Contracts)](#二核心契约体系-core-contracts)
- [三、通用强类型黑板模式 (IMarketState)](#三通用强类型黑板模式-imarketstate)
- [四、组合策略管线体系 (Composite Pipeline)](#四组合策略管线体系-composite-pipeline)
- [五、三种策略装配与开发范式](#五三种策略装配与开发范式)
- [六、时序变更感知与事件到达探测](#六时序变更感知与事件到达探测)
- [七、经典实战策略编写范例](#七经典实战策略编写范例)

---

## 一、策略架构总览与分层设计

在严肃量化投研与生产交易中，将数学特征、交易信号、资金分配与报单风控混杂在单一巨石类中是常见的架构反模式。CarrotQuant .NET 确立**四层工业级解耦**模型，通过统一抽象与组合流水线实现各环节职责单一：

```mermaid
flowchart TD
    Engine["回测/实盘引擎 (IEngine)"] -->|每时间步触发 OnUpdate| MasterPipeline["组合策略管线 (StrategyPipeline : IStrategy)"]

    subgraph MasterPipeline [组合策略管线 StrategyPipeline]
        subgraph Stage1 [阶段 1：特征计算与多信号生产 (可选用 ParallelPipeline 多核并发)]
            S1["Alpha 因子 A<br>(动量/波动率)"]
            S2["Alpha 因子 B<br>(均值回归/价差)"]
            S3["Alpha 因子 C<br>(微观订单簿不平衡)"]
        end

        subgraph Stage2 [阶段 2：策略融合与持仓规划 (SequentialPipeline 串行有序)]
            PM["组合规划策略 (Portfolio Strategy)<br>读取多信号打分，计算目标持仓 TargetPositions"]
        end

        subgraph Stage3 [阶段 3：分仓调仓与风控执行 (SequentialPipeline 串行有序)]
            EX["交易风控策略 (Execution & Risk)<br>校验合规与流动性，向 IBroker 发送物理报单"]
        end

        Stage1 -->|写入强类型信号至 context.Market| Stage2
        Stage2 -->|目标持仓写入 context.Market| Stage3
    end

    Stage3 -->|提交订单 SubmitOrder| Broker["虚拟撮合 / 实盘交易所网关 (IBroker)"]
```

- **阶段内高并发 (Parallel)**：相互独立的 Alpha 因子无因果依赖，通过多核 CPU 并发执行计算；
- **阶段间强时序 (Sequential)**：特征计算 $\rightarrow$ 信号融合 $\rightarrow$ 持仓规划 $\rightarrow$ 风控报单，按严格顺序单调执行，消除状态竞态；
- **对外契约一致**：整个流水线对引擎呈现为单一标准的 `IStrategy`，回测与实盘平滑复用。

---

## 二、核心契约体系 (Core Contracts)

所有接口统一位于 `CarrotBacktesting.NET.Abstraction.Strategy` 命名空间。

### 1. `IStrategy`（统一顶层契约）
作为引擎时间步驱动的统领入口，单一职责，无特定业务预设：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Strategy;

public interface IStrategy
{
    // 策略唯一标识
    string Name { get; }

    // 历史时序最大回溯依赖行数（供非托管内存定长环形缓冲自动推导容量，默认 200）
    int Lookback => 200;

    // 生命周期：预热指标缓存与状态初始化（正式时间步前触发）
    void OnStart(IEngineContext context) { }

    // 核心时间步驱动入口（按主时钟步进触发）
    void OnUpdate(IEngineContext context);

    // 外部异构事件批量到达推送（公告、停复牌、龙虎榜等）
    void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { }

    // 订单与账户状态变更通知
    void OnOrderStatusChanged(IEngineContext context, string orderId, OrderStatus status) { }
    void OnTrade(IEngineContext context, ExecutionReport e) { }
    void OnPositionChanged(IEngineContext context, PositionEvent e) { }

    // 生命周期：回测结束或停机清理
    void OnStop(IEngineContext context) { }
}
```

### 2. `ISignalStrategy`（纯信号语义标记契约）
```csharp
public interface ISignalStrategy : IStrategy
{
}
```
继承自 `IStrategy`，语义上专注于特征提取与 Alpha 信号生产。纯信号策略不触碰 `context.Broker` 报单接口，仅通过黑板（`context.Market`）发布打分向量、概率分布或状态标签，支持高吞吐的纯信号回测与胜率评估。

### 3. `RuleStrategyBase`（规则交易模板基类）
专为单/多标的“无仓开仓、有仓平仓”的规则交易与快速原型测试提供的便利模板：

```csharp
namespace CarrotBacktesting.NET.Strategy;

public abstract class RuleStrategyBase : IStrategy
{
    public virtual string Name => GetType().Name;
    public virtual int Lookback => 200;

    // 默认遍历标的，按持仓状态安全分发至 CheckEntry 与 CheckExit
    public virtual void OnUpdate(IEngineContext context)
    {
        foreach (var symbol in GetSymbols(context))
        {
            var pos = context.Broker.GetPosition(symbol);
            if (pos == 0m)
            {
                CheckEntry(context, symbol);
            }
            else
            {
                CheckExit(context, symbol, pos);
            }
        }
    }

    protected abstract IEnumerable<string> GetSymbols(IEngineContext context);
    protected abstract void CheckEntry(IEngineContext context, string symbol);
    protected abstract void CheckExit(IEngineContext context, string symbol, decimal position);
}
```

### 4. `IStrategyPipeline`（组合管线契约）
```csharp
public interface IStrategyPipeline : IStrategy
{
    IStrategyPipeline Add(IStrategy strategy);
    IStrategyPipeline Compile();
    IEnumerable<IStrategy> GetStrategies();
    new int Lookback { get; }
}
```

---

## 三、通用强类型黑板模式 (IMarketState)

为解决策略间数据共享耦合问题，系统提供 `IMarketState` 通用强类型黑板容器（位于 `context.Market`），底层基于 `ConcurrentDictionary<string, object>` 保障多核并发下的绝对线程安全。

### 1. 契约方法
```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface IMarketState
{
    T? GetState<T>(string? key = null) where T : class;
    void SetState<T>(T state, string? key = null) where T : class;
    bool HasState<T>(string? key = null) where T : class;
    void Clear();
}
```

### 2. 协作范式：单类型默认单例与显式 Key 隔离
- **默认单例（Singleton per Type）**：当某一类数据在当前决策时刻全局权威且唯一时（如宏观偏向、仓位计划），无需传入 `key`，框架自动使用 `typeof(T).FullName` 索引，消除硬编码字符串拼写错误：
  ```csharp
  // 生产端
  context.Market.SetState(new PortfolioTarget(targetWeights));
  // 消费端
  var target = context.Market.GetState<PortfolioTarget>();
  ```
- **显式 Key 命名空间隔离**：当多个策略并发产生同一种数据类型（例如因子 A 与因子 B 均产生 `Dictionary<string, double>` 全市场打分字典），必须通过显式 Key 进行命名空间隔离：
  ```csharp
  // 因子 A 生产端
  context.Market.SetState(scoresA, key: "Alpha_Momentum");
  // 因子 B 生产端
  context.Market.SetState(scoresB, key: "Alpha_Reversion");

  // 消费端按 Key 取回
  var scoresA = context.Market.GetState<Dictionary<string, double>>("Alpha_Momentum");
  ```

---

## 四、组合策略管线体系 (Composite Pipeline)

系统采用对称的组合模式设计，通过统一的抽象基类与两个具体执行拓扑派生类实现灵活编排：

### 1. 类继承体系与架构对称性
- **`StrategyPipeline`（抽象基类）**：管理子策略集合、动态 Roslyn 上下文生命周期、递归汇总子管线 `Lookback`、提供 `Sequential()` 与 `Parallel()` 便捷静态工厂，并在 `Compile()` 时固化不可变数组。
- **`SequentialPipeline`（串行管线）**：严格按照添加顺序以单线程循环依次驱动各子策略的 `OnUpdate` 与 `OnEvent`。
- **`ParallelPipeline`（并行管线）**：默认采用确定性单线程顺序执行（`Concurrent = false`），规避小任务下的线程调度反噬与便于断点调试；当显式开启 `Concurrent = true` 且子策略数大于 1 时，利用 `System.Threading.Tasks.Parallel.For` 零 GC 实例委托复用多核并发驱动 `OnUpdate` 与 `OnEvent`。

### 2. 密封优化机制 (`Compile()`)
在引擎正式驱动前，调用 `pipeline.Compile()` 执行密封优化（Sealing & Freezing）：
1. **递归密封嵌套子管线**：深层嵌套的所有子管线同步冻结；
2. **不可变数组固化 (`IStrategy[]`)**：将可变 `List<IStrategy>` 转为连续内存只读数组，消除 .NET JIT 循环遍历时的数组越界检查（Bounds Check Elimination, BCE），循环提速 20%~30%；
3. **回溯深度锁定 (`Lookback`)**：递归计算全管线所有子节点的最大 Lookback 并锁定，底层非托管内存定长环形缓冲运行期零二次扩容；
4. **执行拓扑固化**：锁定拓扑结构，杜绝运行期动态修改（注：各子策略的 `OnStart(context)` 生命周期预热由引擎在启动正式循环前按拓扑顺序统一步进触发）。

---

## 五、三种策略装配与开发范式

### 模式 1：纯代码直装 (Code-First)
直接在 C# 代码中组装策略管线，适合主工程内嵌策略：

```csharp
using CarrotBacktesting.NET.Strategy;

var pipeline = StrategyPipeline.Sequential()
    .Add(StrategyPipeline.Parallel()
        .Add(new MomentumAlphaStrategy())
        .Add(new ReversionAlphaStrategy()))
    .Add(new PortfolioOptimizerStrategy())
    .Add(new ExecutionRiskStrategy())
    .Compile();
```

### 模式 2：Roslyn 动态 C# 源码免构建热加载 (.cs Scripting)
支持研究员直接编写独立 `.cs` 源码文件，无需编译整个主工程，修改保存即可立即运行：

```csharp
// 直接加载单文件或源码字符串
pipeline.AddFromSource("strategies/DualMaStrategy.cs");
```

- **原生机器码执行**：内存编译耗时仅约 80~150ms，随后由 JIT 转为原生机器码，运行期执行效率与预编译代码 100% 相同；
- **防内存泄露 (`CollectibleAssemblyLoadContext`)**：基于可回收程序集加载上下文，在参数调优与脚本修改重载后由 GC 彻底回收程序集元数据；
- **自动依赖绑定**：动态脚本中可直接引用 C# 原生基础库、LINQ 与 CarrotQuant 抽象层。

### 模式 3：YAML 声明式配置驱动 (`StrategyPipelineFactory`)
在策略配置文件中声明管线结构与参数，无需重新编译主程序即可自由拼装策略拓扑：

#### 配置文件范例 (`configs/pipeline.yaml`):
```yaml
name: ProductionMasterPipeline
mode: sequential                    # 顶层为串行流水线 (sequential | parallel)
pipeline:
  # 阶段 1：多因子多信号并发生产 (利用多核 CPU 并行计算)
  - name: SignalStage
    mode: parallel
    concurrent: true
    pipeline:
      - source: "strategies/MomentumAlpha.cs"
        params:
          Period: 14
          Threshold: 0.02
      - type: "MyQuant.Strategies.ReversionAlpha"
        params:
          Window: 60
  # 阶段 2：投资组合仓位规划 (串行执行)
  - type: "MyQuant.Strategies.PortfolioPlanner"
    params:
      TopK: 20
      RebalanceDays: 5
  # 阶段 3：风控与报单执行 (串行执行)
  - type: "CarrotBacktesting.NET.Strategy.RuleStrategyBase"
    params:
      Lookback: 200
```

#### 加载与驱动代码：
```csharp
using CarrotBacktesting.NET.Config;
using CarrotBacktesting.NET.Strategy;

// 方式 1：直接通过 YAML 配置文件物理路径装配并自动完成 Compile 优化
StrategyPipeline pipeline = StrategyPipelineFactory.Create("configs/pipeline.yaml");

// 方式 2：分步解析配置并装配（配置由 PipelineConfigLoader 权威反序列化）
PipelineConfiguration config = PipelineConfigLoader.Load("configs/pipeline.yaml");
StrategyPipeline customPipeline = StrategyPipelineFactory.Create(config);
```

- **参数自动强类型注入**：配置中 `params:` 字典的值会自动通过反射与类型转换（支持整型、浮点、布尔、字符串等）注入到对应策略类的同名可写属性中。

---

## 六、时序变更感知与事件到达探测

在超高频多周期或异构数据回测中，避免在每个高频 Bar 中重复计算低频指标是保障高吞吐的关键。

### 1. 位掩码常数时间变更感知 (`ITimeseriesRegistry.IsUpdated`)
系统底层使用 64 位无符号整型位掩码 `_updatedMask` 记录各表的跨周期更新状态：
- **执行耗时**：单次判定 `(_updatedMask & (1UL << tableIndex)) != 0` 耗时 < 0.3ns，单 CPU 寄存器完成，零 GC 分配、零装箱；
- **容量越界防御**：表注册序号达到 64 时立即抛出 `NotSupportedException`，防止状态位移取模穿透；
- **调用范式**：
  ```csharp
  // 仅在日线更新的 Bar 时刻，才重新计算高开销的 60 日均线
  if (context.Timeseries.IsUpdated("kline.1d"))
  {
      _dailyMa60 = CalculateDailyMa60(context);
  }
  ```

### 2. 外部事件到达轻量探测 (`IEventRegistry.HasNewEvents`)
```csharp
// 常数时间探测当前时间步指定事件表是否存在新到达事件
if (context.Events.HasNewEvents(context.CurrentTime, "suspension"))
{
      // 处理停牌拦截
}
```

---

## 七、经典实战策略编写范例

### 范例 1：经典时序指标跟踪策略 (`IStrategy`)
```csharp
using System;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Strategy;

public class MaCrossStrategy : IStrategy
{
    public string Name => "MaCross";
    public int Lookback => 30;

    public int FastPeriod { get; set; } = 5;
    public int SlowPeriod { get; set; } = 20;

    public void OnUpdate(IEngineContext context)
    {
        var closeMatrix = context.Timeseries.GetMatrix<double>("close");
        int row = context.CurrentIndex;
        if (row < SlowPeriod) return;

        for (int col = 0; col < context.Timeseries.Metadata.Symbols.Count; col++)
        {
            string symbol = context.Timeseries.Metadata.Symbols[col];
            double todayClose = closeMatrix[row, col];

            double fastMa = 0.0, slowMa = 0.0;
            for (int k = 0; k < FastPeriod; k++) fastMa += closeMatrix[row - k, col];
            for (int k = 0; k < SlowPeriod; k++) slowMa += closeMatrix[row - k, col];
            fastMa /= FastPeriod;
            slowMa /= SlowPeriod;

            decimal position = context.Broker.GetPosition(symbol);

            if (fastMa > slowMa && position == 0)
            {
                context.Broker.SubmitOrder(new OrderRequest(
                    symbol, OrderDirection.Buy, OrderType.Limit, todayClose, 1000m));
            }
            else if (fastMa < slowMa && position > 0)
            {
                context.Broker.SubmitOrder(new OrderRequest(
                    symbol, OrderDirection.Sell, OrderType.Limit, todayClose, position));
            }
        }
    }
}
```

### 范例 2：规则交易便利模板 (`RuleStrategyBase`)
```csharp
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Strategy;

public class SimpleRuleStrategy : RuleStrategyBase
{
    public override string Name => "SimpleRuleDemo";
    public override int Lookback => 20;

    protected override IEnumerable<string> GetSymbols(IEngineContext context)
    {
        return context.Timeseries.Metadata.Symbols;
    }

    protected override void CheckEntry(IEngineContext context, string symbol)
    {
        // 无仓位时：检查开仓条件
        var closeMatrix = context.Timeseries.GetMatrix<double>("close");
        int col = context.Timeseries.Metadata.GetSymbolIndex(symbol);
        double price = closeMatrix[context.CurrentIndex, col];

        if (price > 10.0) // 示例买入条件
        {
            context.Broker.SubmitOrder(new OrderRequest(
                symbol, OrderDirection.Buy, OrderType.Limit, price, 100m));
        }
    }

    protected override void CheckExit(IEngineContext context, string symbol, decimal position)
    {
        // 已有持仓时：检查止盈止损条件
        var closeMatrix = context.Timeseries.GetMatrix<double>("close");
        int col = context.Timeseries.Metadata.GetSymbolIndex(symbol);
        double price = closeMatrix[context.CurrentIndex, col];

        if (price < 9.5) // 示例止损条件
        {
            context.Broker.SubmitOrder(new OrderRequest(
                symbol, OrderDirection.Sell, OrderType.Limit, price, position));
        }
    }
}
```

---

> [!NOTE]
> 更多底层时序矩阵与维表查询 API 细节，请参阅 [数据存储与格式规范 (data_format.md)](data_format.md) 与 [API 参考手册 (API.md)](API.md)。
