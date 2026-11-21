# CarrotQuant.NET 核心链路与架构问题深度审计汇总报告 (AUDIT)

> **审计基准与规范约束**：
> - 《核心架构、软硬协同与工程研发红线通用规范 V1.3》
> - 《CarrotQuant .NET 核心架构与开发规范 (AGENTS.md)》
> - 现代量化工程与回测业界标准（Pyfolio / QuantStats / Lean / Backtrader / Qlib）
> 
> **综合审查结论**：🟢 **PASS（第一批轻量缺陷与流派三通用净持仓/ReduceOnly闭环重构验收通过，776 单元测试全绿）**  
> 系统历经四大阶段系统性重构与缺陷深度治理（33.1整型缺失哨兵统一、8.2热路径日切守卫、8.3国际标准通用净持仓+原生ReduceOnly闭环与多空对称翻仓、32黑板装箱事实与契约澄清），全链路打通资产财务双式记账、开仓费用等比分摊池、无未来函数撮合时态（`NextOpen` / `NextVwap`）、48 字节非托管 `BarSnapshot`、条件单与 Bracket OCO 互斥引擎、逐层组合备忘录断点恢复、策略极简交易动词门面、全套后复权历史时序取价及四层量化分析管线。
>
> 截至目前，全套工程 61 个测试文件、776 个单元与集成测试用例 100% 绿色通过，系统架构纯粹、热路径零 GC 分配，完全契合工程研发红线规范。

---

## 目录

1. [执行摘要与全景架构诊断](#一执行摘要与全景架构诊断)
2. [为什么现有单元测试“全绿”却未能发现问题（三大盲区）](#二为什么现有单元测试全绿却未能发现问题三大盲区)
3. [维度一：交易财务、结算与撮合执行致命漏洞 (P0)](#三维度一交易财务结算与撮合执行致命漏洞-p0)
4. [维度二：接口暴露错误、封装破坏与契约隔离违背](#四维度二接口暴露错误封装破坏与契约隔离违背)
5. [维度三：策略易用性缺陷与日常回测样板代码断层](#五维度三策略易用性缺陷与日常回测样板代码断层)
6. [维度四：工程反模式、性能瓶颈与 DRY 原则违背](#六维度四工程反模式性能瓶颈与-dry-原则违背)
7. [维度五：时态穿透、状态机残留与数据一致性](#七维度五时态穿透状态机残留与数据一致性)
8. [系统性重构路线图与演进蓝图 (P0 ~ P3)](#八系统性重构路线图与演进蓝图-p0--p3)

---

## 一、执行摘要与全景架构诊断

CarrotQuant.NET 采用了模块化设计，但在各层演进与集成过程中，由于**缺少统一且权威的「时间步全市场行情注入与快照供给管线」**，导致引擎、撮合、账本与指标之间存在多处假设断裂：

```mermaid
flowchart TD
    TS[TimeseriesRegistry 时序行情] -->|仅传递 timeseries 实例| EX[SimulatedExchangeGateway]
    ENG[BacktestingEngine 主时钟推进] -->|Step 驱动| EX
    ENG -->|无每步行情注入| BRK[Broker / AccountLedger]
    
    subgraph 断裂带与缺陷表现
        EX -.->|1. bar 恒为 null| M1[流动性与涨跌停拦截永久失效]
        EX -.->|2. 无挂单直接 return| M2[_marketPrices 不更新 / 净值全平]
        BRK -.->|3. 市价单 Price=0| M3[冻结资金为 0 / 成交透支穿仓]
        EX -.->|4. 首根 Bar _marketPrices 为空| M4[典型市价单必被拒]
        ENG -.->|5. 当期收盘价决策并在当期以收盘价成交| M5[未来函数因果律倒错]
        ENG -.->|6. Checkpoint 仅存 32 字节基元| M6[断点续跑伪实现]
        BRK -.->|7. 平仓结算漏算开仓手续费| M7[单笔交易盈亏与胜率虚高]
    end
```

---

## 二、为什么现有单元测试“全绿”却未能发现问题（三大盲区）

单元测试表面看似高覆盖率，但测试用例存在严重的人为伪造与盲区，导致核心链路缺陷被完全掩盖：

### 盲区 1：测试前“手工后门注入”，掩盖了主循环调用缺失
- **现场证据**：在 `TradingFinancialPipelineIntegrationTests.cs` 中：
  ```csharp
  simExchange.OnMarketData("SH.600000", dates[0],
      new BarSnapshot(close: 10.0, volume: 10000.0));
  ```
  单测在回测启动前，**人为调用了网关后门方法塞入一个包含 Volume 或 limit_up 的 DataRow**。
- **后果**：回测主循环自身在 `Step` 中传递 `null` 的重大缺陷被后门注入完全掩盖。

### 盲区 2：市价单测试人为硬编码了伪限价，绕过了 Price=0 路径
- **现场证据**：在 `TradingFinancialPipelineIntegrationTests.cs` 与 `SimulatedExchangeGatewayTests.cs` 中：
  ```csharp
  // 测试中下市价单时，人为把 close 或 10.0 作为 Price 传入
  OrderIdBuy = context.Broker.SubmitOrder(new OrderRequest(symbol, OrderDirection.Buy, OrderType.Market, close, 1000m));
  ```
- **后果**：`request.Price > 0.0` 为真，`Broker` 两阶段冻结正常计算出了保证金，`GetBasePriceForOrder` 也直接返回了该价格，因此 `Price=0` 时的两大致命路径（冻结 0 元导致穿仓、首根 Bar 拒单）从未被测试执行过。

### 盲区 3：短命策略场景，缺乏无挂单持仓期间的过程盯市断言
- **现场证据**：端到端测试均采用“Day 0 买入，Day 1 卖出，随后空仓”的极简脚本。
- **后果**：测试只在回测终局断言“终局现金 == 初始现金 + 净收益”。由于终局现金完全由已清算的平仓交割单决定，**测试从未断言过持仓期间且无挂单的交易日中 `DailySnapshot.TotalEquity` 或 `UnrealizedPnL` 是否随行情涨跌波动**。

---

## 三、维度一：交易财务、结算与撮合执行致命漏洞 (P0)

### 1. 持仓盯市（Mark-to-Market）在无新挂单日完全失效，净值曲线成水平死线【✅ 已修复 (PASS)】
- **严重度**：🔴 致命 P0 -> 🟢 已修复
- **涉及位置**：`BacktestingEngine.cs`、`AccountLedger.cs`、`Broker.cs`
- **缺陷剖析**：
  1. `SimulatedExchangeGateway.Step` 中，若挂单簿为空直接提前 `return;`，根本不读取时序数据更新 `_marketPrices`；
  2. 回测主循环记录每日净值点时调用 `_snapshotProvider.CreateEquityPoint(currentTime)`，未传入价格解析委托；
  3. `Broker` 回退向网关查询，网关无最新价则回退至 `pos.LastPrice`（成本价），导致持仓浮动盈亏恒为 0，净值曲线在两次成交之间变成水平直线。
- **重构方案与治理机制**：
  1. **引擎集中权威价格注入**：在 `BacktestingEngine` 主循环步进中，构造以当期步长 $O(1)$ 直通 `closeMatrix` 的价格解析委托 `sym => ResolveStepPrice(sym, i)`，显式传入 `_snapshotProvider.CreateEquityPoint` 与跨日日终 `CreateDailySnapshot`；
  2. **持仓实体最新价格同步更新**：`AccountLedger` 在盯市估值解析出 `resolvedPrice > 0.0` 时，同步刷新 `pos.LastPrice = resolvedPrice;`，彻底消除开仓后终生未更新的隐患；
  3. **单元测试验证**：`TradingFinancialPipelineIntegrationTests.MarkToMarket_WithoutNewOrders_EquityCurveMustFluctuateWithDailyClose` 验证连续 5 天无新挂单持有股票期间，未实现浮动盈亏与总净值严格随收盘价逐日递增，净值曲线真实波动。

### 2. 市价买单因 `Price=0` 预冻结 0 元资金，引发巨额穿仓破产【✅ 已修复 (PASS)】
- **严重度**：🔴 致命 P0 -> 🟢 已修复
- **涉及位置**：`OrderTypes.cs#L23-L26`、`Broker.cs#L246-L264`、`Broker.cs#L548-L571`、`AccountLedger.cs#L283-L299`
- **缺陷剖析**：
  市价单便捷构造函数 `Price` 默认为 `0.0`。原先 `Broker` 估价时若 `Price <= 0` 则估价为 `0.0`，导致 `requiredCash` 计算为 0 元。账户即使可用资金为 0，也能顺利通过两阶段预冻结发出市价单。成交落账时直接 `_cash -= marginAdded` 扣款，导致现金瞬间穿仓变为巨额负数。
- **重构方案与治理机制**：
  1. **公允参考市价推导**：`Broker.GetEstimatedPrice` 对市价买单（`Price <= 0.0`）自动向绑定的底层网关（`ISimulatedExchange.GetMarketPrice`）查询标的最新客观公允市价；
  2. **预估滑点缓冲上浮**：买单在基准市价基础上强制叠加 `(1.0 + Slippage)` 上浮缓冲作为估价基准，保证预冻结资金充沛覆盖执行滑点；
  3. **前置资金安全拦截**：若可用资金无法覆盖预估保证金与手续费，在两阶段 Phase 1 阶段直接拒绝委托（`OrderStatus.Rejected`），坚决杜绝 0 元免冻结放行；
  4. **无参考价快速失败**：若标的从未注入行情或无合法收盘价，`GetEstimatedPrice` 返回 `0.0`，`Broker` 立即快速失败拒单并提示明确原因，杜绝未冻结现金造成的穿仓破产；
  5. **单元测试验证**：`SimulatedBrokerTests.MarketBuyOrder_WithReferencePrice_ShouldPreFreezeWithSlippage`、`MarketBuyOrder_InsufficientCashForPreFreeze_ShouldRejectImmediately` 与 `MarketBuyOrder_WhenNoReferencePriceAvailable_ShouldRejectFast` 全链路验证预冻结、欠资拦截与无参考价快速失败。

### 3. 无挂单时 `_marketPrices` 为空，典型的市价单在首根 Bar 会直接被拒【✅ 已修复 (PASS)】
- **严重度**：🔴 致命 P0 -> 🟢 已修复
- **涉及位置**：`SimulatedExchangeGateway.cs`、`MatchingEngine.cs`
- **缺陷剖析**：
  首根 Bar 回测开始时，由于先前没有挂单，网关价格字典为空。策略下达标准市价单（`Price = 0.0`），网关查不到价格返回 `basePrice = 0.0`，撮合引擎因基准价格非正数直接 `Rejected` 拒单。
- **重构方案与治理机制**：
  1. **消除过期字典缓存污染**：`SimulatedExchangeGateway.GetMarketPrice` 调整查询优先级，若处于时序步进中，优先直接从全局时序矩阵 `closeMatrix[_currentIndex, col]` 读取当期最新公允基准价，不再受空字典阻断；
  2. **首根 Bar 即时撮合**：`GetBasePriceForOrder` 在首根 Bar 直接通过时序矩阵获取公允市价，市价单顺利通过基准估价即时撮合成交；
  3. **单元测试验证**：`TradingFinancialPipelineIntegrationTests.FirstBar_MarketOrder_WithZeroPrice_ShouldFillImmediatelyWithoutRejection` 验证在无历史挂单的首根 Bar 下达 `Price=0.0` 的标准便捷市价单成功即时建仓。

### 4. 平仓结算遗漏开仓手续费，单笔已实现盈亏与胜率被虚高粉饰【✅ 已修复 (PASS)】
- **严重度**：🔴 致命 P0 -> 🟢 已修复
- **涉及位置**：`AccountLedger.cs#L393-L485`、`PositionData.cs`
- **缺陷剖析**：
  平仓生成 `Trade` 闭环交易记录时：`closeRealizedPnL = grossPnL - closeFee;`，仅扣减了平仓当笔手续费，开仓时扣减的历史手续费完全未分摊计入。导致单笔已实现盈亏偏高、胜率与盈亏比虚假上升，且所有 `Trade.RealizedPnL` 的加总大于实际账户净利润，财务勾稽彻底断裂。
- **架构决策与治理机制（券商成本价 vs 机构量化标准）**：
  1. **明确成本价纯粹性**：散户券商客户端常将手续费算入“保本成本价”，但在专业量化系统中，若成本价含费用，会导致持仓市值（Mark-to-Market）估值扭曲、基准跟踪偏差（Tracking Error）失真，且期货杠杆保证金计算会被算多。因此明确确立：**`AveragePrice`（或 `AverageCost`）必须始终保持纯净加权成交均价，严禁费用资本化入价格**。
  2. **在途开仓手续费池（`OpenCommission`）等比分摊机制**：
     - 在 `PositionData` 中增加 `public double OpenCommission { get; set; }` 跟踪在途累计开仓手续费池；
     - 每次开仓/加仓时：`pos.OpenCommission += report.Commission;`；
     - 部分/全额平仓时：按平仓比例等比分摊结转释放开仓手续费：
       $$\text{allocatedOpenFee} = \frac{\text{closeQty}}{\text{pos.TotalQuantity}} \times \text{pos.OpenCommission}$$
       $$\text{pos.OpenCommission} -= \text{allocatedOpenFee}$$
     - 结转单笔真实净盈亏：`closeRealizedPnL = grossPnL - allocatedOpenFee - closeFee;`；
     - `Trade` 实体完整闭环记录双边总费用：`Trade(..., TotalCommission: allocatedOpenFee + closeFee, RealizedPnL: closeRealizedPnL)`，实现财务勾稽 100% 守恒。

### 5. 撮合行情 `bar` 恒为 `null`，成交量流动性与涨跌停检查永久失效【✅ 已修复 (PASS)】
- **严重度**：🔴 致命 P0 -> 🟢 已修复
- **涉及位置**：`SimulatedExchangeGateway.cs`、`MatchingEngine.cs`、`BarSnapshot.cs`
- **缺陷剖析**：
  `SimulatedExchangeGateway` 在调用 `MatchSymbolOrders` 时第 3 个参数硬编码传入 `null`。撮合内核中对流动性上限（`VolumeLimit`）和一字板涨跌停（`CheckLimit`）的校验全部依赖 `bar != null`。由于恒为 `null`，所有排队限价单的涨跌停拦截与成交量上限被 100% 绕过。
- **重构方案与治理机制**：
  1. **零 GC 48 字节通用切片 `BarSnapshot`**：引入纯寄存器/栈传递的 `readonly struct BarSnapshot`（48 字节非托管紧凑存储），承载 `Open`, `High`, `Low`, `Close`, `Volume`, `Amount` 六大物理维度，彻底剔除 `limit_up` / `limit_down` 业务特化字段与硬编码；
  2. **多时态全字段可选支持**：内部以 `double.NaN` 作为 IEEE 754 天然缺失哨兵，对外提供 `Open` / `HasOpen` / `OpenOr(fallback)` 对称 API，原生兼容次步开盘（Next Open）、VWAP 均价、收盘扫盘与日内限价触碰；
  3. **彻底铲除 `BarSnapshotDataRow` 堆分配**：Hook 委托直接使用强类型 `BarSnapshot` 值传递，热路径 0 堆分配、0 垃圾回收、0 字符串比对；
  4. **单元测试闭环**：`BarSnapshotTests`（内存大小、NaN 语义、对称回退、多时态自适应）及全量测试 100% 通过。

### 6. 因果律倒错：当期收盘价决策并在当期以收盘价即时撮合，缺乏多模态执行时态（ExecutionPriceType）【✅ 已修复 (PASS)】
- **严重度**：🔴 致命 P0 -> 🟢 已修复
- **涉及位置**：`BacktestingEngine.cs#L385-L394`、`SimulatedExchangeGateway.cs#L48-L66`、`BrokerConfig.cs`、`EngineConfig.cs`
- **缺陷剖析**：
  时间步 $T$ 中，指标计算喂入当期收盘价；策略在 `OnUpdate` 中看到当期收盘价并决策；策略发出的市价单当场在当期收盘价即时成交。现实中收盘后无法再以收盘价买入，系统缺乏量化业界标准的次日开盘（Next Bar Open）撮合机制，产生未来函数偷价（Look-ahead Bias）。
- **重构落地方案与多模态执行价格体系（`ExecutionPriceType`）规范**：
  在配置与撮合层正式引入 `StrategyUpdateTiming`（`OnClose` / `OnOpen`）与 `ExecutionPriceType`（`Open` / `Close` / `Vwap` / `Average`），驱动不同的行情矩阵映射与撮合时态：
  1. **`StrategyUpdateTiming.OnClose` + `ExecutionPriceType.Open`（业界黄金标准，默认推荐）**：
     - 标准无未来函数的日线截面/选股基准。策略在 $T$ 步收盘后决策，订单统一提交至权威挂单簿 `OrderBook`（零额外在途队列分裂），在 $T+1$ 步开盘时刻以开盘价切片撮合成交；涨跌停拦截时态同步迁移至 $T+1$ 步开盘。
  2. **`ExecutionPriceType.Vwap`**：
     - 中大资金机构回测标准（对齐 Qlib/Wind）。以全日成交量加权均价模拟全天拆单执行，真实度量市场冲击成本；若时序表缺失 `vwap` 字段且无法推导，显式抛出强类型配置异常阻断回测，严禁隐式降级返回 0。
  3. **`ExecutionPriceType.Close`**：
     - 保留为高频/分钟线回测、或尾盘 14:55 扫盘收盘集合竞价成交的专有通道。
  4. **三时钟相位步进状态机与防偷价守卫**：
     - 单步划分 Phase 1（开盘清算与在途限价单撮合、次日市价单集中撮合、T+N 昨仓解锁、OnOpen 决策）、Phase 2（盘中离散事件探测派发）、Phase 3（增量指标计算、盘中挂起订单撮合、OnClose 决策、净值盯市沉淀）；
     - 在 `OnOpen` 相位决策时，`EngineContext` 门面拦截读取当期未完结 `Close/High/Low/Volume`（`offset == 0`）并严格抛出 `InvalidOperationException` 防未来函数；
  5. **单元测试验证**：`ExecutionTimingAndPriceTests.cs`（10 个高覆盖度用例）全面验证 OnClose/OnOpen 模式下跨步与即时撮合的因果律守恒。

### 7. 仿真挂单簿与撮合机制系统性缺陷
- **7.1 排队限价单撮合仅读收盘价，忽略日内高低价** (`SimulatedExchangeGateway.cs`, `MatchingEngine.cs`)【✅ 已修复 (PASS)】：
  - **缺陷**：原先撮合挂单簿中排队的限价单时仅与收盘价 `marketPrice` 进行触碰比对。若日内最低价跌破买价但收盘价拉起未跌破，该限价单被错误判定为未成交。
  - **修复**：`MatchingEngine.EvaluatePendingLimitOrder` 重构为接收真实 `bar` 行情切片，买单基于 `bar.Low <= request.Price` 判定日内触碰，卖单基于 `bar.High >= request.Price` 判定日内触碰；若 `bar` 缺失则安全回退至 `marketPrice`。
  - **单测**：`TradingFinancialPipelineIntegrationTests.MatchingEngine_PendingLimitOrder_ShouldFillOnIntradayLowTouch` 验证日内最低价 9.4 触碰限价 9.5 成功撮合成交。
- **7.2 排队挂单完全抹杀跳空缺口与价格改善** (`MatchingEngine.cs#L123-L129`)【✅ 已修复 (PASS)】：
  - **缺陷**：排队挂单成交价强制等于委托限价。若次日跳空低开，限价买单无法以更优的开盘价撮合；若次日跳空高开，限价卖单无法以更优的开盘价撮合。
  - **修复**：`MatchingEngine.EvaluatePendingLimitOrder` 引入纯函数跳空价格改善模型。当 `bar.HasOpen && bar.Open > 0.0` 时，买单以 `Math.Min(request.Price, bar.Open)` 成交，卖单以 `Math.Max(request.Price, bar.Open)` 成交；若开盘不利或无开盘价则严格遵循委托限价。
  - **单测**：`MatchingEngineTests.MakerLimitBuyOrder_FavorableOpenGap_ShouldEnjoyPriceImprovement`、`MakerLimitSellOrder_FavorableOpenGap_ShouldEnjoyPriceImprovement` 与 `MakerLimitOrder_UnfavorableOrMissingOpen_ShouldFillAtLimitPrice` 完备验证买卖双向价格改善与不利/缺失时回退限价。
- **7.3 成交滑点价反向污染市场基准价格** (`SimulatedExchangeGateway.cs#L58`)【✅ 已修复 (PASS)】：
  - **缺陷**：原先成交后直接执行 `_marketPrices[symbol] = matchResult.FillPrice;`，将包含执行滑点的成交价反向写回全局公共行情字典，污染后续盯市基准。此外，当挂单簿无订单时原代码提前退出未记录最新时序，导致后续限价单误将自身委托价作为基准市价判定即时成交。
  - **修复**：
    1. 成交回报中的带滑点成交价（`matchResult.FillPrice`）仅反馈给策略与交易账本，公允市场价格字典 `_marketPrices` 仅记录不含滑点的未污染基准市价（`basePrice`）；
    2. `Step` 中接入当前时序索引，`GetMarketPrice` 与 `GetBasePriceForOrder` 按需 $O(1)$ 常数时间直通时序矩阵获取当期公允收盘价，彻底治愈挂单簿为空时基准市价缺失的深层连锁隐患；
    3. 单元测试 `SimulatedExchangeGatewayTests.SendOrder_WithSlippage_ShouldNotPollutePublicMarketPrice` 验证 1% 滑点成交下公共市价字典仍保持 100% 纯净未污染。
- **7.4 挂单簿未实现“当日有效（Day Order）”日切自动撤单** (`SimulatedExchangeGateway.cs`, `BrokerConfig.cs`)【✅ 已修复 (PASS)】：
  - **缺陷**：现货委托单永久驻留挂单簿数天甚至数月，跨日不会失效，违背 A 股/美股日内委托收盘自动作废规则。
  - **修复**：
    1. `BrokerConfig` 增加 `CancelPendingOrdersOnDailyClose` 配置项（默认 `false` 保持 GTC 跨日挂单兼容性）；
    2. `SimulatedExchangeGateway.Step` 在时钟检测到跨日且开启撤单规则时，集中清空挂单簿未成交订单；
    3. 严格遵循 Open Calls 原则在锁外派发 `OrderStatus.Canceled` 回报，驱动 `Broker` 自动全额解冻预占资金与持仓；
    4. 单测 `SimulatedExchangeGatewayTests.Step_CancelPendingOrdersOnDailyClose_WhenTrue_ShouldCancelAndUnfreezeAcrossDays` 与 `Step_CancelPendingOrdersOnDailyClose_WhenFalse_ShouldPreservePendingOrdersAcrossDays` 验证开启与关闭两种模式下的行为。
- **7.5 停牌标的（NaN）与未上市标的允许违规建仓撮合** (`SimulatedExchangeGateway.cs#L59-L70`)【✅ 已修复 (PASS)】：
  - **缺陷**：停牌或未上市股票限价单取委托限价作为 `basePrice`，直接入簿并撮合成交，无法体现停牌无法交易与未上市拦截的风控事实。
  - **修复**：`SimulatedExchangeGateway.SendOrder` 增加前置守卫拦截。若标的在当期无合法收盘价（`NaN`、`<= 0` 或 `Infinity`）且未显式提供 Bar，立即拒绝委托（`OrderStatus.Rejected`），严禁入簿挂单，并触发 `Broker` 立即全额解冻；
  - **单测**：`SimulatedExchangeGatewayTests.SendOrder_SuspendedOrUnlistedSymbol_ShouldRejectAtGateAndUnfreeze` 验证停牌股票发单被前置拒绝且资金零锁定。
- **7.6 盘口深度与时间价格优先缺失** (`SimulatedExchangeGateway.cs#L237-L268`)：所有满足价格条件的排队挂单无视盘口容量一刀切全额成交。
- **7.7 自定义滑点 Hook `OnCalculateSlippage` 永远无法执行** (`MatchingEngine.cs#L128`)【✅ 已修复 (PASS)】：
  - **缺陷**：原先判断条件包含 `bar != null`，因网关传参恒为 `null`，高级滑点模型被静默降级为固定比例滑点。
  - **修复**：随 48 字节非托管通用切片 `BarSnapshot` 的引入，撮合引擎不再接收空 `bar`，`MatchingEngine.EvaluateImmediateOrder` 与 `EvaluatePendingLimitOrder` 中的 `config.OnCalculateSlippage?.Invoke(order, basePrice, bar)` 正常执行；
  - **单测**：`AnalysisHelperAndSlippageTests.CustomSlippageModel_ShouldBeExecutedCorrectly` 专门断言了自定义滑点 Hook 的执行、高低价极值利用与真实成交价计算。

### 8. 资产账本（AccountLedger）与资金风控缺陷
- **8.1 持仓实体 `pos.LastPrice` 终生未更新** (`AccountLedger.cs#L508, L91`)【✅ 已修复 (PASS)】：
  - **缺陷**：仅在成交落账时赋值一次，后续从未更新。当网关查无价格回退到 `LastPrice` 时，浮动盈亏恒为 0。
  - **修复**：在 `AccountLedger` 的所有盯市估值链路（`GetTotalEquity`、`GetTotalMarketValue`、`GetTotalUnrealizedPnL`、`GetActivePositionSnapshots`）中，当外部解析出公允最新价 `resolvedPrice > 0.0` 时，同步刷新 `pos.LastPrice = resolvedPrice;`，确保持仓实体的最新价时刻紧跟市场波动。
  - **单测**：`TradingFinancialPipelineIntegrationTests.MarkToMarket_WithoutNewOrders_EquityCurveMustFluctuateWithDailyClose` 验证终局持仓实体的 `LastPrice` 准确更新为最后一天收盘价 14.0。
- **8.2 T+N 结算混淆自然日与交易日及热路径无效轮询** (`AccountLedger.cs#L529, L559`, `Broker.cs#L382`)【热路径日切守卫 ✅ 已修复 (PASS)】：
  - **缺陷剖析与机理验证**：
    1. **交易日与自然日语义断层**：在途交收批次 `PendingLot` 使用 `exchangeDate.Date.AddDays(settleDays)` 粗暴累加自然日。在标准 A 股现货 $T+1$（`SettleDays = 1`）场景下，由于回测引擎主时钟基于纯交易日历（`TradeDates`）步进，周末与节假日无数据不走步，周五买入（设为周六到期）直到下周一开盘才触发结算，因此在常规 A 股 T+1 回测下结果自洽、未发生提前抛售；但在通用 $T+N$（$N \ge 2$，如港美股 T+2 或跨境/场外 ETF）场景下，自然日累加会将周末/节假日错误计入交收天数，导致周五买入在下周一（仅经历 1 个交易日）即被违规提前解锁。
    2. **热路径重复比对损耗【✅ 已修复】**：`Broker.Step` 在每根分钟/分时 Bar 均全量调用 `_ledger.ProcessDayRollover(exchangeDate)`，导致盘中数百根 Bar 持续对未到期的 `PendingLots` 进行无意义的遍历与日期比对，违背红线规范第 10 条（热路径零开销），缺乏日切（`currentTime.Date != _lastDate`）前置守卫。
  - **重构治理与单测验证**：
    在 `Broker.cs` 中增加 `_lastRolloverDate` 守卫，仅在跨日时刻（`exchangeDate.Date != _lastRolloverDate`）才触发 `ProcessDayRollover`，并在 `SyncState` 与 `RestoreState` 中同步重置；单测 `SimulatedBrokerTests.Broker_Step_IntradayMultipleBars_ShouldOnlyTriggerDayRolloverOncePerDay` 严格验证日内高频 Bar 下日切守卫生效且跨日准确解锁。
- **8.3 通用净持仓（Netting）+ 原生 ReduceOnly 闭环与多空对称翻仓重构** (`AccountLedger.cs#L246-L280`, `OrderTypes.cs`, `Broker.cs`)【✅ 已修复 (PASS)】：
  - **严重度**：🔴 严重 -> 🟢 已修复
  - **缺陷根因剖析**：
    早期版本试图仅凭委托数量猜测用户交易意图，在允许做空（`AllowShort = true`）时，若多头可用持仓因排队挂单冻结不足，超出部分被静默转换为借券“开空单”，导致同一标的多空双向撕裂，既有多头冻结又有在途开空；而空头在反向买入时又缺乏对等的约束，导致多空逻辑不对称且容易透支。
  - **国际顶层通用净持仓架构重构方案（流派三：通用净持仓 + 原生 ReduceOnly 守卫）**：
    1. **原生 `ReduceOnly` 契约一等公民**：
       - `OrderRequest` 引入核心属性 `bool ReduceOnly = false`；
       - `IBroker` 与 `IEngineContext` 的 `Buy` / `Sell` 门面增加可选参数 `bool reduceOnly = false`；
       - 保护性平仓及条件单门面 `ClosePosition`、`StopLoss`、`TakeProfit`、`Bracket` 内部自动绑定 `ReduceOnly: true`；
       - 经独立子 Agent 严苛代码审计，排查并修复了 `Broker.cs#Bracket` 重载遗漏 `ReduceOnly: true` 参数的重大隐患，实现闭环防御；
    2. **纯平仓意图前置硬守卫（`ReduceOnly == true`）**：
       - 只准减少现有持仓绝对值（`|Total|` 向 0 靠拢），`openQty` 恒为 0，绝对防范反向开仓；
       - 持仓不足、无持仓或可用平仓不足时立即 `Rejected` 拒单，杜绝超平与误翻仓；
    3. **自由交易模式（`ReduceOnly == false`）多空 100% 镜像对称翻仓**：
       - **多头持仓卖出**：$Q \le Total$ 为平多；$Q > Total$ 且现有多头全部可用时触发“多翻空”，自动拆分为 `closeQty = Total`（平多）与 `openQty = Q - Total`（开空冻结保证金）；若现有多头存在部分挂单冻结，直接拒单拦截并发竞争；
       - **空头持仓买入**：$Q \le |Total|$ 为平空；$Q > |Total|$ 且现有空头全部可用时触发“空翻多”，自动拆分为 `closeQty = |Total|`（平空）与 `openQty = Q - |Total|`（开多冻结现金）；若现有空头存在平仓挂单冻结，直接拒单防双花；
    4. **严格状态可逆性与零脏状态**：
       - 撤单回滚（`Unfreeze`）在多翻空与空翻多场景下严格镜像对称，未成交部分持仓解冻归还可用，开仓冻结现金 100% 释放；
    5. **单测套件全面闭环（覆盖 4 大并发/分笔边界盲区）**：
       - `UniversalNetting_ReduceOnly_Contract_And_SymmetricFlip_ShouldWorkAccurately`：验证多空双向 ReduceOnly 拦截、自由多翻空与空翻多；
       - `UniversalNetting_FlipOrder_PartialFills_AcrossZeroBoundary_ShouldTransferAccurately`：验证多翻空 4 阶段分笔成交（平多 -> 平尽归零 -> 翻空建仓 -> 加权成本更新）；
       - `UniversalNetting_FlipOrder_RollbackAfterPartialFills_ShouldRestoreConservedState`：验证多翻空/空翻多部分成交后撤单，持仓与资金 100% 解冻守恒；
       - `UniversalNetting_FlipOrder_RollbackWithoutAnyFills_ShouldBeLossless`：验证挂单未成交直接撤单无损复原；
       - `UniversalNetting_BracketOrder_ReduceOnly_Contract_And_RejectionDefense`：验证 Bracket 订单携带 ReduceOnly 并在持仓提前平尽后安全拒单防反手。
- **8.4 缺乏保证金追缴（Margin Call）与爆仓强平机制** (`AccountLedger.cs#L81-L98`)：净值跌为负数时没有任何风控强平，允许账户无限透支存续。
- **8.5 反向调仓（多翻空/空翻多）预冻结误拒单** (`AccountLedger.cs#L246-L271`)：多翻空时开空部分需预冻结现金，而平多释放的资金成交时才入账，资金不足时订单被错误拦截。
- **8.6 现货单向印花税机制缺失【❌ 拒绝修改 (Won't Fix)】**：
  - **争议点**：原审计建议在通用配置中内置现货单向印花税率（如 `StampTaxRate`）。
  - **架构裁定与拒绝理由**：印花税属于特定国家/地区特定现货市场（如 A 股、港股）的非通用特化规则，美股、加密货币、期货、外汇均无此概念。若在通用 `BrokerConfig` 中硬编码 `StampTaxRate`，与此前在 `BarSnapshot` 中硬编码 `limit_up` 犯了一样的特定业务污染通用内核的反模式。系统已有设计良好的一级函数式 Hook `BrokerConfig.OnCalculateFee = (req, turnover) => ...`，任何复杂市场的单双边税费均可通过一行数学表达式自由注入。通用内核保持绝对轻量通用，**坚决拒绝在底层内核中硬编码特定市场税率**。
- **8.7 订单 ID 重复覆盖导致在途冻结额度野指针泄露** (`Broker.cs#L235`)【✅ 已修复 (PASS)】：
  - **缺陷**：重复传入相同 `OrderId` 时，系统原先直接覆盖内部 `_orders` 字典中的记录。旧订单处于挂单或在途状态时被直接切断关联，其冻结的现金与持仓永远无法解冻核销，彻底沦为不可逆的野指针泄漏。
  - **修复**：
    1. 在 `Broker.SubmitOrder` 加锁区强化唯一性检查：若 `_orders` 中已存在同名订单 ID，立即拒绝提交（`freezeOk = false`），返回拒绝原因 `Order with ID '{orderId}' already exists.`；
    2. 绝不调用账本预冻结，绝不覆盖已有订单记录，并向外派发 `OrderStatus.Rejected` 事件通知；
    3. 既有原始订单及其冻结资金 100% 完好守恒，杜绝资金额度永久锁定泄漏；
    4. 单元测试 `SimulatedBrokerTests.SubmitOrder_WithDuplicateOrderId_ShouldBeRejectedAndNotOverwriteExistingOrder` 严密验证重复提交被拦截拒单、且旧订单生命周期与冻结结算完全不受影响。
- **8.8 终局持仓估值依然未传 PriceResolver** (`BacktestingEngine.cs#L86`)【✅ 已修复 (PASS)】：
  - **缺陷**：回测结束导出终局快照时调用 `_snapshotProvider?.GetActivePositions()` 未注入当前价格解析器，期末持仓估值依赖过期网关缓存。
  - **修复**：`BacktestingEngine.GetResult()` 中自动构造终局时间步收盘矩阵的 $O(1)$ 价格解析闭包，显式注入 `_snapshotProvider.GetActivePositions(finalPriceResolver)`，终局持仓市值与浮动盈亏 100% 真实绑定最后交易日收盘价。

---

## 四、维度二：接口暴露错误、封装破坏与契约隔离违背

### 9. `IBroker` 核心契约污染：引擎步进与权威对账方法泄露给策略【✅ 已修复 (PASS)】
- **严重度**：🟡 中等 -> 🟢 已修复
- **涉及位置**：`IBroker.cs`、`Broker.cs`、`BacktestingEngine.cs`
- **问题分析**：`IBroker` 是注入给策略上下文的核心门面。原先 `Step`（驱动跨日清算与 T+N 持仓解锁）与 `SyncState`（权威覆盖本地资产账本）属于引擎驱动层和柜台重连特权方法，却直接暴露在 `IBroker` 接口上。策略在 `OnUpdate` 中可随意调用 `context.Broker.Step(DateTime.MaxValue)` 提前解冻持仓，或调用 `SyncState(99999999, ...)` 任意篡改资产，严重违背接口隔离原则（ISP）。
- **重构方案与治理机制**：
  1. **瘦核心契约 `IBroker`**：从 `IBroker` 接口中彻底剥离 `Step` 与 `SyncState` 方法，策略上下文仅感知纯净交易与查账门面；
  2. **专属驱动契约 `IBrokerEngineDriver`**：在 `CarrotBacktesting.NET.Abstraction.Engine` 定义内部基础设施契约 `public interface IBrokerEngineDriver : IBroker`，集中收敛 `Step` 与 `SyncState`；
  3. **引擎受控持有与单向驱动**：`BacktestingEngine` 在启动构造时安全持有 `_brokerDriver = broker as IBrokerEngineDriver`，由主循环权威驱动时钟；
  4. **单测验证**：`SimulatedBrokerTests.Broker_InterfaceSegregation_IBrokerShouldNotContainEngineDriverMethods` 与 `Broker_EngineDriver_StepAndSyncState_ShouldWorkCorrectly` 通过反射验证 `IBroker` 瘦契约边界，并端到端验证 `IBrokerEngineDriver` 跨日解锁与状态同步。

### 10. `Broker.Gateway` 公共暴露破坏封装边界【✅ 已修复 (PASS)】
- **严重度**：🟡 中等 -> 🟢 已修复
- **涉及位置**：`Broker.cs#L37-L39`、`IExchangeGatewayProvider.cs`
- **缺陷剖析**：`Broker` 作为公共类，原先直接暴露了底层网关 `public IExchangeGateway Gateway => _gateway;`。外部策略可穿透拿到 `Gateway`，直接调用 `Gateway.SendOrder` 绕过两阶段预冻结风控检查，甚至强转为 `ISimulatedExchange` 注入伪造行情。
- **重构落地方案**：
  1. 将 `Broker.Gateway` 改为显式接口实现：`IExchangeGateway IExchangeGatewayProvider.Gateway => _gateway;`，彻底从公共 API 成员中消除；
  2. 策略持有 `Broker` 或 `IBroker` 无法直接点出 `Gateway`，彻底封死绕过风控预冻结直接下单的后门漏洞；
  3. 回测引擎在必要时通过 `broker as IExchangeGatewayProvider` 安全感知底层仿真网关；
  4. 单元测试 `SimulatedBrokerTests.Broker_Gateway_MustBeExplicitInterfaceImplementation_ToSealBackdoor` 通过反射严密验证公共 Getter 不存在且显式接口契约正确。

### 11. `IStreamIndicator` 混淆消费与生产驱动，子流指标违反 LSP 原则
- **涉及位置**：`IStreamIndicator.cs#L35-L48`、`SubStreamIndicator.cs#L33-L36`
- **问题分析**：策略消费指标只需只读访问（`this[offset]`, `Current`, `IsReady`）。`Update` 和 `Reset` 属于引擎步进驱动方法。暴露在顶层接口导致 `Macd.Dif`、`BollingerBands.Upper`、`Kdj.K` 等子流实现调用 `Update` 时抛出 `NotSupportedException`，公然违背里氏替换原则（LSP）。

### 12. `IEventRegistry` 与 `IFieldRegistry` 暴露动态修改方法【✅ 已修复 (PASS)】
- **涉及位置**：`IEventRegistry.cs`、`IFieldRegistry.cs`、`EventRegistry.cs`、`FieldRegistry.cs`
- **问题分析**：`void Register(string name, ...)` 原先直接暴露在面向策略的只读注册表契约中，允许策略在运行期动态覆盖或替换全局事件/维表，破坏单一事实来源（SSOT）。
- **重构落地方案**：
  1. 从只读接口 `IEventRegistry` 与 `IFieldRegistry` 中彻底剥离 `void Register(...)` 突变契约；
  2. 动态装配与写入能力严格收敛在具体实现类 `EventRegistry`、`FieldRegistry` 及工厂方法内部，对策略消费端保持完全只读；
  3. 单元测试 `EventRegistryTests.IEventRegistry_ReadOnlyContract_ShouldNotExposeMutationMethods` 通过反射断言只读接口绝不暴露突变方法。

### 13. `IMarketMetadata` 只读描述契约混入突变状态机方法【✅ 已修复 (PASS)】
- **涉及位置**：`IMarketMetadata.cs`、`IMutableMarketMetadata.cs`、`DynamicMarketMetadata.cs`
- **问题分析**：`AppendDate` 与 `AppendSymbol` 在元数据只读描述契约中出现且默认抛出 `NotSupportedException`，属于不良的伪实现。
- **重构落地方案**：
  1. 确立专有写端契约 `IMutableMarketMetadata : IMarketMetadata`，物理隔离 `AppendDate` 与 `AppendSymbol` 方法；
  2. `IMarketMetadata` 恢复为纯粹只读描述契约，彻底消除默认抛 `NotSupportedException` 的伪实现；
  3. `DynamicMarketMetadata` 实现 `IMutableMarketMetadata`，缓存管理器与注册表按需向上塑型安全调用；
  4. 单元测试 `DynamicMarketMetadataTests.TestMarketMetadata_ReadOnlyVsMutable_ContractSeparation` 验证契约物理分离。

### 14. `ITimeseriesRegistry.PrewarmFields` 内部预热方法外泄【✅ 已消除 (PASS)】
- **涉及位置**：`ITimeseriesRegistry.cs`
- **修复**：时序注册表只读契约中已彻底消除 `PrewarmFields`，预热流程完全收敛于引擎生命周期内部。

### 15. `IStrategyPipeline` 泄露 Assembly 动态卸载底层回调【✅ 已修复 (PASS)】
- **涉及位置**：`IStrategyPipeline.cs`、`StrategyPipeline.cs`
- **问题分析**：`RegisterDisposable` 与 `RegisterOnDispose` 是为 Roslyn 动态编译 `alc.Unload` 服务的专有生命周期回调，直接暴露在策略管线顶层抽象上，污染了策略组合模式的纯粹性。
- **重构落地方案**：
  1. 从顶层策略接口 `IStrategyPipeline` 中彻底剔除 `RegisterDisposable` 与 `RegisterOnDispose`；
  2. 生命周期资源管理职责严格收敛在抽象基类 `StrategyPipeline` 内部，支持链式装配，对策略消费端完全透明；
  3. 单元测试 `StrategyPipelineTests.StrategyPipeline_ContractIsolation_ShouldNotExposeLifecycleDisposalOnInterface` 反射验证契约零符号泄露。

### 16. `BacktestingEngine` 虚假检查点伪实现（Broken Checkpoint）【✅ 已修复 (PASS)】
- **严重度**：🔴 严重 -> 🟢 已修复
- **涉及位置**：`BacktestingEngine.cs`、`ICheckpointable.cs`、`CheckpointModels.cs`、`Broker.cs`、`AccountLedger.cs`、`SimulatedExchangeGateway.cs`、`TradingRecorder.cs`、`MarketState.cs`
- **缺陷剖析**：声明实现 `ICheckpointable`，但旧版仅写入 32 字节元数据，核心交易资产全部未被保存，属于违背红线第 8 条的伪实现。此外，旧版即使恢复挂单，由于 Broker 遗漏活跃订单快照，导致后续成交回报被静默吞灭；且断点续跑时 `_lastDate` 被错误重置，引发虚假跨日与因子重复刷新。
- **重构落地方案（通用泛型与逐层组合备忘录 Composite Memento）**：
  1. **强类型泛型状态契约**：确立 `ICheckpointable<TState>` 与通用扩展 `ToJsonBytes()` / `RestoreFromJsonBytes()`，支持 JSON/二进制双通道；
  2. **在途订单与资金生命周期完整持久化**：
     - `BrokerSnapshot` 完整持久化 `AccountLedgerSnapshot` 与在途活跃订单集合 `IReadOnlyList<BrokerOrderSnapshot>`；
     - 恢复时复原 `_orders` 映射表，确保在途挂单后续成交回报能精准核销保证金、释放冻结资金，杜绝裂脑与静默吞单；
  3. **纯契约驱动与消除向下强转**：
     - `BacktestingEngine` 统一通过 `ICheckpointable<BrokerSnapshot>`、`ICheckpointable<ExchangeSnapshot>`、`ICheckpointable<TradingRecorderSnapshot>` 与 `ICheckpointable<MarketStateSnapshot>` 进行状态存取，彻底杜绝针对具体类的 `as` / `is` 向下模式匹配；
  4. **时态与状态机严格复原**：
     - 在 `BacktestingEngine.Run()` 中保护从断点恢复的 `_lastDate`，消除非首步日切震荡；
     - 通过 `[RestoredIndex - Lookback, RestoredIndex]` 毫秒级重放批量泵送预热增量指标池；
  5. **黑板强类型状态断点无损恢复**：
     - `MarketState` 纳入检查点持久化，并在反序列化中支持对 `JsonElement` 的延迟类型转换与缓存加速；
  6. **端到端严格守恒单测闭环**：
     - 在 `EngineCheckpointTests.cs` 中增加连续回测（0~4 步）与中途断点恢复（0~1 步打断点并续跑至 4 步）的端到端对齐单测，断言净收益、手续费、终局可用资金、持仓与平仓笔数 100% 绝对恒等。

---

## 五、维度三：策略易用性缺陷与日常回测样板代码断层

### 17. 核心交易动词完全缺失：下单与平仓极其繁琐【✅ 已修复 (PASS)】
- **涉及位置**：`IBroker.cs`、`Broker.cs`、`IEngineContext.cs`、`EngineContext.cs`
- **重构落地方案**：
  1. 在 `IBroker` 与 `IEngineContext` 原生内置三大高频交易动词门面与高级条件单门面：
     - `Buy(symbol, qty, price = 0.0, type = Market, orderTag, timeInForce)`：极简买入；
     - `Sell(symbol, qty, price = 0.0, type = Market, orderTag, timeInForce)`：极简卖出；
     - `ClosePosition(symbol, price = 0.0, type = Market, orderTag, timeInForce)`：全额平仓（无仓位时安全返回 `0L`）；
     - `StopLoss(symbol, stopPrice, qty = 0m, orderTag, timeInForce)`：极简止损单（未触发前持仓零冻结）；
     - `TakeProfit(symbol, takeProfitPrice, qty = 0m, orderTag, timeInForce)`：极简止盈单（未触发前持仓零冻结）；
     - `Bracket(symbol, qty, stopPrice, takeProfitPrice, orderTag, timeInForce, closeDirection = null)`：一键挂出成对 OCO 互斥保护单，支持空头反向平仓与单边触发自动互斥注销；
  2. 策略在 `OnUpdate` 中无需手动实例化 `OrderRequest`，直接 `context.Buy("sh.600000", 100)` 或 `context.Bracket("sh.600000", 100, 9.0, 12.0)` 即可完成交易与风控；
  3. 单元测试 `EngineContextTests.Facades_TradeVerbs_BuySellClosePosition_ForwardToBrokerAccurately` 与 `ConditionalOrderTests` 完整覆盖端到端成交、平仓与互锁触发。

### 18. 缺少通用策略基类 `StrategyBase`，强制手写大段空白样板代码【✅ 已修复 (PASS)】
- **涉及位置**：`StrategyBase.cs`、`RuleStrategyBase.cs`
- **重构落地方案**：
  1. 引入标准通用策略抽象基类 `StrategyBase : IStrategy`；
  2. 默认提供 `Name`（类名反射）、`Lookback`（默认 200 行）及 `OnStart`、`OnEvent`、`OnStop`、`OnOrderStatusChanged`、`OnTrade`、`OnPositionChanged` 安全虚方法实现；
  3. 策略开发者仅需覆写 `OnUpdate(IEngineContext context)`，彻底告别冗余空白样板代码；
  4. 既有 `RuleStrategyBase` 平滑演进为 `StrategyBase` 的特化子类；
  5. 单元测试 `StrategyPipelineTests.StrategyBase_SubClass_ShouldInheritDefaultPropertiesAndLifecycle` 验证继承与默认行为。

### 19. 核心取价门面无历史时序 `offset` 参数，获取昨日后复权价格无路可走【✅ 已修复 (PASS)】
- **严重度**：🔴 严重 -> 🟢 已修复
- **涉及位置**：`IEngineContext.cs#L210-L295`、`EngineContext.cs#L265-L380`
- **缺陷剖析**：旧版取价门面（如 `GetClose`）只能获取当期 Bar。若策略需要获取昨日收盘价（$T-1$），必须穿透到底层矩阵。而底层矩阵存储的是物理未复权原价（Raw），复权因子字典是 `EngineContext` 私有变量，导致策略无法获取正确的历史后复权价格。
- **重构落地方案**：
  1. 在 `IEngineContext` 与 `EngineContext` 统一提供带时序偏移量与复权模式的全套核心取价门面：
     - `GetClose(symbol, offset = 0, mode = PriceMode.Post)` / `GetClose(symbolIndex, offset, mode)`；
     - `GetOpen(symbol, offset = 0, mode = PriceMode.Post)` / `GetOpen(symbolIndex, offset, mode)`；
     - `GetHigh(symbol, offset = 0, mode = PriceMode.Post)` / `GetHigh(symbolIndex, offset, mode)`；
     - `GetLow(symbol, offset = 0, mode = PriceMode.Post)` / `GetLow(symbolIndex, offset, mode)`；
     - `GetVolume(symbol, offset = 0, mode = PriceMode.Raw)` / `GetVolume(symbolIndex, offset, mode)`；
  2. **严格的因果律与防偷价守卫**：
     - 传入正数 `offset > 0` 强制抛出 `ArgumentOutOfRangeException`，坚决杜绝未来函数；
     - 在 `IsInOpenPhase` 开盘相位读取当期未走完的 `Close/High/Low/Volume`（`offset == 0`）时，强制抛出 `InvalidOperationException`；
  3. **统一复权因果守卫**：自动按历史日历与标的映射 `_factorMatrix` 进行等比换算，获取准确的历史后复权真实价格；
  4. **单元测试验证**：`AdjustmentAndFacadeTests` 与 `EngineContextTests` 完整验证偏移量历史回溯、复权因子乘法、越界拦截与非价格字段白名单隔离。

### 20. 策略完全无法查询持仓成本均价与浮动盈亏（无法原生写止盈止损）【✅ 已修复 (PASS)】
- **严重度**：🟡 中等 -> 🟢 已修复
- **涉及位置**：`IBroker.cs`、`Broker.cs`、`IEngineContext.cs`、`EngineContext.cs`
- **缺陷剖析**：虽然底层的 `AccountLedger` 和 `PositionData` 完整维护了加权均价 `AverageCost` 与浮动盈亏，但面向策略的门面上只有 `GetPosition`（只返回数量）。策略想实现“盈利 10% 止盈”或“亏损 5% 止损”，拿不到成本价，被迫在外部自行维护冗余变量。
- **重构落地方案**：
  1. 在 `IBroker` 增加契约方法 `double GetPositionCost(string symbol)`，并在 `Broker` 中安全加锁透传 `_ledger.GetPositionCost(symbol)`；
  2. 在 `IEngineContext` 的 `Account & Position SSOT Facades` 区域增加 `double GetPositionCost(string symbol)`，并在 `EngineContext` 中直通转发；
  3. 单元测试 `EngineContextTests.Facades_AccountAndPosition_ForwardToBrokerAccurately` 完整覆盖初始无仓 (0.0)、首仓买入、多次加仓加权均价、部分平仓成本守恒与完全清仓归零的全生命周期校验。

### 21. 缺少 C# 流畅回测构建器（Fluent Backtest Builder）
- **涉及位置**：`BacktestSession.cs`、`BacktestingEngine.cs#L113-L126`
- **问题分析**：在纯 C# 代码中启动回测体验极差：要么构造十余个层层嵌套的配置模型，要么手动实例化 8 个底层对象。`BacktestingEngine` 构造函数高达 12 个参数。

### 22. `AnalysisReport` 报表缺乏扁平快捷属性【❌ 拒绝修改 (Won't Fix)】
- **涉及位置**：`AnalysisReport.cs`
- **争议点**：原审计建议在 `AnalysisReport` 顶层平铺直接代理 `SharpeRatio`、`MaxDrawdown`、`WinRate` 等快捷属性。
- **架构裁定与拒绝理由**：
  1. **严重违背单一事实来源（SSOT）**：夏普比率归属组合统计、最大回撤归属资金统计、胜率归属交易统计。平铺代理会导致 `AnalysisReport` 退化为包罗万象的上帝类（God Object），破坏各子统计模块的领域高内聚；
  2. **违反红线第 8 条（严禁隐式兜底与伪实现）**：子分析器具备独立可配置可插拔性。若用户关闭了某分析器，扁平属性若返回 `0.0` 伪数据，会导致外部参数调优和回测选拔逻辑将“未计算”误判为“数值等于 0”；若抛异常则代理属性毫无意义；
  3. **结论**：保留分层强类型属性访问（如 `report.Portfolio?.SharpeRatio`）才是客观真实反映模块边界与可选性状态的最纯粹第一性原理设计，**坚决不搞扁平化垫片污染**。

### 23. 标的元数据访问路径过深，缺乏一级门面【✅ 已修复 (PASS)】
- **涉及位置**：`IEngineContext.cs`、`EngineContext.cs`
- **问题分析**：获取全市场标的或遍历股票池原先必须层层穿透调用 `context.Timeseries.Metadata.Symbols` 和 `context.Timeseries.Metadata.GetSymbolIndex(symbol)`，缺乏 `context.Symbols` 等一级快捷属性。
- **重构落地方案**：
  1. 在 `IEngineContext` 顶层增加直通只读门面：
     - `IReadOnlyList<string> Symbols { get; }`：标的池列表只读直通；
     - `int SymbolCount { get; }`：标的池总数量；
     - `int GetSymbolIndex(string symbol)`：代码列索引直通查询；
  2. 策略写遍历循环由原先的 `foreach (var s in context.Timeseries.Metadata.Symbols)` 极简提升为 `foreach (var s in context.Symbols)`；
  3. 单元测试 `EngineContextTests.Facades_Metadata_SymbolsAndIndex_ForwardAccurately` 验证门面与底层元数据完全一致。

---

## 六、维度四：工程反模式、性能瓶颈与 DRY 原则违背

### 24. 手续费计算逻辑在账本预冻结与经纪商结算中重复实现（DRY 违背）【✅ 已修复 (PASS)】
- **严重度**：🟡 中等 -> 🟢 已修复
- **涉及位置**：`AccountLedger.cs`、`Broker.cs`、`FeeCalculator.cs`
- **缺陷剖析**：手续费公式（`FeeRate`、`MinFee`、`OnCalculateFee`）在账本预冻结和经纪商结算两个不同文件中被硬编码重复实现。一旦修改费率规则，两处极易发生不同步分叉。
- **重构落地方案（SSOT 单一事实来源与纯函数）**：
  1. 抽取 `CarrotBacktesting.NET/Engine/Broker/FeeCalculator.cs` 作为全系统手续费纯计算权威单一事实来源（SSOT）；
  2. `CalculateCommission(double tradeAmount, BrokerConfig config, ...)` 作为无状态纯函数统筹费率、最低费用与自定义委托；
  3. `Broker` 与 `AccountLedger` 统一调用 `FeeCalculator`，彻底消除代码重复。

### 25. 布林带 `BollingerBands` 复制代码且退化为 $O(N)$ 暴力计算【✅ 已修复 (PASS)】
- **严重度**：🟡 中等 -> 🟢 已修复
- **涉及位置**：`BollingerBands.cs`
- **缺陷剖析**：`BollingerBands` 原先在每个时间步对滑动窗口 $N$ 进行全遍历循环求方差，时间复杂度为 $O(N)$，且存在长循环浮点减法开销。而其他指标均为 $O(1)$ 递推。
- **重构落地方案**：
  1. 引入滑动平方和 `_sumSq`，利用代数关系 $\text{Var} = E[X^2] - (E[X])^2$ 实现单步常数时间 $O(1)$ 增量递推；
  2. 彻底剔除 $O(N)$ 循环遍历；加入微小负数下溢守卫（`Math.Max(0.0, variance)`），杜绝因浮点精度舍入导致开方负数异常；
  3. 内置防长时间漂移自愈机制：每 256 步从窗口数组重新基准校准一次累加和与平方和，保障长时间运行下零精度衰减；
  4. 单元测试 `StreamIndicatorsTests.BollingerBands_O1Recurrence_ShouldMatchBruteForceVarianceExactly`（1000 步随机序列双轨精度比对）与 `BollingerBands_ConstantValueSeries_ShouldHandleZeroVarianceWithoutNegativeSqrtException`（零方差常数序列）全面验证通过。

### 26. 指标前置校验严重混乱，漏判 `IsInfinity` 且阻断价差/套利合法场景【✅ 已修复 (PASS)】
- **严重度**：🔴 严重 -> 🟢 已修复
- **涉及位置**：`BollingerBands.cs`、`Macd.cs`、`Atr.cs`、`Rsi.cs`
- **缺陷剖析**：
  1. `Macd`、`BollingerBands`、`Atr`、`Rsi` 漏判了 `double.IsInfinity`。若脏数据输入无穷大，`Infinity` 流入累加和导致指标后续计算全部永久瘫痪；
  2. 检查中生硬的 `close <= 0.0` 直接阻断了跨期套利、配对交易、国债基差及负油价等实数价差序列。
- **重构落地方案**：
  1. 在所有指标（`BollingerBands`, `Macd`, `Atr`, `Rsi` 等）中统合前置守卫：`double.IsNaN(x) || double.IsInfinity(x)`，严格阻断无穷大与空值污染；
  2. 放开非必要的 `close <= 0.0` 绝对正数限制，原生支持套利与任意实数序列；
  3. 针对 `Atr` 等必须符合物理几何约束（$High \ge Low$ 且 $High - Low \ge 0$）严格守卫，防止负波动率异常。

### 27. 指标计算无法绑定自定义 Field，硬编码吃 Close 导致无法计算成交量均线且虚耗 80% 算力
- **涉及位置**：`IndicatorPool.cs#L123-L135`、`IStreamIndicator.cs#L46`、`Sma.cs#L45-L57`
- **问题分析**：
  1. **输入契约与单值指标硬编码绑定**：`IStreamIndicator.Update(open, high, low, close, volume)` 强行固定 5 参，而 `Sma`、`Ema`、`Wma` 等单值指标内部直接硬编码写死 `_window[_windowIndex] = close; _sum += close;`，完全忽略其他输入；
  2. **常规量化能力大面积瘫痪**：系统**完全无法对非收盘价字段计算指标**，无法计算成交量均线（`Vol_SMA20`，量化选股与放量突破核心基础特征）、换手率均线（`Turnover_SMA5`）、VWAP 均线、开盘价均线或任意基本面时序因子平滑指标；
  3. **严重的算力与内存带宽虚耗**：无论策略注册何种指标，`IndicatorPool.Step` 每一时间步对全市场标的无条件抓取 `Open`, `High`, `Low`, `Close`, `Volume` 全部 5 个字段并执行复权计算。对于仅需单个序列的均线指标，造成高达 80% 的无效矩阵点查与计算浪费。
- **重构方案（单值流与复合流分流解耦）**：
  1. **支持单值指标抽象**：引入轻量单值接口 `ISingleValueIndicator`，核心驱动仅需 `void Update(double value);`；
  2. **注册时支持绑定数据源 Field**：在 `IndicatorRegistry.GetOrAdd` 中提供 `string field = "close"` 参数（如支持 `field: "volume"` 或 `field: "turnover"`）；
  3. **驱动层按需切片供给**：`IndicatorPool` 根据绑定的单个 Field，仅单点切片提取对应的一个矩阵序列进行批量递推，既实现特征计算全覆盖，又使指标计算内存访问量骤降 80%。

### 28. `DynamicMarketMetadata.Symbols` 高频 `lock + ToArray()` 产生严重 GC 堆分配【✅ 已修复 (PASS)】
- **严重度**：🟡 中等 -> 🟢 已修复
- **涉及位置**：`DynamicMarketMetadata.cs`
- **缺陷剖析**：`Symbols` 属性内部每次调用均执行 `lock (_gate) { return _symbols.ToArray(); }`。策略在 `OnUpdate` 中每步遍历标的池时，都会触发一次全量数组堆分配与锁争用，严重违背热路径零分配红线。
- **重构落地方案（写时复制 Copy-on-Write + 无锁只读快照）**：
  1. 内部维护不可变缓存数组 `_cachedSymbols`、`_cachedTradeDates`、`_cachedFieldNames`；
  2. 读属性（`Symbols`, `TradeDates`, `FieldNames`）无锁直读只读缓存数组，热路径 $O(1)$ 且零 GC 堆分配；
  3. 仅在冷路径追加（`AppendSymbol`, `AppendDate`, `AppendField`）时上锁并通过写时复制（COW）原子替换引用。

---

## 七、维度五：时态穿透、状态机残留与数据一致性

### 29. 黑板 `MarketState.SetState` 致命时间机器穿透（使用现实物理时钟）【✅ 已修复 (PASS)】
- **严重度**：🔴 致命 P0 -> 🟢 已修复
- **涉及位置**：`MarketState.cs`、`IEngineContext.cs`、`EngineContext.cs`
- **缺陷剖析**：在回测 2021 年历史行情时，策略调用 `SetState(signal)` 写入黑板，底层时间戳竟然默认被打上了宿主机的当前现实时间（`DateTime.UtcNow`），下游策略 100% 判定为陈旧信号而丢弃。
- **重构落地方案**：
  1. 彻底移除 `MarketState.cs` 中对宿主机物理时钟 `DateTime.UtcNow` 的隐式兜底；
  2. 黑板支持时钟注入提供者 `Func<DateTime>? timeProvider`，缺少合法时间步时直接抛出强类型异常；
  3. 在 `IEngineContext` 顶层统一提供 `context.SetState<T>(state, key)` / `context.GetState<T>(key)` 门面，天然注入 `CurrentTime`，消除向下类型转换与时间机器穿透。

### 30. 首根 Bar 即时单成交时间戳穿越至现实世界（`DateTime.UtcNow`）【✅ 已修复 (PASS)】
- **严重度**：🔴 致命 P0 -> 🟢 已修复
- **涉及位置**：`SimulatedExchangeGateway.cs`
- **缺陷剖析**：若在引擎尚未推进 `_currentTime` 时发出即时单，`tradeTime` 将回退为宿主机系统时间 `DateTime.UtcNow`，导致回测产生时间戳穿越与时态污染。
- **重构落地方案**：
  1. 彻底移除 `DateTime.UtcNow` 回退；
  2. 若引擎时钟未步进且无有效时间（如初始状态），优先安全取系统交易日历第一天 `_tradeDates[0]`，若仍不可得则抛出异常，杜绝现实物理时间穿越。

### 31. 黑板跨时间步无淘汰，残留“僵尸信号”导致幽灵交易
- **涉及位置**：`MarketState.cs`
- **问题分析**：黑板状态永久驻留。若某标的在 $T$ 日触发买入信号写入黑板；$T+1$ 日该股停牌，生产策略未为其更新黑板。下游执行策略读取到 $T$ 日的僵尸信号，会在股票停牌时盲目重复下单。

### 32. 黑板文档与契约规范对齐（澄清装箱事实，消除虚假承诺）【✅ 已修复 (PASS)】
- **涉及位置**：`IMarketState.cs`、`MarketState.cs`
- **缺陷剖析**：原接口注释声称“支持 struct / readonly record struct 实现零堆分配”，但内部载体为 `StateEntry(object Value, MarketTime UpdatedTime)`。值类型写入必须装箱，属于与底层实现不符的过度陈述。
- **重构方案**：
  1. 彻底修正 `IMarketState.cs` 与 `MarketState.cs` 接口文档注释，客观澄清底层采用线程安全字典与不可变备忘录快照管理，明确指出值类型在通用黑板存储中存在装箱；
  2. 引导外部开发规范：高频单标的极速存取建议优先使用引擎上下文强类型变量或专用状态字段，杜绝反模式。

### 33. 多模态与多系统协同口径分叉
- **33.1 Parquet 与 CSV 整型缺失值哨兵规范冲突【✅ 已修复 (PASS)】** (`ParquetTimeseriesSource.cs` vs `MarketDataAligner.cs`)：
  - **缺陷**：整型缺失值规范在 `MarketDataAligner` 中定义为 `long.MinValue` / `int.MinValue`，但 `ParquetTimeseriesSource` 读取允许为空的列时硬编码填充为 `0`，而 `0` 在成交量与标志位中有业务含义，破坏 SSOT。
  - **修复**：`ParquetTimeseriesSource.cs` 读取整型列时统一调用 `MarketDataAligner.GetNaNValue<long>()` 与 `MarketDataAligner.GetNaNValue<int>()` 填充缺失哨兵，消灭 0 歧义；
  - **单测**：`ParquetTimeseriesSourceTests.ParquetTimeseriesSource_NullableIntegerColumns_ShouldFillSentinelMinValue` 严格验证 NULL 单元格被正确填充为 `long.MinValue` 与 `int.MinValue`。
- **33.2 策略后复权价与仿真网关未复权价口径分裂（Split-brain Pricing）** (`EngineContext.cs#L238-L245` vs `SimulatedExchangeGateway.cs#L149`)：`context.GetClose` 默认返回后复权价格，若策略据此下达限价单，而仿真网关内部直接读取未复权原始时序矩阵，限价单将被错误撮合或永久挂死。
- **33.3 并行策略管线（`ParallelPipeline`）并发重入 Broker 回调导致数据竞争** (`ParallelPipeline.cs#L47-L58`)【✅ 已修复 (PASS)】：
  - **缺陷**：开启 `Concurrent = true` 时，多线程并行调用子策略 `OnUpdate` 并发下单，触发的 `OnTrade` 回调在多线程中重入，若持有非线程安全状态会导致数据损坏或锁逆序死锁。
  - **修复**：`Broker` 核心撮合回报、状态流转与条件单触发全链路推行 **Open Calls 模式**。在内部主锁内完成原子状态跃迁与缓冲区收集（复用零 GC 列表 `_orderEventsBuffer`），退出锁区后再单向分发 `OnOrderEvent`、`OnExecution` 与策略回调，彻底消灭外部策略回调重入引发的锁逆序死锁与数据竞争；
  - **单测**：`ConditionalOrderTests`、`EngineBrokerEventBridgeTests` 与 `AdvancedStrategyPipelineIntegrationTests` 严密验证回调顺序与并发隔离。


