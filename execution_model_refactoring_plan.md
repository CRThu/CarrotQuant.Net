# CarrotQuant.NET 核心交易执行模型演进重构计划

> **设计基准与规范约束**：
> - 《核心架构、软硬协同与工程研发红线通用规范 V1.3》
> - 《CarrotQuant .NET 核心架构与开发规范 (AGENTS.md)》
> - 国际金融信息交换标准（FIX Protocol v4.4 / v5.0 SP2）
> - 现代高性能事件驱动量化架构（NautilusTrader / QuantConnect Lean）

---

## 一、重构背景与目标

当前 CarrotQuant.NET 在撮合与订单执行链路中已完成阶段性演进，但仍存在以下待治理的架构设计与能力断层：
1. **命名冗余与配置补丁**：`BrokerConfig.CancelPendingOrdersOnDailyClose`（31 字符）属于过程式全局补丁，缺乏一等公民订单时效模型（Time-In-Force）；
2. **撮合角色隐式执行**：`MatchingEngine` 内部已区分主动吃单（Taker，扣滑点）与被动排队（Maker，享受跳空价格改善），但顶层未暴露强类型契约，外部无法归因；
3. **分笔成交状态与资产守恒断层**：`ExecutionReport` 缺少 `CumQty`（累计成交）与 `LeavesQty`（剩余未成交），Broker 依赖除法比例解冻，全额成交时未清理浮点尾差，存在微小资金残留隐患；
4. **风控条件单与资产死锁约束**：缺乏原生的止损（Stop）与止盈（TakeProfit）条件单；若将条件单直接推入常规预冻结，会导致卖出止损单锁定可用持仓而无法挂出限价止盈单；日频回测中若以收盘价处理触发，无法反映日内高低点穿透与跳空开盘的实际成交价；
5. **亚 Bar 撮合与多时态执行模型缺失**：目前仅支持单一收盘后决策驱动，缺乏对开盘跳空、集合竞价选股、全天加权均价（VWAP）算法拆单的多模态执行价格支持。

**重构核心目标**：
- 契约命名杜绝晦涩缩写，采用全拼命名，注释统一标注行业标准缩写与 FIX Tag；
- 零运行时分配（Zero GC）与零热路径开销；
- 契约驱动，正交解耦策略决策时点（OnClose / OnOpen）与撮合成交价格（Open / VWAP / Close）；
- 完全向前兼容现有 API，平滑演进。

---

## 二、架构演进全景设计

```mermaid
flowchart TD
    subgraph 契约层 [Order Contracts & Types]
        TIF["TimeInForce (Day / GoodTilCanceled)"]
        LR["LiquidityRole (Maker / Taker)"]
        OT["OrderType (Market / Limit / Stop / TakeProfit / StopLimit / TakeProfitLimit)"]
        SUT["StrategyUpdateTiming (OnClose / OnOpen)"]
        EPT["ExecutionPriceType (Open / Close / Vwap / Average)"]
    end

    subgraph 触发与条件中枢 [ConditionalTriggerManager (触发前零冻结)]
        CTM["Trigger Watcher (监控 High/Low/Open 极值)"]
        CTM -->|跌破 StopPrice / 冲高触碰 TargetPrice| FIRED[原子化转为即时市价/限价报单]
    end

    subgraph 经纪商与账本层 [Broker & AccountLedger]
        BRK["Broker.SubmitOrder (两阶段预冻结)"]
        LEDGER["AccountLedger (净头寸自动平仓开仓)"]
        UNFREEZE["全额成交/撤单浮点残差清零吸收"]
        FIRED --> BRK
        BRK --> LEDGER
        UNFREEZE --> LEDGER
    end

    subgraph 撮合与网关层 [SimulatedExchangeGateway & MatchingEngine]
        OB["OrderBook (单一权威挂单簿 SSOT)"]
        ME["MatchingEngine (纯函数撮合内核)"]
        
        OB -->|日切扫描撤销 Day 挂单| CANCEL[集中撤单并解冻]
        OB -->|排队被动限价成交| ME_MAKER[Maker 撮合: 零滑点 + 跳空改善]
        OB -->|次日开盘市价/穿透撮合| ME_TAKER[Taker 撮合: 扣除滑点]
        BRK -->|提交订单统一入簿| OB
    end

    subgraph 分析与黑匣子记录层 [TradingRecorder & AnalysisEngine]
        EVT["OrderEvent (Submitted / Fill / Canceled / Rejected)"]
        REC["TradingRecorder (统一 Record(T) 沉淀)"]
        REP["ExecutionReport (携带 Role, CumQty, LeavesQty)"]
        
        BRK -->|发布 OnOrderEvent| EVT --> REC
        ME_MAKER --> REP
        ME_TAKER --> REP
        REP --> UNFREEZE
    end
```

---

## 三、分阶段详细演进方案

### 阶段一：订单时效契约（TimeInForce）收敛与日切撤单重构 (P0) - [✅ 已完成 / 独立审计 PASS]

> **落地状态与验收凭证（2026-11-21 归档）**：
> - **契约固化**：`TimeInForce : byte`（Day=0, GoodTilCanceled=1）已在 `Abstraction/Engine/OrderTypes.cs` 落地，`OrderOffset` 彻底物理移除；
> - **单一事实来源（SSOT）**：全局业务时效归一化唯一在 `Broker.SubmitOrder` 单点执行，网关与挂单簿零二次推导、零冗余分配；
> - **高频门面透出**：`IBroker`、`Broker`、`IEngineContext`、`EngineContext` 均已透出 `TimeInForce? timeInForce = null` 可选参数，100% 向前兼容；
> - **挂单簿重构**：`OrderBook.CancelDayOrders()` 仅移出 Day 挂单，标的桶空时注销键；
> - **全量测试回归**：专用测试套件 `TimeInForceTests.cs` 覆盖全生命周期分支，全工程 725 个单元测试全部绿色通过（0 失败，0 跳过）；
> - **独立审计**：通过两轮独立子 Agent 软件架构与代码质量审计，最终裁决为 **【PASS】**。

#### 1. 契约定义 (`Abstraction/Engine/OrderTypes.cs`)
采用易懂全拼定义强类型枚举，底层锚定 `byte` 消除装箱开销，注释标注行业缩写与 FIX 协议标准：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 订单时效性与生命周期规则。
/// 严格收敛为全市场日频与分钟频回测的核心时效基元，杜绝无盘口深度下的伪高频实现。
/// </summary>
public enum TimeInForce : byte
{
    /// <summary>
    /// 当日有效（Day Order，默认）。收盘或日切结算时未成交挂单集中自动撤销并解冻资产。
    /// 物理对齐：A 股现货、国内期货默认规则，FIX Tag 59 = 0。
    /// </summary>
    Day = 0,

    /// <summary>
    /// 撤单前有效（Good 'Til Canceled）。跨交易日持续在挂单簿排队，直到完全成交或主动撤销。
    /// 物理对齐：美股、加密货币、网格长线挂单规则，FIX Tag 59 = 1。
    /// </summary>
    GoodTilCanceled = 1
}
```

#### 2. `OrderRequest` 升级（零破坏兼容）
在 `OrderRequest` 主构造函数挂载 `TimeInForce`，并清理悬空的未消费属性 `OrderOffset`：

```csharp
[method: System.Text.Json.Serialization.JsonConstructor]
public record OrderRequest(
    string Symbol,
    OrderDirection Direction,
    OrderType Type,
    double Price,
    decimal Quantity,
    string OrderTag = "",
    TimeInForce? TimeInForce = null)
{
    public OrderRequest(string symbol, OrderDirection direction, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
        : this(symbol, direction, OrderType.Market, 0.0, quantity, orderTag, timeInForce)
    {
    }

    public OrderRequest(string symbol, OrderDirection direction, double price, decimal quantity, string orderTag = "", TimeInForce? timeInForce = null)
        : this(symbol, direction, OrderType.Limit, price, quantity, orderTag, timeInForce)
    {
    }

    /// <summary>
    /// 本地客户端订单 ID（UUID），用于防重推、去重和实盘柜台唯一映射。
    /// </summary>
    public string ClientOrderId { get; init; } = string.Empty;

    /// <summary>
    /// 条件单触发价（止损单/止盈单有效，阶段三基元预留）。
    /// </summary>
    public double StopPrice { get; init; } = 0.0;

    public bool IsBuy => Direction == OrderDirection.Buy;
    public bool IsSell => Direction == OrderDirection.Sell;
}
```

> **注**：
> 1. `TimeInForce` 默认形参采用可空类型 `TimeInForce? = null`，Broker 内部取 `request.TimeInForce ?? _config.DefaultTimeInForce`，彻底消除 C# 编译期常量硬编码导致全局配置失效的问题，保障单一事实来源（SSOT）；
> 2. 悬空未消费的 `OrderOffset` 枚举与属性已彻底物理移除，遵循红线第 1 条与第 21 条（第一性原理与零历史包袱）。

#### 3. 配置收敛 (`BrokerConfig.cs`)
- 废弃过程式配置 `CancelPendingOrdersOnDailyClose`；
- 替换为强类型属性 `DefaultTimeInForce`（默认 `TimeInForce.Day`）；
- 跨日清算时仅撤销 `order.TimeInForce == TimeInForce.Day` 的挂单，保留 `GoodTilCanceled` 挂单。

---

### 阶段二：分笔成交不变量（CumQty/LeavesQty）、流动性角色（Maker/Taker）与订单黑匣子 (P1) - [✅ 已完成 / 独立审计 PASS]

> **落地状态与验收凭证（2026-11-21 归档）**：
> - **流动性角色与分笔不变量**：`LiquidityRole : byte`（Maker / Taker）与 `ExecutionReport(CumQuantity, LeavesQuantity, Role)` 契约落地；`Broker.HandleExecution` 实行 $CumQty + LeavesQty \equiv OrderQty$ 守恒硬校验（违背守恒立即抛出 `InvalidOperationException` 熔断，杜绝 Release 穿透）；`MatchingEngine` 准确归因即时市价/穿透限价（Taker）与挂单簿撮合（Maker）；
> - **浮点尾差吸收与账本防御**：`Broker` 在全额成交或撤单时强制吸收清零剩余微小浮点尾差（解冻残留）；`AccountLedger.Unfreeze` 完善多头平尽（`TotalQuantity == 0m`）时的边界防御，杜绝负持仓异常；
> - **网关回调签名升级**：`IExchangeGateway.OnOrderStateChanged` 升级为带原因的三元签名，精准透传网关废单/拒单/撤单原因至 `OrderEvent`；
> - **订单黑匣子审计链路**：`OrderEvent`（严格对齐 FIX Tag 150/32/14/151/31/851，明确区分当笔 `FillQuantity` 与累计 `CumQuantity`）打通 5 大生命周期锚点派发；`BacktestingEngine` 解耦订阅并转交 `TradingRecorder` 沉淀；
> - **状态机边界与异常治理**：`Broker.HandleOrderStateChanged` 消除假默认值隐式兜底，终态（Filled/Canceled/Rejected）不可逆保护，未知状态显式抛出强类型 `ArgumentOutOfRangeException`；
> - **统一记录器契约与纯粹持久化**：`ITradingRecorder` 全面收敛为 `Record(...)` 重载；二进制检查点持久化协议统一重置为 `StateVersion = 1` 包含 `OrderEvents`，消除兼容包袱；
> - **全量测试回归与独立审计**：新增 `AccountLedgerPrecisionTests`、`MatchingEngineRoleTests`、`OrderEventBlackboxTests`，全工程 735 个单元测试全部绿色通过（0 失败，0 跳过）；经两轮严苛独立子 Agent 架构审计，最终裁决为 **【PASS】**。

#### 1. 契约定义与升级 (`Abstraction/Engine/OrderTypes.cs` & `IExchangeGateway.cs`)
```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 成交流动性角色标识。
/// </summary>
public enum LiquidityRole : byte
{
    None = 0,

    /// <summary>
    /// 被动挂单方（Maker / 提供流动性）。
    /// 在挂单簿中排队被动撮合，承担零滑点，享受开盘跳空价格改善。对齐 FIX Tag 851 = 1。
    /// </summary>
    Maker = 1,

    /// <summary>
    /// 主动吃单方（Taker / 消耗流动性）。
    /// 市价单或穿透限价单即时成交，承担物理滑点成本。对齐 FIX Tag 851 = 2。
    /// </summary>
    Taker = 2
}

/// <summary>
/// 表示一笔成交的执行回报，包含了物理成交与生命周期进度凭证。
/// </summary>
public record ExecutionReport(
    string OrderId,
    string Symbol,
    double FillPrice,
    decimal FillQuantity,
    double Commission,
    MarketTime Time,
    decimal CumQuantity = 0m,      // 累计已成交量，行业缩写：CumQty，对齐 FIX Tag 14
    decimal LeavesQuantity = 0m,   // 挂单簿剩余排队量，行业缩写：LeavesQty，对齐 FIX Tag 15
    LiquidityRole Role = LiquidityRole.None
);

/// <summary>
/// 表示网关或交易所订单生命周期状态变更的权威报告。
/// 对齐 FIX 协议状态回报模型，消除散装参数与二义性，对称于 ExecutionReport。
/// </summary>
public record OrderStateReport(
    string OrderId,
    OrderStatus Status,
    MarketTime Time,
    string Symbol = "",
    string Reason = "",
    string ClientOrderId = "",
    decimal LeavesQuantity = 0m,
    int ErrorCode = 0);
```

**网关回调强类型升级 (`Abstraction/Engine/IExchangeGateway.cs`)**：
将散装参数委托彻底升级为对称的不可变强类型载体，打通时间戳、品种、剩余量与拒单原因物理链路：
```csharp
public interface IExchangeGateway
{
    // ...
    /// <summary>
    /// 当订单发生实际成交时触发。
    /// </summary>
    event Action<ExecutionReport> OnExecution;

    /// <summary>
    /// 当订单生命周期状态发生变化时触发（如：已提交排队、被拒绝、已撤销）。
    /// </summary>
    event Action<OrderStateReport> OnOrderStateChanged;
}
```

#### 2. 撮合透出、资产守恒闭环与账本防御 (`MatchingEngine.cs` & `Broker.cs` & `AccountLedger.cs`)
- **撮合与挂单簿透出**：
  - `MatchResult`（只读结构体）增加 `LiquidityRole Role`；
  - `EvaluateImmediateOrder` 打标为 `LiquidityRole.Taker`；
  - `EvaluatePendingLimitOrder` 打标为 `LiquidityRole.Maker`；
  - `SimulatedOrder` 挂载 `decimal FilledQuantity`，挂单成交时递增并在 `ExecutionReport` 中精准填入 `CumQuantity` 与 `LeavesQuantity`；
- **Broker 守恒校验与尾差吸收**：
  - `Broker.HandleExecution` 严格校验守恒断言，使用显式强类型异常杜绝 Release 下失效：
    ```csharp
    if (report.CumQuantity + report.LeavesQuantity != ord.Request.Quantity)
    {
        throw new InvalidOperationException(
            $"分笔成交守恒不变量被破坏: 订单 '{report.OrderId}' CumQuantity({report.CumQuantity}) + LeavesQuantity({report.LeavesQuantity}) != OrderQuantity({ord.Request.Quantity})");
    }
    ```
  - 全额成交时吸收释放现金浮点尾差（解决 $100/300$ 循环小数产生的微小冻结资金残留）：
    ```csharp
    if (report.LeavesQuantity == 0m || ord.FilledQuantity >= ord.Request.Quantity)
    {
        if (ord.RemainingFrozenCash > 0.0)
        {
            _ledger.Unfreeze(ord.Request.Symbol, ord.RemainingFrozenCash, 0m, ord.Request.IsBuy);
            ord.RemainingFrozenCash = 0.0;
        }
        if (ord.RemainingFrozenPosition > 0m)
        {
            _ledger.Unfreeze(ord.Request.Symbol, 0.0, ord.RemainingFrozenPosition, ord.Request.IsBuy);
            ord.RemainingFrozenPosition = 0m;
        }
    }
    ```
- **AccountLedger.Unfreeze 边界防御**：
  - 修复持仓全额平仓（`pos.TotalQuantity == 0m`）时误入空头分支将 `AvailableQuantity` 扣减为负数的隐患：
    ```csharp
    if (unfreezePosition > 0m && _positions.TryGetValue(symbol, out var pos))
    {
        pos.FrozenQuantity = Math.Max(0m, pos.FrozenQuantity - unfreezePosition);
        if (pos.TotalQuantity > 0m)
        {
            pos.AvailableQuantity += unfreezePosition;
        }
        else if (pos.TotalQuantity < 0m)
        {
            pos.AvailableQuantity -= unfreezePosition;
        }
        _positionsSnapshot = null;
    }
    ```

#### 3. 订单全生命周期动作事件 (`OrderEvent`)
```csharp
namespace CarrotBacktesting.NET.Abstraction.Analysis;

/// <summary>
/// 订单生命周期动作类型。
/// </summary>
public enum OrderEventType : byte
{
    Submitted = 0,  // 提交排队，对齐 FIX Tag 150 = 0 (New)
    Fill = 1,       // 产生成交，对齐 FIX Tag 150 = 1/2 (Partial/Fill)
    Canceled = 2,   // 撤单作废，对齐 FIX Tag 150 = 4 (Canceled)
    Rejected = 3    // 拒绝拦截，对齐 FIX Tag 150 = 8 (Rejected)
}

public sealed record OrderEvent(
    MarketTime Time,
    string OrderId,
    string Symbol,
    OrderDirection Direction,
    OrderType Type,
    double Price,
    decimal Quantity,
    OrderEventType EventType,
    OrderStatus Status,
    decimal FillQuantity = 0m,     // 当笔实际成交数量（FIX Tag 32 LastQty）
    decimal CumQuantity = 0m,      // 累计已成交数量（FIX Tag 14 CumQty）
    decimal LeavesQuantity = 0m,   // 剩余挂单排队量（FIX Tag 151 LeavesQty）
    double FillPrice = 0.0,        // 当笔实际成交价格（FIX Tag 31 LastPx）
    LiquidityRole Role = LiquidityRole.None,
    string Reason = "",
    string OrderTag = ""
);
```

- **事件派发与传递解耦**：
  - `IBroker` 增加 `event Action<OrderEvent>? OnOrderEvent;`；
  - `Broker` 在 5 大生命周期锚点派发 `OrderEvent`（提交成功 Submitted、预冻结失败 Rejected、网关/撮合拒绝 Rejected 带 Reason、成交 Fill 带 Role 与分笔量、撤单 Canceled）；
  - `BacktestingEngine` 在启动时订阅 `_broker.OnOrderEvent` 并调用 `_recorder.Record(orderEvent)`，注销时反向退订，单向依赖解耦；
- `TradingResult` 增加 `IReadOnlyList<OrderEvent> OrderEvents` 属性，`TradingRecorderSnapshot` 同步挂载。

#### 4. `ITradingRecorder` 统一重载范式与持久化
彻底移除 `RecordStep`、`RecordClosedTrade` 等非对称别名，全量收敛为 `Record(...)` 重载：
```csharp
public interface ITradingRecorder
{
    void Record(EquityPoint point);
    void Record(DailySnapshot snapshot);
    void Record(SettlementRecord settlement);
    void Record(Trade trade);
    void Record(SignalRecord signal);
    void Record(OrderEvent orderEvent);

    TradingResult GetResult(IReadOnlyList<PositionSnapshot> endingPositions);
}
```
- **检查点持久化极致收敛**：
  - `TradingRecorder` 彻底移除非泛型 `ICheckpointable` 及手写二进制 `BinaryWriter/BinaryReader` 代码，仅实现 `ICheckpointable<TradingRecorderSnapshot>`，与全系统所有底层领域组件（Broker、Ledger、Exchange、MarketState）保持严格一致；
  - 字节流持久化统一由系统通用扩展方法（`ToJsonBytes()` / `RestoreFromJsonBytes()`）处理，消除双重维护与散弹式修改风险。

---

### 阶段三：亚 Bar 撮合与多模态执行时态体系 (P2) - [✅ 已完成 / 独立审计 FINAL PASS]

> **落地状态与验收凭证（2026-11-21 归档）**：
> - **时态契约解耦与正交配置**：`StrategyUpdateTiming`（OnClose / OnOpen）与 `ExecutionPriceType`（Open / Close / Vwap / Average）契约落地；分别正交挂载至 `EngineConfig.UpdateTiming` 与 `BrokerConfig.DefaultExecutionPrice`，遵循瘦契约原则，未污染 `OrderRequest`；
> - **业界标准黄金默认**：默认 `StrategyUpdateTiming.OnClose` 决策、次日 `ExecutionPriceType.Open` 开盘价撮合成交（Taker 角色计入滑点），彻底消灭收盘偷价反模式；
> - **架构极致精简（SSOT）**：彻底裁撤过度设计的第二队列 `PendingNextStepOrders`，跨步市价单统一进入权威挂单簿 `OrderBook` 集中管理；物理消除补丁标志 `IsCrossStep` 与网关抽象泄露属性 `IsOpenPhase`，日切仅针对未成交 Day 限价单，架构极致纯净；
> - **三时钟相位步进状态机**：`BacktestingEngine.RunInternal` 单步严格划分为 Phase 1（开盘日切清算、在途限价单被动撮合、次日开盘市价单集中撮合、T+N 昨仓可用解锁、OnOpen 开盘决策）、Phase 2（盘中离散事件探测派发）、Phase 3（批量增量指标计算、盘中开盘挂起 VWAP/Close 订单集中撮合、OnClose 收盘决策、资产净值盯市沉淀），时序因果律严格守恒；
> - **防未来函数内存守卫**：在 `OnOpen` 开盘相位决策时，`EngineContext` 门面拦截读取当期未完结 `Close/High/Low/Volume` 等字段并抛出 `InvalidOperationException`，底层非托管二维矩阵 `ChunkMatrix2D<T>` 保持严格只读纯净，绝不写脏（零伪 NaN）；
> - **严格数据契约（零隐式降级）**：显式配置 VWAP 时，若时序源缺失且无 volume/amount，网关坚决抛出强类型 `InvalidDataException` 阻断回测，严禁隐式降级；
> - **生命周期边界与断点续跑守恒**：`CloseGateway` 在回测终局精确撤销未完成市价单与到期 Day 单并全额解冻资金，保护 GTC 限价单排队状态；`SimulatedOrderSnapshot` 记录 `FilledQuantity` 业务事实，检查点持久化 100% 守恒还原；
> - **全量测试回归与独立审计**：新增 `ExecutionTimingAndPriceTests.cs`（10 个高覆盖度用例），全工程 747 个单元与集成测试 100% 绿色通过（0 失败，0 跳过）；经独立高级架构师两轮严苛代码审计，最终裁决为 **【FINAL PASS - 最终验收通过】**。

将“策略决策时点（Update Timing）”与“撮合成交价格（Execution Price）”正交解耦，全面支持开盘跳空策略、集合竞价选股与全天算法拆单回测，全面对齐国际主流量化回测框架（QuantConnect Lean / Qlib / Backtrader）黄金标准：

#### 1. 契约定义 (`Abstraction/Engine/OrderTypes.cs`)

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 策略决策与更新时点。
/// </summary>
public enum StrategyUpdateTiming : byte
{
    /// <summary>
    /// 收盘更新（默认推荐，绝大多数策略适用）。
    /// 当前时间步 Bar 完全走完时驱动 OnUpdate，策略可读取当期完整 OHLCV 与收盘指标。
    /// </summary>
    OnClose = 0,

    /// <summary>
    /// 开盘更新（开盘跳空、集合竞价选股、日内算法拆单策略）。
    /// 在当前时间步 Bar 刚开盘时驱动 OnUpdate，策略可读取当期 Open 与历史已完结 Bar。
    /// </summary>
    OnOpen = 1
}

/// <summary>
/// 撮合成交价格基准（回测环境与经纪商执行规则配置，外部不可见，不侵入 OrderRequest）。
/// </summary>
public enum ExecutionPriceType : byte
{
    /// <summary>
    /// 开盘价成交（默认推荐，对齐国际主流框架标准）。
    /// OnOpen 模式下以当期 Open 撮合；OnClose 模式下以次日 Open 撮合。
    /// </summary>
    Open = 0,

    /// <summary>
    /// 收盘价成交。
    /// 假设尾盘 14:55 决策并参与收盘集合竞价，以当期 Close 撮合。
    /// </summary>
    Close = 1,

    /// <summary>
    /// 全日成交量加权均价成交（大资金机构回测标准，行业通用缩写：VWAP）。
    /// OnOpen 模式下以当期 VWAP 拆单；OnClose 模式下以次日 VWAP 拆单。
    /// 契约约束：若数据源缺失 vwap 且无法通过 amount/volume 计算，显式抛出强类型异常，坚决杜绝隐式降级伪实现！
    /// </summary>
    Vwap = 2,

    /// <summary>
    /// 全日均价成交 (Open + High + Low + Close) / 4。
    /// 供用户主动显式配置的均价拆单基准，严禁作为 VWAP 的隐式兜底替身。
    /// </summary>
    Average = 3
}
```

#### 2. 配置归属与默认值 (SSOT)
- `EngineConfig`: 增加 `StrategyUpdateTiming UpdateTiming { get; set; } = StrategyUpdateTiming.OnClose;`
- `BrokerConfig`: 增加 `ExecutionPriceType DefaultExecutionPrice { get; set; } = ExecutionPriceType.Open;`
- **默认标准**：对齐业界主流，默认 `OnClose` 决策、次日 `Open` 成交，消灭收盘偷价模式。

#### 3. 架构极致精简：统一挂单簿（彻底裁撤 PendingNextStepOrders）
- **单一事实来源（SSOT）**：全系统**严禁开辟第二在途订单队列**，所有跨步订单在提交后直接进入权威 `OrderBook`（状态为 `Submitted`），零状态分裂，撤单、状态查询与检查点持久化 100% 复用既有成熟逻辑；
- **跨步市价单预冻结**：第 T 日收盘提交时以已知最新基准价（Close）预估冻结资金；第 T+1 日开盘以真实成交价结算，多退少补并清零浮点尾差；
- **回测终局生命周期边界**：回测结束日期最后一根 Bar 收盘挂出的跨步订单，在引擎收尾退出（`FinishRun`）时由网关统一撤销（`Canceled`），全额解冻预占资金并派发 `OrderEvent`，确保终局账本与持仓快照守恒纯净。

#### 4. 三时钟相位步进状态机（Three-Phase Bar Stepping）

`BacktestingEngine` 单步循环自然划分为三大物理时钟相位，严格遵循金融物理先后次序：

```
【单步推进至时间步 i】
       │
       ▼
【Phase 1: Open 相位 (开盘时刻)】
  ├─ 1.1 推进经纪商跨日清算与昨仓可用解锁 (ProcessDayRollover，先结算后开盘)
  ├─ 1.2 提取绑定当期 Open 矩阵并执行标的停牌与涨跌停状态重估
  ├─ 1.3 驱动网关扫描 OrderBook 执行开盘撮合:
  │      ├─ 正常标的:
  │      │   ├─ 市价单: 以当期 Open (或指定 ExecutionPrice) 撮合成交 (Taker，计入滑点)
  │      │   └─ 限价单: 满足触碰条件按 Maker 撮合成交 (享开盘跳空价格改善，零滑点)
  │      └─ 突然停牌标的 (Open=NaN): 立即执行确定性熔断 Rejected，全额解冻预占资金，杜绝孤儿泄漏！
  ├─ 1.4 若 StrategyUpdateTiming == OnOpen:
  │      ├─ 激活防偷价门面安全守卫 (调用 GetClose(0)/GetHigh(0)/GetLow(0) 严格抛出 InvalidOperationException)
  │      ├─ 驱动开盘指标 (基于截至昨收已完结 Bar 数据)
  │      ├─ 驱动策略: _pipeline.OnUpdate(_context)
  │      └─ 策略产出新订单：
  │         ├─ 若 ExecutionPriceType == Open: 立即以当期 Open 撮合成交！
  │         └─ 若 ExecutionPriceType == VolumeWeightedAveragePrice 或 Close: 留在 OrderBook 挂单簿留待 Phase 3 撮合
  │
       ▼
【Phase 2: Intraday 相位 (盘中时刻)】
  ├─ 2.1 驱动盘中撮合与后续条件单极值检查 (留待阶段四接入 ConditionalTriggerManager)
  │
       ▼
【Phase 3: Close 相位 (收盘时刻)】
  ├─ 3.1 提取当期完整 OHLCV 矩阵，驱动指标注册中心计算 _indicators.Step(_context)
  ├─ 3.2 若存在盘中挂起至收盘成交的订单 (VWAP / Close)：在此时按当期对应矩阵撮合成交！
  ├─ 3.3 若 StrategyUpdateTiming == OnClose:
  │      ├─ 驱动策略: _pipeline.OnUpdate(_context)
  │      └─ 策略产出新订单直接压入 OrderBook 挂单簿并预冻结，留待下一时间步 Phase 1 撮合
  └─ 3.4 记录时间步资产净值点 Record(EquityPoint)
```

#### 5. 因果律守卫与物理隔离防偷价
在 `StrategyUpdateTiming == OnOpen` 模式下，底层共享的 `ChunkMatrix2D<T>` 矩阵保持纯净绝不写脏（严禁就地写入 NaN）；防偷价守卫由 `EngineContext` 门面及只读视图统一裁决：在 `OnOpen` 决策时刻，策略若尝试调用 `context.GetClose(offset = 0)`、`GetHigh(0)`、`GetLow(0)`，**严格抛出 `InvalidOperationException`**。

---

### 阶段四：止损与止盈条件单机制 (P2) - [✅ 已完成 / 759 测试全绿]

> **落地状态与验收凭证（2026-11-21 归档）**：
> - **契约标准化（FIX Tag 40）**：`OrderType : byte`（Market=0, Limit=1, Stop=2, TakeProfit=3, StopLimit=4, TakeProfitLimit=5）全面扩充，全链路基元统一；
> - **Broker 单点算法中枢（SSOT 与零死锁）**：条件单物理属性明确为券商端算法单，统一由 `Broker` 权威托管（`OrderStatus.Pending`）；未触发前持仓保持 100% 可用（触发前零冻结），彻底解决止损与止盈双挂死锁；触发瞬间由 Broker 权威核验 `_ledger.TryFreeze`；
> - **默认时效性收敛（GTC 防持仓裸奔）**：条件单未显式传参时强制默认采用 `TimeInForce.GoodTilCanceled`，日切结算不被误撤单；
> - **四象限定价彻底消灭日内偷价**：纯函数一次性精确裁决，跳空开盘按 Open 惩罚成交（空头高开/多头低开），盘中击穿按 StopPrice 成交，高开止盈享受 Open 价格改善；
> - **统一 Bracket 括号单管理**：彻底弃用散装 `string linkedOrderId`，底层引入 `OcoGroupId` 原子化管理互斥对，门面一键 `context.Bracket(...)`，任意一侧触发自动联动撤销另一侧；
> - **合规流动性统一内核**：触发后无缝接入 `MatchingEngine` 既有的 `VolumeLimit` 与 `CheckLimit` 机制，一字跌停无量自然判定 `NoFill` 排队，拒绝特例硬编码；
> - **单根 Bar 双触碰悲观仲裁**：极端日内波动同时触碰止损与止盈时，自营量化风控准则优先判定止损生效并联动注销止盈；
> - **检查点快照无损守恒**：底层 `BrokerSnapshot.ActiveOrders` 原生支持 `OrderStatus.Pending`，零接口改动无损支持断点续跑；
> - **全量测试回归**：专用测试套件 `ConditionalOrderTests.cs`（12 个严苛用例）100% 通过，全工程 759 个测试全部绿色通过（0 失败，0 跳过）。

#### 1. 契约定义 (`Abstraction/Engine/OrderTypes.cs`)
止损与止盈作为一等公民正式纳入基元体系，对齐国际 FIX 协议 Tag 40 标准：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 订单基础执行类型（对齐 FIX Protocol Tag 40 标准）。
/// </summary>
public enum OrderType : byte
{
    /// <summary>
    /// 市价单：以当前市场最优价格立即成交（Taker）。对齐 FIX Tag 40 = 1。
    /// </summary>
    Market = 0,

    /// <summary>
    /// 限价单：以指定价格或更优价格成交（未触碰时在挂单簿排队，Maker）。对齐 FIX Tag 40 = 2。
    /// </summary>
    Limit = 1,

    /// <summary>
    /// 止损市价单：市场行情跌穿/涨破 StopPrice 时触发，转为市价单打出（Taker）。
    /// 行业缩写：STOP，对齐 FIX Tag 40 = 3 (Stop / Stop Loss)。
    /// </summary>
    Stop = 2,

    /// <summary>
    /// 止盈市价单：市场行情冲高/回调触碰 StopPrice 时触发，转为市价单打出（Taker）。
    /// 行业缩写：TP，对齐 FIX Tag 40 = J (Market If Touched，缩写 MIT)。
    /// </summary>
    TakeProfit = 3,

    /// <summary>
    /// 止损限价单：触发后转为限价单挂单排队（Maker）。对齐 FIX Tag 40 = 4。
    /// </summary>
    StopLimit = 4,

    /// <summary>
    /// 止盈限价单：触发后转为限价单挂单排队（Maker）。对齐 FIX Tag 40 = K (Limit If Touched，缩写 LIT)。
    /// </summary>
    TakeProfitLimit = 5
}
```

#### 2. 条件单管理与物理撮合闭环 (第一性原理设计)

- **宿主归属（Broker 单点托管算法中枢，SSOT）**：
  - 条件单物理本质属于**券商端算法单（Broker-hosted Algo Order）**，全生命周期由 **`Broker` 权威单点托管**，`SimulatedExchangeGateway`（网关）保持高内聚，纯粹处理标准报单撮合，绝不穿透持仓账本；
  - 策略提交条件单时，订单在 `Broker` 登记为 **`OrderStatus.Pending`**，**持仓维持完全可用状态（触发前零冻结）**，彻底消灭止盈与止损在同一笔持仓上的冻结死锁；
  - 触发瞬间由 `Broker` 单点执行 `_ledger.TryFreeze`：
    - 若冻结成功：转为正式的市价/限价报单，推向 `_gateway.SendOrder` 撮合成交；
    - 若冻结失败（如持仓已被提前平仓）：由 `Broker` 权威发布状态为 `Rejected`、理由为 `"Conditional order triggered but pre-freeze failed due to insufficient available position/cash"` 的 `OrderEvent`，实现审计闭环。

- **默认时效性（强制 GoodTilCanceled）**：
  - 条件单（`Stop` / `TakeProfit` / `StopLimit` / `TakeProfitLimit`）在未显式指定 `timeInForce` 时，**默认强制赋予 `TimeInForce.GoodTilCanceled`**（对齐 QuantConnect Lean、NautilusTrader 与国际券商惯例）；
  - 杜绝因日切结算自动撤单导致隔夜持仓无保护“裸奔”；仅在用户显式传入 `TimeInForce.Day` 时遵循当日有效。

- **极简 OCO 括号单（Bracket / OcoGroupId 统一管理）**：
  - 彻底废弃散装 `string linkedOrderId` 互相引用的反模式，避免“鸡生蛋蛋生鸡”与字符串拼写风险；
  - 系统在底层统一引入 **`OcoGroupId`（`long`）** 管理成对互斥；
  - 策略层直接通过极简高频门面一键建仓/挂单保护：
    ```csharp
    // 一键挂出成对的互斥保护单（Bracket / OCO），底层分配统一 OcoGroupId
    context.Bracket(symbol, quantity, stopPrice: 9.0, takeProfitPrice: 12.0);
    ```
  - 任意单边触发成交后，Broker 自动联动撤销同组另一侧条件单，派发 `OrderEvent(Canceled, Reason: "Canceled by OCO bracket trigger.")`。

- **单点一次性纯函数裁决（四象限定价彻底消灭日内偷价）**：
  - 拒绝过度设计，单步内无需开辟复杂的两阶段相位分裂，统一在时间步推进中单点执行纯函数裁决：
    1. **多头止损卖单（Sell Stop）**：低开跳空（`Open <= StopPrice`）按 **`Open`** 成交（承担跳空滑点惩罚）；盘中跌破（`Open > StopPrice && Low <= StopPrice`）按 **`StopPrice`** 成交；
    2. **空头止损买单（Buy Stop）**：高开跳空（`Open >= StopPrice`）按 **`Open`** 成交（承担跳空滑点惩罚）；盘中涨破（`Open < StopPrice && High >= StopPrice`）按 **`StopPrice`** 成交；
    3. **多头止盈卖单（Sell TakeProfit）**：高开跳空（`Open >= TargetPrice`）按 **`Open`** 成交（享受价格改善）；盘中冲高（`High >= TargetPrice`）按 **`TargetPrice`** 成交；
    4. **空头止盈买单（Buy TakeProfit）**：低开跳空（`Open <= TargetPrice`）按 **`Open`** 成交（享受价格改善）；盘中下探（`Low <= TargetPrice`）按 **`TargetPrice`** 成交；
  - 坚决杜绝使用日终 `Close` 撮合导致虚假高位逃顶。

- **合规与流动性统一内核（零特例代码）**：
  - 条件单触发转为正式报单后，直接进入 `MatchingEngine` 既有的撮合管线，**100% 复用现有的 `VolumeLimit` 与 `CheckLimit` 机制**；
  - 若标的出现一字跌停无量，受成交量流动性硬约束自然判定为无法成交并挂在跌停板排队（`NoFill`），严禁无脑成交逃顶。

- **单根 Bar 双触碰悲观仲裁机制（Pessimistic Arbitration）**：
  - 日频回测中若单根 Bar 振幅巨大同时触碰了止盈价与止损价，依据量化自营风控准则，**默认采用悲观仲裁：优先判定止损生效**，TakeProfit 联动注销。

- **检查点持久化天然守恒（零额外数据结构）**：
  - `Broker.cs` 的 `SaveState()` 与 `RestoreState()` 现有逻辑已原生包含 `OrderStatus.Pending` 订单；
  - 条件单以 `Pending` 状态保存在 `Broker._orders` 中，天然被 `BrokerSnapshot.ActiveOrders` 完整序列化与还原，零接口破坏，100% 守恒无缝支持断点续跑。

---

## 四、实施计划与 TDD 落地蓝图

### 1. 总体实施里程碑矩阵

| Milestone | 交付核心组件 | 关键验证手段 |
| :--- | :--- | :--- |
| **Milestone 1<br>(TIF 重构)**<br>`[✅ 已完成/PASS]` | 1. `TimeInForce` 枚举（全拼无缩写）与 `OrderRequest` 升级<br>2. 移除悬空的 `OrderOffset`<br>3. `BrokerConfig.DefaultTimeInForce`<br>4. 统一在 `Broker.SubmitOrder` 权威归一化（SSOT）<br>5. 网关跨日按 TIF 撤单与挂单簿清理 | **验收通过（725 测试全绿）**：<br>- `Day` 挂单跨日撤单并解冻；<br>- `GoodTilCanceled` 挂单跨日继续排队且资金维持冻结；<br>- 跨日混合挂单隔离与次日成交闭环；<br>- 策略高频交易门面（Buy/Sell）完整透传。 |
| **Milestone 2<br>(Maker/Taker & 资产守恒 & 订单黑匣子)**<br>`[✅ 已完成/PASS]` | 1. `LiquidityRole` 枚举<br>2. `ExecutionReport` 挂载 `CumQuantity`, `LeavesQuantity`, `Role`<br>3. 撮合器显式打标与 Broker 尾差清零吸收<br>4. `OrderEventType` 与 `OrderEvent` 实体<br>5. `ITradingRecorder.Record(...)` 统一重载 | **验收通过（740 测试全绿）**：<br>- 即时成交报告 `Role == LiquidityRole.Taker` 且包含滑点；<br>- 排队跳空成交 `Role == LiquidityRole.Maker` 且滑点为 0；<br>- 多次分笔成交断言 `CumQty + LeavesQty == OrderQty`；<br>- 全额成交后剩余冻结资金与持仓清零；<br>- 验证订单全生命周期（Submitted, Fill, Canceled, Rejected）被准确记录。 |
| **Milestone 3<br>(亚 Bar 撮合与时态体系)**<br>`[✅ 已完成/PASS]` | 1. `StrategyUpdateTiming` 与 `ExecutionPriceType` 枚举<br>2. 网关矩阵路由与两阶段步进状态机<br>3. OnOpen 模式防未来函数安全拦截 | **验收通过（747 测试全绿）**：<br>- OnClose 模式搭配 Open/VWAP 的次日撮合与跨日涨跌停拦截；<br>- OnOpen 模式在开盘即刻以当期 Open 成交；<br>- OnOpen 模式在开盘决策并在全天以当期 VWAP 拆单撮合；<br>- OnOpen 模式下尝试读取当期 Close/High/Low 时成功拦截抛错。 |
| **Milestone 4<br>(条件单机制)**<br>`[✅ 已完成/PASS]` | 1. `OrderType.Stop` / `TakeProfit` / `StopLimit` / `TakeProfitLimit`（FIX Tag 40 标准）<br>2. `Broker` 内部集成轻量条件单管理（触发前零冻结）<br>3. 条件单默认强制 `TimeInForce.GoodTilCanceled`<br>4. 统一 Bracket 括号单管理与上下文门面 `context.Bracket(...)`<br>5. 触发后无缝接入既有 `MatchingEngine` 流动性/涨跌停合规机制<br>6. 复用现有 `BrokerSnapshot.ActiveOrders` 天然实现断点续跑 | **验收通过（759 测试全绿）**：<br>- 未触发前持仓与资金维持可用，支持止损与止盈双挂无死锁；<br>- 跳空开盘以 Open 惩罚成交，盘中击穿以 StopPrice 成交；<br>- 一字跌停无量受流动性拦截判定为 NoFill 排队，杜绝假阳性逃顶；<br>- 极值触发判定正确区分不利突破（Stop）与有利冲高（TakeProfit）；<br>- 单根 Bar 双触碰确定性悲观仲裁（优先止损生效，联动注销止盈）；<br>- 检查点保存与恢复时未触发条件单 100% 守恒还原。 |

---

### 2. 每个 Milestone 的 TDD 严格执行标准

为杜绝“大爆炸式重构”引发的测试大面积退化，全链路推行“垂直切片、小步快跑、严格测试驱动（红-绿-重构）”的演进标准：

#### Milestone 1: 订单契约精简与 TIF 收敛（低风险 / 奠定基石）- [✅ 已完成落地并通过审计]
- **交付内容**：
  1. `OrderTypes.cs` 升级：引入 `TimeInForce`（`Day`, `GoodTilCanceled`），删除悬空的 `OrderOffset`，挂载 `StopPrice`；
  2. `BrokerConfig` 属性收敛为 `DefaultTimeInForce`；
  3. `Broker.SubmitOrder` 作为全局唯一业务归一化单点（SSOT）；
  4. `SimulatedExchangeGateway.Step` 跨日撤单按 `order.TimeInForce == TimeInForce.Day` 过滤，挂单簿完成清理。
- **TDD 落地闭环（已验证）**：
  1. **测试用例集**：`TimeInForceTests.cs` 与 `SimulatedExchangeGatewayTests.cs`：
     - 测试场景 A：`Day` 挂单跨日被集中撤单，且预冻结资产 100% 解冻；
     - 测试场景 B：`GoodTilCanceled` 挂单跨日继续保留在挂单簿排队，资金维持冻结；
     - 混合测试场景：同一交易日内混合提交 Day 与 GTC 限价单，跨日撤销 Day 保留 GTC，次日价格触碰时 GTC 正常成交入账。
  2. **全量回归**：全工程 725 个测试全部通过（0 失败，0 跳过）。

#### Milestone 2: 资产守恒闭环、Maker/Taker 归因与订单黑匣子（消除浮点 Bug）- [✅ 已完成落地（740 测试全绿）并通过审计]
- **交付内容**：
  1. `ExecutionReport` 挂载 `CumQuantity`, `LeavesQuantity`, `Role`；
  2. `IExchangeGateway.OnOrderStateChanged` 升级为携带原因的三元签名，打通网关拒单原因透传；
  3. `Broker.HandleExecution` 增加浮点尾差吸收释放（根治浮点微小泄漏）与守恒断言，完善平尽持仓解冻防御；
  4. 引入 `OrderEventType` 与 `OrderEvent`，`IBroker` 暴露 `OnOrderEvent`，`BacktestingEngine` 单向订阅；
  5. `ITradingRecorder.Record(...)` 统一重载，二进制检查点持久化直接升级包含 `OrderEvents`，沉淀至 `TradingResult`。
- **TDD 落地闭环**：
  1. **先写失败测试（Red）**：
     - `AccountLedgerPrecisionTests`：模拟现货买入 300 股分 3 笔 100 股成交（触发浮点除法 $100/300$），断言全额成交后 `RemainingFrozenCash` 绝对等于 `0.0`；
     - `OrderEventBlackboxTests`：断言涨跌停被拒单时，`OrderEvent` 记录 `EventType == Rejected` 且原因清晰。
  2. **编写最小实现（Green）**：在 Broker、网关与记录器中补充尾差清理、事件通知与持久化逻辑；
  3. **老测试同步升级（DRY / 零历史包袱）**：同步更新受接口参数变更影响的已有测试，保证无过渡冗余代码；
  4. **全量回归**：验证所有历史策略与财务报表统计 100% 绿色（740 测试全绿通过）。

#### Milestone 3: 亚 Bar 撮合与多模态时态体系（量化时序核心升级）- [✅ 已完成落地（747 测试全绿）并通过审计]
- **交付内容**：
  1. 引入 `StrategyUpdateTiming`（`OnClose`, `OnOpen`，置于 `EngineConfig.UpdateTiming`，默认 `OnClose`）与 `ExecutionPriceType`（`Open`, `VolumeWeightedAveragePrice`, `Close`, `TypicalPrice`，置于 `BrokerConfig.DefaultExecutionPrice`，默认 `Open`）；
  2. 极致精简架构：彻底裁撤第二队列，所有跨步订单在提交后直接进入权威 `OrderBook` 挂单簿单点管理（SSOT）；
  3. `BacktestingEngine` 升级为清晰的三时钟相位状态机（Phase 1 开盘撮合与昨仓结算，Phase 2 盘中，Phase 3 收盘指标与决策）；
  4. 缺失数据强类型报错：用户显式指定 `VolumeWeightedAveragePrice` 时，若数据源缺失 `vwap` 且无法推导，直接抛出 `InvalidDataException`，坚决不搞隐式降级；
  5. 回测终局边界清理：回测最后一根 Bar 结束时由网关统一撤销在途跨步挂单（`Canceled`），全额解冻资金并派发 `OrderEvent`，保持终局资产与持仓账本纯净；
  6. 引擎在 `OnOpen` 模式下增加防偷看 High/Low/Close 的内存安全守卫（`context.GetClose(0)` 抛出 `InvalidOperationException`，底层只读矩阵不写脏）。
- **TDD 落地闭环**：
  1. **先写失败测试（Red）**：
     - `ExecutionPriceTypeTests`：分别测试 `Open`、`VolumeWeightedAveragePrice` 与 `TypicalPrice` 模式，断言成交价严格等于次日 open 矩阵、vwap 矩阵与四价均值；当缺失 vwap 且无法推导时断言强类型异常；
     - `StrategyOnOpenTests`：开盘即刻 Open 成交，并在尝试读取当期收盘价/最高价/最低价（`offset = 0`）时断言严格抛出 `InvalidOperationException`；
     - `BacktestingEngineRolloverBoundaryTests`：断言回测最后一根 Bar 收盘挂出的次日单在引擎结束退出时被完全撤销解冻，无孤儿资产残留。
  2. **编写最小实现（Green）**：在引擎与网关内部补充三相位步进、开盘撮合与异常拦截；
  3. **全量回归**：确保已有测试与新增时态测试 100% 绿色（747 测试全绿通过）。

#### Milestone 4: 条件单（Stop 止损 / TakeProfit 止盈）独立中枢（风控闭环）- [✅ 已完成落地（759 测试全绿）并通过审计]
- **交付内容**：
  1. `OrderType` 扩充：引入 `Stop` / `TakeProfit` / `StopLimit` / `TakeProfitLimit`（底层基元 `: byte`，对齐 FIX Tag 40）；
  2. `Broker` 内部集成轻量条件单管理（`OrderStatus.Pending`，触发前零冻结）；
  3. 条件单默认时效性收敛：未显式传参时强制采用 `TimeInForce.GoodTilCanceled`，防止隔夜持仓裸奔；
  4. 纯函数单点极值触碰判定与四象限定价模型（跳空低开按 Open 惩罚成交，盘中击穿按 StopPrice 成交）；
  5. 废弃散装 `string linkedOrderId`，统一通过 `Bracket` 括号单管理互斥对，暴露策略上下文门面 `context.Bracket(...)` 与 `context.StopLoss(...)` / `context.TakeProfit(...)`；
  6. 触发后无缝接入既有 `MatchingEngine` 的 `VolumeLimit` 与 `CheckLimit` 合规机制（一字跌停无量自然判定 `NoFill`，杜绝特例代码）；
  7. 检查点持久化复用既有 `BrokerSnapshot.ActiveOrders`（原生包含 `Pending` 状态），零额外结构无损支持断点续跑。
- **TDD 落地闭环**：
  1. **先写失败测试（Red）**：
     - `ConditionalTriggerTests`：同时设置止损与限价止盈，断言持仓不被锁死；
     - `StopOrderPricingTests`：构造跳空低开 Bar，断言成交价等于 Open（非 Close）；
     - `LimitDownStopLossLiquidityTests`：构造一字跌停 Bar，断言卖单无法成交（NoFill）；
     - `ExtremeDualTouchPessimisticArbitrationTests`：单根 Bar 振幅极大双触碰时，断言悲观仲裁优先判定止损生效并注销止盈；
     - `ConditionalOrderCheckpointTests`：断言断点续跑保存与恢复时，未触发条件单 100% 还原；
  2. **编写最小实现（Green）**：完成 Broker 内部条件单管理、极值判定与撮合转单逻辑；
  3. **全量回归**：验证全量 759 个测试 100% 绿色通过，最终验收。

---

## 五、关键单元测试设计

### 1. `Day` 与 `GoodTilCanceled` 跨日隔离测试 (`TimeInForceTests`)
- **场景**：Day 1 分别提交 `TimeInForce.Day` 与 `TimeInForce.GoodTilCanceled` 限价买单，均未触碰价格。
- **验证**：步进至 Day 2，`Day` 订单状态跃迁为 `Canceled` 并解冻资金；`GoodTilCanceled` 订单保持 `Submitted` 且资金维持冻结。

### 2. 多次分笔成交浮点尾差吸收测试 (`AccountLedgerPrecisionTests`)
- **场景**：委托买入 300 股，单价 10.0 元，连续推送 3 笔 100 股分笔成交。
- **验证**：每笔成交均满足 `CumQty + LeavesQty == 300m`；第 3 笔成交后 `RemainingFrozenCash == 0.0`，无浮点残留。

### 3. Maker / Taker 角色归因测试 (`MatchingEngineRoleTests`)
- **场景**：
  - 动作 1：市价单立即买入；
  - 动作 2：前一日限价买单，次日开盘跳空低开触碰。
- **验证**：动作 1 返回 `Role == LiquidityRole.Taker` 并扣除滑点；动作 2 返回 `Role == LiquidityRole.Maker` 并享受开盘跳空价格。

### 4. 订单生命周期事件链与拒单溯源测试 (`OrderEventBlackboxTests`)
- **场景**：模拟涨跌停拦截拒单与正常撤单流程。
- **验证**：`TradingResult.OrderEvents` 中准确记录拒单原因（`Reason`），撤单呈现完整的生命周期记录。

### 5. 止损止盈双挂无持仓死锁测试 (`ConditionalTriggerTests`)
- **场景**：持有 1000 股标的，设置止损单 `StopLoss(8.0, 1000)`，随后策略挂出 `TakeProfit(12.0, 1000)`。
- **验证**：Stop 与 TakeProfit 在未触发前均不锁定持仓，可用持仓保持 1000 股；当行情下跌至 7.9 元触发止损后，TakeProfit 自动被联动撤销注销（OCO 语义）。

### 6. 止损日内跳空与触碰定价测试 (`StopOrderPricingTests`)
- **场景**：止损价 8.0 元，推送 Bar `Open = 7.5, Low = 7.0, Close = 8.2`。
- **验证**：触发后成交价为 `7.5`（Open），而非 `8.2`（Close）或 `8.0`（StopPrice）。

### 7. 开盘更新（OnOpen）与当期撮合模式测试 (`StrategyOnOpenExecutionTests`)
- **场景**：配置 `StrategyUpdateTiming.OnOpen`，测试以下组合：
  - 组合 A（`ExecutionPriceType.Open`）：策略在开盘看到当期 Open = 10.0 并发出买单，验证当场以 10.0 成交并落账；
  - 组合 B（`ExecutionPriceType.VolumeWeightedAveragePrice`）：策略开盘买入，验证在收盘时刻以当期 vwap 矩阵价格完成成交；
  - 组合 C（防未来函数验证）：策略在 `OnUpdate` 中尝试调用 `context.GetClose(0)`，断言严格抛出 `InvalidOperationException`。

### 8. 收盘更新（OnClose）与次日 VWAP 拆单测试 (`StrategyOnCloseVwapTests`)
- **场景**：配置 `StrategyUpdateTiming.OnClose` 与 `ExecutionPriceType.VolumeWeightedAveragePrice`。
- **验证**：第 T 日收盘策略发出买单，第 T 日收盘持仓为 0；推进至第 T+1 日，订单以第 T+1 日的 `vwap` 矩阵价格完成成交入账。

### 9. 一字跌停板卖出止损拦截测试 (`LimitDownStopLossLiquidityTests`)
- **场景**：标的出现一字跌停（`Open == High == Low == Close == LimitDownPrice`，`Volume = 0`），策略持有股票并挂有止损单。
- **验证**：跌停价格触碰止损线，但撮合引擎识别出一字跌停封死且无流动性，**判定为 `NoFill`（挂在跌停板排队），绝对断言不以 Open 价格成交逃顶**，杜绝假阳性。

### 10. 跨步订单标的突然停牌孤儿资金熔断测试 (`OrphanFrozenCashOnSuspendedSymbolTests`)
- **场景**：第 T 日收盘提交跨步买单，第 T+1 日标的突发停牌（整行时序均为 `NaN`）。
- **验证**：第 T+1 日开盘网关立即触发熔断机制，订单状态跃迁为 `Rejected` 并记录拒绝原因，**断言关联的预冻结现金 100% 退回可用资金，绝对无孤儿资金残留**。

### 11. 条件单触发时持仓不足被拒闭环测试 (`ConditionalOrderInsufficientAssetTests`)
- **场景**：挂出止损单，但在触发前策略因其他逻辑提前手动卖空了持仓；随后行情暴跌触碰止损价。
- **验证**：触发器转正式单时因可用持仓不足被拒，系统正确派发 `EventType == Rejected` 的 `OrderEvent`，错误原因清晰明确，系统不崩溃且账本无负数。

### 12. 空头与止盈跳空四象限定价测试 (`ShortPositionGapFillPricingTests`)
- **场景**：
  - 空头持有空单并挂止损买单，次日开盘跳空高开暴涨；
  - 多头持仓挂止盈卖单，次日开盘跳空高开大涨。
- **验证**：
  - 空头止损在跳空高开时按恶化的 `Open` 价格成交（严格计入滑点惩罚）；
  - 多头止盈在跳空高开时按更高的 `Open` 价格成交（享受价格改善）。

### 13. 单根 Bar 双触碰确定性悲观仲裁测试 (`ExtremeDualTouchPessimisticArbitrationTests`)
- **场景**：单根 Bar 振幅巨大，日内极值同时满足了止盈价（High 触碰）与止损价（Low 触碰）。
- **验证**：在默认悲观仲裁规则下，系统**必须且仅能判定止损生效**，TakeProfit 触发器联动撤销，杜绝不确定性二义性。

### 14. 撮合热路径零 GC 堆分配断言测试 (`ZeroAllocationHotPathTests`)
- **场景**：在 1000 个密集时间步循环中驱动多标的跨步撮合与条件单触碰判定。
- **验证**：通过 `GC.GetAllocatedBytesForCurrentThread()` 进行量化断言，稳态撮合循环中增量内存分配严格为 0 bytes。

### 15. 回测终局生命周期边界跨步挂单撤销解冻测试 (`RolloverBoundaryTerminalCancelTests`)
- **场景**：回测结束日期为 2020-12-31，策略在最后一根 Bar 收盘挂出次日开盘买单并预冻结资金，随后回测主循环正常完结退出。
- **验证**：引擎在 `FinishRun` 阶段由网关自动撤销残留挂单，派发 `OrderStateReport(Canceled, "Terminal cancellation on backtest completion.")` 与 `OrderEvent`，断言最终可用现金 `AvailableCash` 100% 解冻归位，无任何孤儿冻结资金。

### 16. VWAP 缺失数据严格强类型报错测试 (`VwapMissingDataStrictExceptionTests`)
- **场景**：用户显式配置 `ExecutionPriceType.VolumeWeightedAveragePrice`，但数据源缺少 `vwap` 列且无 `amount/volume` 列推导。
- **验证**：撮合引擎严格抛出强类型 `InvalidDataException` 阻断执行，断言绝不隐式降级伪实现为四价均值。

---

## 六、红线守恒与回归验证

- **内存守恒**：热路径新增枚举底层均为基元 `byte`，无装箱开销，热循环保持 0 bytes GC 增量；
- **全量回归**：实施各阶段后执行全工程 700+ 单元测试，确保已有用例无退化。
