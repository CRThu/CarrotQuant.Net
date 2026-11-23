# 交易所仿真与集中撮合体系 (Exchange Simulation & Matching System)

本文档面向系统架构师与策略开发者，深入介绍 CarrotQuant.NET 中**交易所仿真子系统**（`CarrotBacktesting.NET/Engine/Exchange/`）的核心契约（`IExchangeGateway`、`ISimulatedExchange`）、标的桶化限价挂单簿（`OrderBook`）、纯计算撮合引擎（`MatchingEngine`）、响应式纯行情驱动（`OnMarketData`）以及回测与实盘平滑替换规范。

> [!NOTE]
> 关于投资者账户资金账本、两阶段挂单预冻结及 T+N 结算，请参阅专门文档 [客户端经纪商与账户账本体系 (broker.md)](broker.md)。

---

## 目录
- [一、交易所子系统总览与架构设计](#一交易所子系统总览与架构设计)
- [二、核心契约设计 (Contracts)](#二核心契约设计-contracts)
  - [1. 物理通信协议 (IExchangeGateway)](#1-物理通信协议-iexchangegateway)
  - [2. 仿真驱动契约 (ISimulatedExchange)](#2-仿真驱动契约-isimulatedexchange)
- [三、标的桶化时间价格优先挂单簿 (OrderBook)](#三标的桶化时间价格优先挂单簿-orderbook)
  - [1. 标的桶化与双向优先队列 (Bids & Asks)](#1-标的桶化与双向优先队列-bids--asks)
  - [2. 时间价格优先排队规则 (Price-Time Priority)](#2-时间价格优先排队规则-price-time-priority)
  - [3. 空桶自动物理清理 (内存防泄漏)](#3-空桶自动物理清理-内存防泄漏)
  - [4. 常数时间撤单寻址 (_ordersById)](#4-常数时间撤单寻址-_ordersbyid)
- [四、纯计算撮合引擎 (MatchingEngine)](#四纯计算撮合引擎-matchingengine)
  - [1. 无状态计算设计](#1-无状态计算设计)
  - [2. Maker 被动触碰规则与委托限价保护](#2-maker-被动触碰规则与委托限价保护)
  - [3. Taker 即时成交规则与滑点截断保护](#3-taker-即时成交规则与滑点截断保护)
  - [4. 流动性成交量硬约束 (VolumeLimit)](#4-流动性成交量硬约束-volumelimit)
  - [5. 涨跌停封板与停牌拦截 (CheckLimit)](#5-涨跌停封板与停牌拦截-checklimit)
  - [6. 手续费与摩擦模型](#6-手续费与摩擦模型)
- [五、响应式纯行情驱动撮合 (OnMarketData)](#五响应式纯行情驱动撮合-onmarketdata)
  - [1. 纯行情驱动撮合机制](#1-纯行情驱动撮合机制)
  - [2. 显式时间戳强绑定 (防时间穿越)](#2-显式时间戳强绑定-防时间穿越)
  - [3. 离线时间步撮合 (Step)](#3-离线时间步撮合-step)
  - [4. 单一事实来源权威撮合内核 (MatchSymbolOrders)](#4-单一事实来源权威撮合内核-matchsymbolorders)
- [六、锁外异步回报派发 (DispatchReports & Open Calls)](#六锁外异步回报派发-dispatchreports--open-calls)
- [七、对称 Hook 矩阵与配置参数](#七对称-hook-矩阵与配置参数)
- [八、实盘柜台适配与平滑替换 (CTP / 币安)](#八实盘柜台适配与平滑替换-ctp--币安)

---

## 一、交易所子系统总览与架构设计

在集中撮合交易体系中，交易所的核心本质是一个**基于行情数据驱动的订单撮合状态机**。它不关心投资者的账户内部细节（那是券商/经纪商的职责），只对订单意图、挂单簿队列、成交规则与市场行情负责：

```mermaid
graph TD
    subgraph ClientSide [客户端 / 策略]
        Broker[Broker 客户端门面]
    end

    subgraph ExchangeSubsystem [交易所仿真子系统 (Engine/Exchange/)]
        Gateway[SimulatedExchangeGateway<br>仿真网关门面 / 锁外回报派发]
        OrderBook[OrderBook<br>标的桶化 FIFO 限价挂单簿 / 自动销毁空桶]
        MatchingEngine[MatchingEngine<br>纯计算撮合器 / Maker·Taker / 滑点与流动性]

        Gateway --> OrderBook
        Gateway --> MatchingEngine
    end

    Broker -->|IExchangeGateway.SendOrder / CancelOrder| Gateway
    Gateway -->|OnExecution / OnOrderStateChanged| Broker
```

- **纯计算无状态撮合**：`MatchingEngine` 只包含撮合逻辑与数学模型，不持有可变状态，输入为订单请求与当前行情，输出为结构化判定结果 `MatchResult`；
- **状态集中于挂单簿**：`OrderBook` 仅维护排队中的未成交限价单，按照标的精准桶化与时间优先排队；
- **纯行情驱动**：撮合动作由行情数据（`OnMarketData`）或离线时间步（`Step`）触发，与离散业务事件完全正交解耦。

---

## 二、核心契约设计 (Contracts)

### 1. 物理通信协议 (IExchangeGateway)
`CarrotBacktesting.NET/Abstraction/Engine/IExchangeGateway.cs` 定义了客户端与交易所/柜台之间的标准通信协议：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface IExchangeGateway
{
    // 发送报单与撤单请求
    void SendOrder(string orderId, OrderRequest request);
    void CancelOrder(string orderId);

    // 成交明细回报与订单状态跃迁事件
    event Action<ExecutionReport> OnExecution;
    event Action<OrderStateReport> OnOrderStateChanged;
}
```

### 2. 仿真驱动契约 (ISimulatedExchange)
`CarrotBacktesting.NET/Abstraction/Engine/ISimulatedExchange.cs` 为模拟回测环境下的交易所仿真网关提供时钟推进与行情灌入能力：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface ISimulatedExchange : IExchangeGateway
{
    // 获取当前时间步的不可变行情物理切片（Open, High, Low, Close, Volume 等，零 GC 48 字节值类型）
    BarSnapshot GetBar(string symbol);

    // 显式校准交易所当前物理时间
    void SetCurrentTime(MarketTime currentTime);

    // 离线时间步推进与矩阵撮合（微观时钟相位驱动，零历史兼容重载）
    void Step(MarketTime currentTime, IMarketDataProvider marketData, int currentIndex, EnginePhase phase);

    // 纯行情数据驱动撮合（唯一物理切片权威输入，48 字节栈传递零 GC）
    void OnMarketData(string symbol, MarketTime time, in BarSnapshot snapshot);
}
```

> 💡 **行情单一事实来源（SSOT）与双模式供给**：
> 系统彻底废弃歧义的标量 `GetMarketPrice`，统一以 48 字节非托管 `BarSnapshot` 作为行情的单一事实来源：
> 1. **矩阵回测模式**：若绑定了时序矩阵，按当前行号 `_currentIndex` 权威从 Open/High/Low/Close/Volume/Amount 矩阵常数时间提取六维真实切片；
> 2. **事件流/单测模式**：由 `OnMarketData` 入口在接收数据时单点归一化为 `BarSnapshot` 存入 `_snapshots`，杜绝散装字典与多入口数据分裂；
> 3. **缺失与停牌检测**：无数据时诚实返回 `BarSnapshot.Empty`，全字段为 `NaN`（`!bar.IsValid`），业务层显式熔断拦截，严禁任何伪造数据或默认返回 0.0。

---

## 三、标的桶化时间价格优先挂单簿 (OrderBook)

`OrderBook` 专职负责在途未成交订单的内存排序存储、检索、撮合切片与快速撤销，严格遵循集中撮合交易所的物理排队规则：

### 1. 标的桶化与双向优先队列 (Bids & Asks)
- 内部采用 `Dictionary<string, SymbolPendingQueue> _pendingBySymbol` 按标的代码进行一级分桶；
- 每个标的桶内部物理拆分为买盘（`Bids`）与卖盘（`Asks`）双向独立连续列表，消除双边遍历时的无谓过滤开销；
- 当某一标的新行情到达时，系统只需在 $O(1)$ 常数时间提取该标的的优先队列进行撮合比对，避免全量扫描全局订单。

### 2. 时间价格优先排队规则 (Price-Time Priority)
系统严格实现金融交易所标准的**价格优先、时间优先（Price-Time Priority）**法则：
- **买盘队列 (Bids)**：
  $$\text{市价单优先} \succ \text{委托限价降序 (高价先买)} \succ \text{挂单时间升序 (先来先到)} \succ \text{OrderId 升序}$$
- **卖盘队列 (Asks)**：
  $$\text{市价单优先} \succ \text{委托限价升序 (低价先卖)} \succ \text{挂单时间升序 (先来先到)} \succ \text{OrderId 升序}$$
- **二分查找保序插入（0 GC 堆分配）**：
  采用 `BinarySearch` + `List.Insert` 进行常数/对数时间插入保序，比较器采用无状态单例（`BidPriorityComparer.Instance` 与 `AskPriorityComparer.Instance`），列表基于连续数组内存布局，热路径零 GC 分配，CPU 缓存命中率极高。

### 3. 空桶自动物理清理 (内存防泄漏)
在全市场数千只标的的长周期回测中，大量标的挂单撮合成交或撤单后，挂单队列会变为空。
`OrderBook.Remove` 严格遵循资源回收原则：**当某一标的的买卖双盘挂单全部清空（`queue.Count == 0`）时，自动从主字典中物理注销该标的键**，杜绝遍历数万历史空桶导致的内存与 CPU 泄漏。

### 4. 常数时间撤单寻址 (_ordersById)
内部同步维护 `Dictionary<long, SimulatedOrder> _ordersById`：
- 用户调用 `CancelOrder(orderId)` 时，通过字典直接常数时间定位订单并从对应标的的买/卖队列中剔除（$O(1)$）；
- 避免了在长列表中逐一对比 `orderId` 的低效遍历。

---

## 四、纯计算撮合引擎 (MatchingEngine)

`MatchingEngine` 纯粹负责撮合判定与摩擦成本计算，不持有状态：

### 1. 无状态计算设计
方法接收订单请求 `OrderRequest`、基准价格 `marketPrice`、可选行情行 `IDataRow? bar` 及时间戳，返回紧凑的只读值类型 `MatchResult`（包含 `IsFilled`, `IsPending`, `IsRejected`, `FillPrice`, `FillQuantity`, `Commission`）。

### 2. Maker 被动触碰规则与跳空价格改善 (Price Improvement)
挂单簿中排队的限价单，在行情触及限价时撮合（基于 `BarSnapshot` 纯值类型切片，零 GC）：
- **买单成交条件**：$\text{TouchPrice} = \text{LowOr}(\text{ExecPrice}) \le P_{\text{limit}}$（日内最低价触及或跌破委托限价；若缺少 Low 则自适应平滑回退至基准执行价）
- **卖单成交条件**：$\text{TouchPrice} = \text{HighOr}(\text{ExecPrice}) \ge P_{\text{limit}}$（日内最高价触及或突破委托限价；若缺少 High 则自适应平滑回退至基准执行价）
- **开盘跳空价格改善 (Price Improvement)**：
  当新时间步开盘出现对挂单方更有利的跳空缺口时，Maker 限价挂单享受更优开盘价成交：
  $$\text{FillPrice}_{\text{Buy}} = \min(P_{\text{limit}}, P_{\text{open}}) \quad (\text{若 } P_{\text{open}} \text{ 有效且 } P_{\text{open}} < P_{\text{limit}})$$
  $$\text{FillPrice}_{\text{Sell}} = \max(P_{\text{limit}}, P_{\text{open}}) \quad (\text{若 } P_{\text{open}} \text{ 有效且 } P_{\text{open}} > P_{\text{limit}})$$
  若无开盘价或开盘跳空对挂单方不利（如买单高开但盘中触及限价），成交价严格锚定委托限价 $P_{\text{limit}}$，绝不突破限价劣质成交。

> 💡 **零 GC 48 字节通用切片 (`BarSnapshot`)**：
> 系统采用严格 48 字节非托管值类型 `BarSnapshot`（承载 `Open`, `High`, `Low`, `Close`, `Volume`, `Amount` 六大物理维度），完美容纳于单个 64 字节 CPU Cache Line。全字段原生支持缺失（内部以 `double.NaN` 表达），对称提供 `HasXxx` 探测与 `XxxOr(fallback)` 零开销内联回退，完美兼容次步开盘（Next Open）、VWAP 均价、收盘扫盘与日内限价等多种撮合时态。

### 3. Taker 即时成交规则与滑点截断保护
市价单或提交时即穿透基准价格的限价单立即成交：
- **固定滑点计算**：买入抬价，卖出折价：
  $$\text{FillPrice}_{\text{Buy}} = \text{BasePrice} \times (1 + \text{Slippage})$$
  $$\text{FillPrice}_{\text{Sell}} = \text{BasePrice} \times (1 - \text{Slippage})$$
- **限价截断保护 (Price Truncation)**：即便由于滑点计算导致理论价格劣于限价单限定价格，系统强制施加截断防御（买入成交价 $\le P_{\text{limit}}$，卖出成交价 $\ge P_{\text{limit}}$），绝不允许金融违规穿透。
- **公允市价与成交滑点价物理隔离**：即时成交价（`FillPrice`）仅反馈给策略与交易账本，公允市场价格字典（`_marketPrices`）仅维护客观基准价格（`BasePrice`），严禁将带滑点的执行价格反向写回全局公共行情字典，防止污染后续盯市基准。
- **时序矩阵按需 $O(1)$ 直通发现**：`SimulatedExchangeGateway` 在步进时绑定当前时序矩阵与时间步索引。当挂单簿为空或标的首次报单时，网关直接按需 $O(1)$ 常数时间直通时序矩阵获取当前收盘价作为公允市价，彻底杜绝挂单簿无排队单时公允市价缺失、进而导致限价单误拿自身委托价判定即时成交的系统性漏洞。

### 4. 流动性成交量硬约束与单 Bar 配额瓜分 (Volume Budget & Partial Fills)
为防止虚假高成交量穿透真实市场流动性，系统采用**单笔流动性硬约束**与**单 Bar 成交量配额瓜分**双层防御体系：

1. **单笔流动性硬约束 (`CheckLiquidity`)**：
   - 限制单笔报单不得超过当前 Bar 物理成交量的指定比例（`VolumeLimit`，如 5%）：
     $$\text{MaxAllowedQuantity} = \text{BarVolume} \times \text{VolumeLimit}$$
   - 若单笔报单超过此阈值，或者当前 Bar 物理成交量为 0（如一字无量跌停），订单直接被交易所判定为流动性不足拒单（`OrderStatus.Rejected`）；
   - 若配置了 `OnCheckVolume` Hook，则优先由自定义流动性委托进行判定；`VolumeLimit == 0.0` 时禁用此硬约束。
2. **单 Bar 成交量配额瓜分机制 (Volume Budget)**：
   - 在单一时间步（Bar）撮合时，全盘口可用撮合容量存在物理总上限：
     $$\text{RemainingBudget} = \text{BarVolume} \times \text{VolumeLimit}$$
   - 挂单簿中排队的多个限价单按照**时间价格优先队列**依次瓜分预算：
     - 若 `RemainingBudget >= order.RemainingQuantity`：订单**全额成交**（`Filled`），从挂单簿出簿，预算相应扣减；
     - 若 `RemainingBudget < order.RemainingQuantity` 且 `RemainingBudget > 0`：订单执行**部分成交**（`OrderStatus.PartialFilled`），成交数量等于剩余预算，未成交份额保留在挂单簿等待后续 Bar 续撮合，当前 Bar 预算归零；
     - 预算耗尽后，排在后面的其他低优先级订单保持在簿等待，绝不允许在单根 Bar 内累计突破流动性容量上限。

### 5. 涨跌停封板与停牌前置拦截 (CheckLimit & Suspended Filter)
- **停牌与无行情前置检查**：在回测时间步推进时，若标的当期收盘价无效（`NaN`、`<= 0` 或 `Infinity`）且未提供显式 Bar，`SendOrder` 在网关入口直接拒绝（`OrderStatus.Rejected`），不压入挂单簿；
- **涨跌停封板拦截**：开启 `CheckLimit` 时，若挂载了 `OnCheckLimit` 委托，在触及一字涨跌停板时拦截不可成交的反向或顺向订单并返回拒单。

### 6. 手续费与摩擦模型
手续费支持百分比费率与单笔最低消费门槛（如 A 股 5 元低吸保障）：
$$\text{Commission} = \max(\text{Turnover} \times \text{FeeRate}, \text{MinFee})$$
若配置了 `OnCalculateFee` 覆盖 Hook，则优先执行自定义手续费模型。

---

## 五、响应式纯行情驱动撮合 (OnMarketData)

### 1. 纯行情驱动撮合机制
交易所撮合引擎本质上是“订单-价格-容量”计算状态机，它只关心行情数据（Market Data：代码、价格、时间、Bar），与送股、派息、公告等离散业务事件（Event）完全正交解耦。

### 2. 显式时间戳强绑定 (防时间穿越)
```csharp
void OnMarketData(string symbol, MarketTime time, in BarSnapshot snapshot);
```
- **时间戳精准绑定**：撮合触发生成的成交报告 `ExecutionReport.Time` 严格锚定传入的 `time` 参数；
- **回测时间穿越防御**：绝不隐式回退使用系统当前时钟 `DateTime.UtcNow`，彻底消除回测报告与历史时间轴错位的隐患。

### 3. 离线时间步撮合与日切自动撤单 (Step & Day Order)
在离线回测中，引擎推进主时钟时调用 `gateway.Step(currentTime, marketData, currentIndex, phase)`：
1. **跨日挂单集中撤销**：检测到全局时间戳发生跨日时，网关在撮合前自动扫描挂单簿，集中撤销所有当日有效（`TimeInForce.Day`）的未成交排队挂单，并在锁外统一派发 `OrderStatus.Canceled` 回报，驱动 `Broker` 自动全额解冻预占资金与持仓；撤单前有效（`TimeInForce.GoodTilCanceled`）挂单则继续在挂单簿排队；
2. **快速排队检查**：若挂单簿总数 `OrderBook.Count == 0`，锁外派发已撤销回报后立即常数时间安全退出；
3. **活跃标的索引遍历**：仅遍历挂单簿中当前存在排队订单的标的集合 `OrderBook.ActiveSymbols`；
4. **时序矩阵批量撮合**：从时序注册表提取当日行情矩阵，批量触发被动撮合。

### 4. 单一事实来源权威撮合内核 (MatchSymbolOrders)
`Step` 离线批量推进与 `OnMarketData` 实时增量灌入，统一收敛至私有纯函数 `MatchSymbolOrders` 进行权威撮合，100% 杜绝多入口代码重复与逻辑分叉。

---

## 六、锁外异步回报派发 (DispatchReports & Open Calls)

持有对象锁调用外部订阅事件是典型的并发死锁根源。`SimulatedExchangeGateway` 遵循 Open Calls 原则：
1. **锁内权威转移**：在内部锁保护下完成挂单出簿、价格判定与成交模型计算，将生成的 `ExecutionReport` 和被拒绝订单列表暂存于本地列表；
2. **锁外开放派发 (`DispatchReports`)**：彻底释放内部锁后，正序派发 `OnOrderStateChanged` 与 `OnExecution` 事件，从架构层面根绝重入死锁。

---

## 七、对称 Hook 矩阵与配置参数

`BrokerConfig` 中与撮合交易所相关的参数与覆盖 Hook 如下：

| 配置项 | 覆盖 Hook 委托 | 职责说明 | 默认基准值 |
| :--- | :--- | :--- | :---: |
| `Slippage` | `OnCalculateSlippage(request, price, bar)`| 动态冲击成本与滑点模型 | `0.001` (千一) |
| `FeeRate` + `MinFee` | `OnCalculateFee(request, turnover)` | 动态手续费/印花税计算 | `0.001` / `0.0` |
| `VolumeLimit` | `OnCheckVolume(request, bar)` | 成交量流动性硬约束判定 | `0.05` (5%) |
| `CheckLimit` | `OnCheckLimit(request, bar)` | 涨跌停封板与停牌拦截判定 | `false` |
| `DefaultTimeInForce` | - | 默认订单时效性（发单未显式指定时采用），对齐国内现货期货市场规则 | `TimeInForce.Day` |

---

## 八、实盘柜台适配与平滑替换 (CTP / 币安)

客户端 `Broker` 仅面向瘦接口 `IExchangeGateway` 编程：
- **回测环境**：装配 `SimulatedExchangeGateway`，由引擎主时钟驱动模拟撮合；
- **生产实盘环境**：装配真实柜台网关（如 `CTPGateway` 或 `BinanceGateway`），将订单请求序列化为柜台 API 报文发送，接收网络推送并触发 `OnExecution` 与 `OnOrderStateChanged`；
- 策略业务代码与资产记账模型 100% 无需修改，实现零成本实盘切换。
