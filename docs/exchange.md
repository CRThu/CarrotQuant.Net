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
- [三、标的桶化 FIFO 限价挂单簿 (OrderBook)](#三标的桶化-fifo-限价挂单簿-orderbook)
  - [1. 标的桶化与 O(1) 寻址](#1-标的桶化与-o1-寻址)
  - [2. FIFO 时间优先排队队列](#2-fifo-时间优先排队队列)
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
  - [1. 纯行情驱动第一性原理](#1-纯行情驱动第一性原理)
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
    event Action<string, OrderStatus> OnOrderStateChanged;
}
```

### 2. 仿真驱动契约 (ISimulatedExchange)
`CarrotBacktesting.NET/Abstraction/Engine/ISimulatedExchange.cs` 为模拟回测环境下的交易所仿真网关提供时钟推进与行情灌入能力：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface ISimulatedExchange : IExchangeGateway
{
    // 离线时间步推进与矩阵撮合
    void Step(DateTime currentTime, ITimeseriesRegistry timeseries, int currentIndex);

    // 纯行情数据驱动撮合（明确绑定标的、价格、时间戳与可选当前 Bar）
    void OnMarketData(string symbol, double price, DateTime time, IDataRow? bar = null);

    // 显式校准交易所当前物理时间
    void SetCurrentTime(DateTime currentTime);

    // 查询最新已记录市场价格
    double GetMarketPrice(string symbol);
}
```

---

## 三、标的桶化 FIFO 限价挂单簿 (OrderBook)

`OrderBook` 负责维护回测生命周期内排队的限价订单：

### 1. 标的桶化与 O(1) 寻址
- 内部采用 `Dictionary<string, LinkedList<SimulatedOrder>> _buckets` 按标的代码进行一级分桶；
- 当某一标的新行情到达时，系统只需在 $O(1)$ 常数时间提取该标的的挂单链表进行撮合比对，完全避免了全量扫描全局订单的低效做法。

### 2. FIFO 时间优先排队队列
- 桶内采用双向链表 `LinkedList<SimulatedOrder>` 存储；
- 遵循时间优先原则：新挂单追加至链表尾部（`AddLast`），撮合时从链表头部正序遍历（FIFO），真实反映交易所挂单排队次序。

### 3. 空桶自动物理清理 (内存防泄漏)
在全市场数千只标的的长周期分钟级回测中，大量标的挂单撮合成交或撤单后，挂单队列会变为空。
`OrderBook.Remove` 严格遵循资源回收原则：**当某一标的的挂单链表计数归零时，自动从主字典中物理注销该标的键**，杜绝遍历数万历史空桶导致的内存与 CPU 泄漏。

### 4. 常数时间撤单寻址 (_ordersById)
内部同步维护 `Dictionary<string, LinkedListNode<SimulatedOrder>> _ordersById`：
- 用户调用 `CancelOrder(orderId)` 时，通过字典直接定位链表节点并常数时间移除（$O(1)$）；
- 避免了在线性链表中逐一对比 `orderId` 的 $O(N)$ 遍历开销。

---

## 四、纯计算撮合引擎 (MatchingEngine)

`MatchingEngine` 纯粹负责撮合判定与摩擦成本计算，不持有状态：

### 1. 无状态计算设计
方法接收订单请求 `OrderRequest`、基准价格 `marketPrice`、可选行情行 `IDataRow? bar` 及时间戳，返回紧凑的只读值类型 `MatchResult`（包含 `IsFilled`, `IsPending`, `IsRejected`, `FillPrice`, `FillQuantity`, `Commission`）。

### 2. Maker 被动触碰规则与委托限价保护
挂单簿中排队的限价单，在行情触及限价时撮合：
- **买单成交条件**：$\text{MarketPrice} \le P_{\text{limit}}$
- **卖单成交条件**：$\text{MarketPrice} \ge P_{\text{limit}}$
- **委托限价保护契约**：Maker 挂单成交时，成交价严格遵循委托限价（$\text{FillPrice} = P_{\text{limit}}$），绝不突破限价劣质成交。

### 3. Taker 即时成交规则与滑点截断保护
市价单或提交时即穿透基准价格的限价单立即成交：
- **固定滑点计算**：买入抬价，卖出折价：
  $$\text{FillPrice}_{\text{Buy}} = \text{BasePrice} \times (1 + \text{Slippage})$$
  $$\text{FillPrice}_{\text{Sell}} = \text{BasePrice} \times (1 - \text{Slippage})$$
- **限价截断保护 (Price Truncation)**：即便由于滑点计算导致理论价格劣于限价单限定价格，系统强制施加截断防御（买入成交价 $\le P_{\text{limit}}$，卖出成交价 $\ge P_{\text{limit}}$），绝不允许金融违规穿透。

### 4. 流动性成交量硬约束 (VolumeLimit)
为杜绝小盘股撮合成交量超过真实市场容量的作弊行为，配置项 `VolumeLimit` 严格限制单笔委托不得超过当前 Bar 物理成交量的指定比例（如 5%）：
$$\text{MaxAllowedQuantity} = \text{BarVolume} \times \text{VolumeLimit}$$
超出比例的订单直接被拒单（Rejected）。若 `VolumeLimit == 0.0` 则代表完全禁用流动性约束（无限制放行）。

### 5. 涨跌停封板与停牌拦截 (CheckLimit)
开启 `CheckLimit` 时，若挂载了 `OnCheckLimit` 委托，在触及一字涨跌停板或停牌时，拦截不可成交的反向或顺向订单并返回拒单。

### 6. 手续费与摩擦模型
手续费支持百分比费率与单笔最低消费门槛（如 A 股 5 元低吸保障）：
$$\text{Commission} = \max(\text{Turnover} \times \text{FeeRate}, \text{MinFee})$$
若配置了 `OnCalculateFee` 覆盖 Hook，则优先执行自定义手续费模型。

---

## 五、响应式纯行情驱动撮合 (OnMarketData)

### 1. 纯行情驱动第一性原理
交易所撮合引擎本质上是“订单-价格-容量”计算状态机，它只关心行情数据（Market Data：代码、价格、时间、Bar），与送股、派息、公告等离散业务事件（Event）完全正交解耦。

### 2. 显式时间戳强绑定 (防时间穿越)
```csharp
void OnMarketData(string symbol, double price, DateTime time, IDataRow? bar = null);
```
- **时间戳精准绑定**：撮合触发生成的成交报告 `ExecutionReport.Time` 严格锚定传入的 `time` 参数；
- **回测时间穿越防御**：绝不隐式回退使用系统当前时钟 `DateTime.UtcNow`，彻底消除回测报告与历史时间轴错位的隐患。

### 3. 离线时间步撮合 (Step)
在离线回测中，引擎推进主时钟时调用 `gateway.Step(currentTime, timeseries, currentIndex)`：
1. 快速检查挂单簿：若挂单簿总数 `OrderBook.Count == 0`，立即常数时间返回；
2. 仅遍历挂单簿中当前存在排队订单的标的集合 `OrderBook.ActiveSymbols`；
3. 从时序注册表提取当日收盘价矩阵 `closeMatrix`，批量触发被动撮合。

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

---

## 八、实盘柜台适配与平滑替换 (CTP / 币安)

客户端 `Broker` 仅面向瘦接口 `IExchangeGateway` 编程：
- **回测环境**：装配 `SimulatedExchangeGateway`，由引擎主时钟驱动模拟撮合；
- **生产实盘环境**：装配真实柜台网关（如 `CTPGateway` 或 `BinanceGateway`），将订单请求序列化为柜台 API 报文发送，接收网络推送并触发 `OnExecution` 与 `OnOrderStateChanged`；
- 策略业务代码与资产记账模型 100% 无需修改，实现零成本实盘切换。
