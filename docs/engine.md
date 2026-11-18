# 核心回测引擎与运行时调度 (BacktestingEngine & Runtime System)

本文档面向量化系统架构师与策略开发者，系统介绍 CarrotQuant.NET 的回测引擎（`BacktestingEngine`）、运行时只读上下文（`EngineContext`）以及检查点容灾体系（`ICheckpointable`）的架构设计、状态机调度与热路径优化。

---

## 目录
- [一、架构总览与分层职责](#一架构总览与分层职责)
- [二、引擎生命周期与状态机](#二引擎生命周期与状态机)
- [三、主时钟步进推进机制](#三主时钟步进推进机制)
- [四、运行时只读上下文 (EngineContext)](#四运行时只读上下文-enginecontext)
- [五、检查点保存与断点复原 (ICheckpointable)](#五检查点保存与断点复原-icheckpointable)
- [六、顶层会话门面 (BacktestSession)](#六顶层会话门面-backtestsession)

---

## 一、架构总览与分层职责

```mermaid
graph TD
    subgraph DataLayer [Data Layer - 数据供给]
        TR[TimeseriesRegistry - 稠密矩阵]
        ER[EventRegistry - 稀疏事件]
        FR[FieldRegistry - 静态维表]
    end

    subgraph EngineLayer [Engine Layer - 引擎与执行]
        Engine[BacktestingEngine<br>主时钟循环 / 状态机]
        Context[EngineContext<br>只读上下文黑板]
        Broker[Broker<br>账户资金 / 两阶段冻结]
        Gateway[SimulatedExchangeGateway<br>订单簿 / 撮合 / 行情驱动]
        Monitor[NullMonitor / IMonitor<br>可观测性]
    end

    subgraph StrategyLayer [Strategy Layer - 策略管线]
        Pipeline[StrategyPipeline<br>Composite 拓扑编排]
        Strategy[IStrategy / RuleStrategyBase]
    end

    TR --> Engine
    ER --> Engine
    FR --> Broker
    FR --> Gateway
    Broker --> Context
    Gateway --> Broker
    Engine --> Context
    Monitor --> Context
    Context --> Pipeline
    Pipeline --> Strategy
    Strategy -->|下单 / 查持仓| Broker
```

### 核心分层设计原则
1. **单一内核与纯单向依赖 (DAG)**：
   - 依赖关系严格自顶向下：`IFieldRegistry` $\to$ `Broker` / `SimulatedExchangeGateway` $\to$ `EngineContext` $\to$ `BacktestingEngine`；
   - 经纪商仅依赖标的静态维表抽象 `IFieldRegistry` 与网关契约 `IExchangeGateway`，彻底消除对运行时上下文的双向循环依赖；详细清算与撮合机制见独立文档 [经纪商与交易所体系 (broker.md)](broker.md)；
2. **读写分离与零装箱**：
   - 上下文（`IEngineContext`）为策略层提供纯只读门面；
   - 策略与经纪商之间的交互严格通过强类型订单请求（`OrderRequest`）与执行回报（`ExecutionReport`）进行，杜绝策略直接改写账户内部状态。

---

## 二、引擎生命周期与状态机

引擎统一实现 `IEngine` 核心调度契约与 `ICheckpointable` 快照契约，由五态有限状态机（FSM）驱动生命周期流转：

```mermaid
stateDiagram-v2
    [*] --> Idle: 构造完成
    Idle --> Running: Run() / RunAsync()
    Running --> Paused: Pause()
    Paused --> Running: Resume()
    Running --> Completed: 正常跑完全部时间步 / Stop()
    Paused --> Completed: Stop() 安全终止
    Running --> Faulted: 策略异常未捕获
    Completed --> [*]
    Faulted --> [*]
```

### 状态机核心状态定义与约束
| 状态 (EngineStatus) | 物理含义 | 允许的操作 |
| :--- | :--- | :--- |
| **`Idle`** | 初始空闲状态 | `Run()`、`RunAsync()`、`RestoreCheckpoint()` |
| **`Running`** | 主循环正在推进 | `Pause()`、`Stop()`、外部 `CancellationToken` 取消 |
| **`Paused`** | 步进循环安全挂起 | `Resume()`、`Stop()` |
| **`Completed`** | 回测全周期完成或被提前终止 | 终止态，资源安全释放 |
| **`Faulted`** | 策略异常崩溃 | 终止态，错误已上报并安全注销监听 |

> [!IMPORTANT]
> - **确定性终止防护**：在 `Paused` 状态下调用 `Stop()` 或取消令牌时，内部采用双重条件守卫，唤醒后立即退出循环，绝不多跑一个时间步；
> - **安全级联释放**：引擎调用 `Dispose()` 时自动级联释放底层 `StrategyPipeline`，并防御并发 `ObjectDisposedException` 异常穿透。

---

## 三、主时钟步进推进机制

回测主时钟在 `BacktestingEngine.Run()` 中以整数索引推进循环，每步执行确定性离散事件与管线驱动，热路径保持零堆内存分配（Zero GC）：

### 1. 二分日期裁剪与尾日纳秒推移
回测开始前，引擎基于 `DynamicMarketMetadata.TradeDates` 快速二分定位起止索引：
- **纯日期截至时间防护**：若配置的 `EndDate` 未包含时分秒（即 `TimeOfDay == TimeSpan.Zero`），引擎自动将其推移至当日最后纳秒：
  $$\text{TargetEndDate} = \text{EndDate.Date} + 1\text{天} - 1\text{纳秒Tick} \quad (23:59:59.9999999)$$
  确保截至日全天的盘中所有分钟 Bar（如 09:30 至 15:00）全部被包含在回测中，绝不丢失尾日数据。

### 2. 热路径步进流水线 (Step Pipeline)
每个时间步内部严格按以下顺序执行，全程无堆内存分配：
1. **时钟推进**：`_context.UpdateStep(i, currentTime)`，更新上下文中的索引与当前物理时间戳；
2. **跨日增量因子供给**：检测到跨日且存在除权事件时，针对性增量更新 `_context` 对应标的的累计复权因子；
3. **时序缓存滑动**：引擎通过 `ITimeseriesScheduler` 调度契约（`_timeseriesScheduler?.StepTo(i)`）驱动底层 4096 行分块内存槽位滑动与异步预读，与上层策略只读门面 `ITimeseriesRegistry` 形成 CQRS 物理隔离；
4. **仿真交易所撮合**：`_simExchange.Step(currentTime, _timeseries, i)`，撮合在途订单，**强制使用未复权物理价格（Raw）**；
5. **经纪商时钟推进与跨日结算**：调用 `_broker.Step(currentTime)`，由经纪商自主比对换日阈值 `_nextRollTimeUtc`，到达交易所本地次日 00:00:00 时自动触发 T+N 可用持仓划转（详见 [broker.md](broker.md)）；
6. **离散事件探测**：常数时间探测 `_events.HasNewEvents(currentTime)`，命中时调用预分配静态缓冲派发；
7. **指标注册中心单步驱动**：`_indicators.Step(_context)`，统一单次泵送所有已注册指标池，多策略共享单次计算；
8. **策略管线执行**：驱动 `_pipeline.OnUpdate(_context)`，策略黑板与状态机协同。

### 3. 复权因子冷启动极速滚动与跨模态覆盖告警
- **免剪枝全量加载**：除权因子事件表（`ashare.adj_factor.*`）全历史仅 2~3 MB，不按起测年份激进剪枝；
- **从头滚动至 StartDate (< 0.5ms)**：引擎在正式进入主循环前，按时间序从历史首个事件极速滚动遍历至起测日，直接覆写沉淀出全市场截至起测日的权威累计因子；
- **跨模态覆盖校验（有行情无因子必报警）**：启动前扫描全市场，若发现某标的在行情矩阵中存在有效价格，但除权表中完全查无此人，立即通过 `IMonitor.Log` 输出醒目的聚合 Warning 告警并采样打印前 5 只标的代码，杜绝因因子漏载导致的静默失真。

---

## 四、运行时只读上下文 (EngineContext)

`EngineContext` 是策略层在 `OnUpdate` 与 `OnEvent` 中获取环境状态的唯一入口，统一收敛极简当期取价门面与指标注册中心：

```csharp
public class EngineContext : IEngineContext
{
    public int CurrentIndex { get; }
    public DateTime CurrentTime { get; }
    public ITimeseriesRegistry Timeseries { get; }
    public IEventRegistry Events { get; }
    public IFieldRegistry Fields { get; }
    public IBroker Broker { get; }
    public IMarketState Market { get; }
    public IndicatorRegistry Indicators { get; }

    // 当期极简取价门面（专职当期快照，统一 double，默认 Post 后复权，根除历史因子时间错位）
    public double GetClose(string symbol, PriceMode mode = PriceMode.Post);
    public double GetClose(int symbolIndex, PriceMode mode = PriceMode.Post);
    public double GetOpen(string symbol, PriceMode mode = PriceMode.Post);
    public double GetOpen(int symbolIndex, PriceMode mode = PriceMode.Post);
    public double GetHigh(string symbol, PriceMode mode = PriceMode.Post);
    public double GetHigh(int symbolIndex, PriceMode mode = PriceMode.Post);
    public double GetLow(string symbol, PriceMode mode = PriceMode.Post);
    public double GetLow(int symbolIndex, PriceMode mode = PriceMode.Post);
    public double GetVolume(string symbol, PriceMode mode = PriceMode.Raw); // 成交量默认保持物理量 Raw
    public double GetVolume(int symbolIndex, PriceMode mode = PriceMode.Raw);
    public double GetField(string symbol, string fieldName, PriceMode mode = PriceMode.Post);
    public double GetField(int symbolIndex, string fieldName, PriceMode mode = PriceMode.Post);
}
```

- **单例内联更新**：每个回测任务全局仅分配一个 `EngineContext` 实例，每时间步仅通过内部 `UpdateStep` 字段赋值更新，不产生额外托管堆对象分配；
- **物理价格与策略价格严格隔离**：交易所撮合（`SimulatedExchangeGateway`）与资产账本（`Broker`）100% 走真实未复权价格（Raw），策略决策通过门面默认享受无跳空的平滑后复权价格（Post）。

---

## 五、检查点保存与断点复原 (ICheckpointable)

遵循接口隔离原则（ISP），检查点持久化能力抽象为独立的 `ICheckpointable` 契约，与引擎核心接口 `IEngine` 正交组合：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface ICheckpointable
{
    byte[] SaveCheckpoint();
    void RestoreCheckpoint(byte[] checkpoint);
}
```

`BacktestingEngine` 通过多接口实现提供该能力：
```csharp
public class BacktestingEngine : IEngine, ICheckpointable, IDisposable
```

### 1. 32 字节二进制快照布局
快照采用固定 32 字节小端序二进制流紧凑打包：
```
+---------------+---------------+---------------+---------------+---------------+---------------+
| Magic (4B)    | Version (4B)  | Status (4B)   | StepIndex (4B)| TimeTicks(8B) | Progress (8B) |
| 0x43514254    | 0x00000001    | (EngineStatus)| (int)         | (long)        | (double)      |
+---------------+---------------+---------------+---------------+---------------+---------------+
```

### 2. 防护与恢复契约
- **魔数防护**：文件头必须精确匹配 `0x43514254`（ASCII 标识 `"CQBT"`），若数据损坏或格式不符显式抛出 `InvalidDataException`；
- **状态机守护**：仅允许在引擎处于 `Idle` 状态时调用 `RestoreCheckpoint`；在 `Running` 或 `Paused` 下恢复将立即抛出 `InvalidOperationException`，杜绝运行期竞态污染；
- **断点续跑**：恢复后，引擎将从 `StepIndex + 1` 自动开始后续步进，步数与进度精确衔接。

---

## 六、顶层会话门面 (BacktestSession)

`BacktestSession`（位于 `CarrotBacktesting.NET`）作为系统的顶层门面（Facade），统筹配置解析、引擎执行与绩效分析导出。支持声明式配置驱动与编程式策略挂载两种使用模式：

### 1. 声明式配置执行 (Declarative Execution)
基于 YAML 配置文件直接执行回测，并依据配置完成绩效分析与报表导出：

```csharp
using CarrotBacktesting.NET;

// 依据配置文件执行回测并生成分析报告
var report = BacktestSession.Run("configs/session.yaml");
```

### 2. 编程式策略挂载 (Programmatic Execution)
加载环境与账户配置，并在代码中显式注入自定义策略实例：

```csharp
using CarrotBacktesting.NET;

// 加载配置并注入自定义策略实例
using var session = BacktestSession.Load("configs/session.yaml", new DualMaStrategy());
session.Run();
var report = session.Analyze();
```

### 3. 结果与底层组件访问
回测完成后，可通过实例属性访问不可变交易事实账本与引擎上下文：

```csharp
// 获取不可变交易事实账本
TradingResult? facts = session.Result;

// 访问底层引擎运行时上下文
IEngineContext context = session.Engine.Context;
```
