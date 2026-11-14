# 虚拟经纪商与撮合执行体系 (SimulatedBroker & Execution System)

本文档面向量化交易架构师与策略开发者，系统介绍 CarrotQuant.NET 中虚拟经纪商（`SimulatedBroker`）的核心契约（`IBroker`）、资产持仓状态机、多时区自然日清算与撮合规则模型。

---

## 目录
- [一、核心契约与职责定义 (IBroker)](#一核心契约与职责定义-ibroker)
- [二、账户模式与持仓状态机](#二账户模式与持仓状态机)
- [三、两阶段挂单冻结账本与撮合体系 (Two-Phase Order Lifecycle)](#三两阶段挂单冻结账本与撮合体系-two-phase-order-lifecycle)
- [四、撮合规则与摩擦成本模型](#四撮合规则与摩擦成本模型)
- [五、多时区与本地自然日清算 (TimeZone & Settlement)](#五多时区与本地自然日清算-timezone--settlement)
- [六、并发线程安全与开放调用 (Thread Safety & Open Calls)](#六并发线程安全与开放调用-thread-safety--open-calls)
- [七、规则 Hook 与依赖倒置](#七规则-hook-与依赖倒置)
- [八、配置规范与使用示例](#八配置规范与使用示例)

---

## 一、核心契约与职责定义 (IBroker)

在 CarrotQuant.NET 中，`IBroker` 是执行层与策略层交互的权威门面，统一抽象模拟回测环境（`SimulatedBroker`）与实盘网关（`LivingBroker`）：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface IBroker
{
    // 资金与持仓查询
    double GetCash();
    double GetTotalEquity();
    decimal GetPosition(string symbol);
    decimal GetAvailablePosition(string symbol);
    IReadOnlyDictionary<string, decimal> GetPositions();

    // 订单生命周期管理
    string SubmitOrder(OrderRequest request);
    void CancelOrder(string orderId);
    OrderStatus GetOrderStatus(string orderId);

    // 物理时钟单向推进
    void Step(DateTime currentTime);

    // 实盘状态对账同步
    void SyncState(double cash, IReadOnlyDictionary<string, decimal> positions);

    // 异步执行事件
    event Action<string, OrderStatus> OnOrderStatusChanged;
    event Action<ExecutionReport> OnTrade;
    event Action<PositionEvent>? OnPositionChanged;
}
```

### 单一物理入口原则 (Step Only)
外部引擎无需关心具体经纪商何时日切，仅需通过单一入口 `Step(DateTime currentTime)` 推动世界物理时间流逝。经纪商自治检测是否跨越交易所本地换日点并执行 T+N 持仓划转，彻底消除了冗余的外部 `RollDate` 调用。

---

## 二、账户模式与持仓状态机

`SimulatedBroker` 采用单一内核自适应支持现货与保证金两套经典业务范式：

```mermaid
stateDiagram-v2
    [*] --> Submitted: SubmitOrder()
    Submitted --> Rejected: 资金不足 / 卖空受限 / 流动性限制 / 涨跌停
    Submitted --> Filled: 撮合通过
    Filled --> [*]
```

### 1. 现货全额模式 (`Multiplier == 1.0 && MarginRate == 1.0 && !AllowShort`)
适用于 A 股普通股票、ETF 等标的：
- **买入扣款**：全额冻结并扣除成交金额与手续费：
  $$\text{Cash}_{\text{new}} = \text{Cash}_{\text{old}} - (\text{成交金额} + \text{手续费})$$
- **T+N 持仓锁定**：买入后总持仓 `TotalQuantity` 立即增加，但可用持仓 `AvailableQuantity` 根据结算天数 `SettleDays`（如 A 股 $N=1$）进入冻结队列 `PendingLots`，当日无法卖出；
- **卖出回款**：校验 `Quantity <= AvailableQuantity` 后扣减持仓并全额释放可用资金：
  $$\text{Cash}_{\text{new}} = \text{Cash}_{\text{old}} + (\text{成交金额} - \text{手续费})$$

### 2. 期货 / 衍生品保证金模式 (`MarginRate < 1.0 || Multiplier != 1.0 || AllowShort`)
适用于股指期货、商品期货、期权及外盘双向合约：
- **双向持仓**：多头持仓记为正数，空头持仓记为负数；
- **开仓占用保证金**：
  $$\text{MarginOccupied} = |\text{Quantity}| \times \text{Price} \times \text{Multiplier} \times \text{MarginRate}$$
  扣减账户可用现金：$\text{Cash}_{\text{new}} = \text{Cash}_{\text{old}} - (\text{MarginOccupied} + \text{手续费})$；
- **平仓盈亏释放**：
  $$\text{RealizedPnL} = (\text{FillPrice} - \text{AverageCost}) \times \text{CloseQuantity} \times \text{Multiplier} \quad (\text{多头平仓})$$
  $$\text{RealizedPnL} = (\text{AverageCost} - \text{FillPrice}) \times \text{CloseQuantity} \times \text{Multiplier} \quad (\text{空头平仓})$$
  释放保证金并计入已实现盈亏：$\text{Cash}_{\text{new}} = \text{Cash}_{\text{old}} + (\text{MarginReleased} + \text{RealizedPnL})$。

---

## 三、两阶段挂单冻结账本与撮合体系 (Two-Phase Order Lifecycle)

为了支持真实的限价挂单、防双花（Double Spending）与防超卖（Over-selling），`SimulatedBroker` 内置了两阶段提交（Prepare-Commit-Abort）挂单账本：

```mermaid
stateDiagram-v2
    [*] --> Submitted: SubmitOrder() (限价未触及市价)
    Submitted --> Canceled: CancelOrder() / SyncState() (释放冻结资金与持仓)
    Submitted --> Filled: 行情触及限价 (自动撮合结算)
    Filled --> [*]
    Canceled --> [*]
```

### 1. 两阶段冻结账本 (Prepare Phase)
当限价买单价格低于市价，或限价卖单价格高于市价时，订单作为未成交挂单排队：
- **资金预冻结 (`_frozenCash`)**：按委托限价 $P_{\text{limit}}$ 精确计算所需开仓保证金与佣金预先扣除，可用现金 `GetCash()` 即时扣减 $\text{FrozenCash}$，杜绝多单同时提交导致的资金双花透支；
- **持仓预冻结 (`pos.FrozenQuantity`)**：卖单所需平仓数量直接从 `AvailableQuantity` 扣减并划入 `FrozenQuantity`，杜绝同一批持仓被多个挂单重复锁定的超卖隐患；
- **反手挂单复合冻结**：若当前持有多头且挂出更大数量的卖单（反手），系统原子化同时冻结多头可用持仓与反手开空所需保证金。

### 2. 撤单全额解冻 (Abort Phase)
调用 `CancelOrder(orderId)` 或在实盘对账 `SyncState` 时：
- `_frozenCash` 全额反转释放，资金原路归还可用现金；
- `pos.FrozenQuantity` 全额复位至 `pos.AvailableQuantity`；
- 订单状态流转为 `OrderStatus.Canceled` 并触发通知。

### 3. FIFO 时间优先撮合 (Commit Phase)
- **标的挂单桶化 (`_pendingOrdersBySymbol`)**：挂单按标的精准桶化索引，寻址复杂度由 $O(N)$ 降至 $O(1)$，空桶自动即时销毁；
- **正序时间优先 (FIFO)**：新行情推送或 `Step` 推进时，正序遍历挂单队列，严格遵循交易所时间优先原则；
- **Maker 限价保护契约**：挂单被动成交时，成交价格严格受委托限价保护（买单成交价 $\le P_{\text{limit}}$，卖单成交价 $\ge P_{\text{limit}}$），绝不突破限价劣质成交。

---

## 四、撮合规则与摩擦成本模型

### 1. 成交价滑点模型 (Slippage) 与 Taker 保护
默认采用固定比例滑点，买入抬价，卖出折价：
$$\text{FillPrice}_{\text{Buy}} = \text{BasePrice} \times (1 + \text{Slippage})$$
$$\text{FillPrice}_{\text{Sell}} = \text{BasePrice} \times (1 - \text{Slippage})$$

> [!IMPORTANT]
> **Taker 激进限价单限价截断保护**：若提交的是限价单且基准市价穿透限价立即成交，即便由于滑点计算导致理论价格劣于限价，系统强制施加限价截断防御（买入成交价 $\le P_{\text{limit}}$，卖出成交价 $\ge P_{\text{limit}}$），绝不允许金融违规穿透。

### 2. 交易手续费模型 (Fee & MinFee)
手续费支持百分比费率与单笔最低消费门槛（如 A 股 5 元低吸保障）：
$$\text{Commission} = \max(\text{Turnover} \times \text{FeeRate}, \text{MinFee})$$

### 3. 流动性硬约束 (VolumeLimit)
为杜绝小盘股撮合成交量超过真实市场容量的作弊行为，配置项 `VolumeLimit` 严格限制单笔委托不得超过当前 Bar 物理成交量的指定比例（如 5%）：
$$\text{MaxAllowedQuantity} = \text{BarVolume} \times \text{VolumeLimit}$$
超出比例的订单直接被拒单（Rejected）。若 `VolumeLimit == 0.0` 则代表完全禁用流动性约束（无限制放行）。

### 4. 涨跌停封板拦截 (CheckLimit)
开启 `CheckLimit` 时，若挂载了 `OnCheckLimit` 委托，在触及涨跌停板时拦截不可成交的反向或顺向订单。

---

## 五、多时区与本地自然日清算 (TimeZone & Settlement)

CarrotQuant.NET 原生支持多时区数据与跨市场多标的回测，采用“数据源端一次性归一化至 UTC、经纪商端在本地时区日切”的设计：

```mermaid
flowchart LR
    subgraph DataLayer ["1. 数据加载期 (Cold Path)"]
        RawCSV["原始数据文件<br>(如 CST / EST 本地时间)"] -->|配置 TimeZone 映射| UTCGrid["全局 UTC 时间轴<br>(TradeDates)"]
    end

    subgraph HotLoop ["2. 回测主时钟推进 (Hot Path)"]
        UTCGrid -->|单调时间戳推进| BrokerStep["Broker.Step(currentTimeUtc)"]
    end

    subgraph SettleLogic ["3. 交易所本地清算 (Zero GC)"]
        BrokerStep --> Check{"currentTimeUtc >= _nextRollTimeUtc?"}
        Check -->|Yes| DoRoll["本地 00:00:00 换日结算<br>T+N 可用持仓划转<br>预计算下一换日点"]
        Check -->|No| FastPass["直接放行 (常数时间比对)"]
    end
```

### 1. 规范约定
- **时区命名标准**：统一采用标准 **IANA 时区标识**（如 `"Asia/Shanghai"`、`"America/New_York"`、`"UTC"`），跨平台自适应解析；
- **日切时间点 (Cutoff Time)**：统一采用交易所所在地的**本地自然日 `00:00:00`** 换日；
- **时态安全性**：输入时间戳若为 `DateTimeKind.Unspecified`，防御性视为 UTC 基准时间，杜绝系统本地时区隐式污染。

### 2. 预计算换日阈值机制 (NextRollTimeUtc)
为了避免在主时钟循环（热路径）中高频调用开销昂贵的时区转换 API（如 `ConvertTime`）：
1. 在初始化或每次日切完成时，Broker 预先计算出交易所本地次日 `00:00:00` 对应的 UTC 绝对时刻：
   $$\text{NextRollTimeUtc} = \text{ConvertTimeToUtc}(\text{ExchangeDate} + 1\text{天 } 00:00:00, \text{ExchangeTimeZone})$$
2. 在 `Step(currentTime)` 中，仅执行一次时间戳数值比对：
   $$\text{currentTime} \ge \text{NextRollTimeUtc}$$
   若未达到，直接放行，零额外开销；达到则触发本地清算并更新下一个阈值。

### 3. T+N 可用持仓解锁 (UnlockPendingLots)
清算触发时，倒序遍历持仓冻结队列 `PendingLots`：
$$\text{ExchangeDate} \ge \text{lot.AvailableDate} \implies \text{AvailableQuantity} += \text{lot.Quantity}$$
移除已到期的冻结份额，使次日首根 Bar 交易即可使用解锁持仓。

---

## 六、并发线程安全与开放调用 (Thread Safety & Open Calls)

量化回测与实盘网关面对多策略并行计算、异步行情推送与事件监听，`SimulatedBroker` 在并发架构上贯彻两大核心保障：

### 1. 128 位 `decimal` 撕裂读防御 (Torn Read Prevention)
在 64 位 CPU 架构中，128 位的 `decimal` 持仓数据需跨多个 CPU 寄存器读写。`SimulatedBroker` 对 `GetPosition`、`GetAvailablePosition`、`GetOrderStatus` 等所有外部读取契约统一施加内部锁保护，杜绝多线程并发读写时读取到被写一半的脏数值。

### 2. 锁外零 GC 开放调用 (Open Calls)
持有对象锁时调用外部用户订阅的委托（如 `OnOrderStatusChanged`、`OnTrade`、`OnPositionChanged`）属于典型的死锁反模式（监听者可能在回调内重入调用 `broker.GetCash()` 或持有外部业务锁）。
- **锁内权威转移**：所有账户状态机跃迁、挂单冻结账本维护均在锁内原子完成；
- **锁外开放派发**：单笔订单利用栈上值类型变量暂存事件，批量撮合借用 `ArrayPool<FillDispatch>` 缓冲池，在彻底退出锁后进行安全开放派发，既消除了死锁与重入风险，又实现了 **100% 零 GC 堆内存分配**。

---

## 七、规则 Hook 与依赖倒置

为满足复杂的实盘定制需求（如动态保证金率、浮动阶梯手续费、期货品种不同乘数），`BrokerConfig` 提供了基于强类型委托的拦截器：

```csharp
public class BrokerConfig
{
    // 动态合约乘数 Hook (仅依赖标的维表 IFieldRegistry)
    public Func<string, IFieldRegistry?, double>? OnGetMultiplier { get; set; }

    // 动态保证金率 Hook (仅依赖标的维表 IFieldRegistry)
    public Func<string, IFieldRegistry?, double>? OnGetMarginRate { get; set; }

    // 动态结算天数 Hook
    public Func<string, int>? OnGetSettleDays { get; set; }

    // 动态自定义手续费计算 Hook
    public Func<OrderRequest, double, double>? OnCalculateFee { get; set; }

    // 动态自定义滑点计算 Hook
    public Func<OrderRequest, double, IDataRow, double>? OnCalculateSlippage { get; set; }

    // 动态流动性成交量约束 Hook
    public Func<OrderRequest, IDataRow, bool>? OnCheckVolume { get; set; }

    // 动态涨跌停拦截 Hook
    public Func<OrderRequest, IDataRow, bool>? OnCheckLimit { get; set; }
}
```

> [!NOTE]
> 规则 Hook 仅向外暴露标的维表抽象 `IFieldRegistry`，坚决不反向引入全局上下文 `IEngineContext`，保持严格的单向依赖拓扑。

---

## 八、配置规范与使用示例

### 1. Code-First 构造配置
```csharp
// A 股现货回测配置：T+1 结算、千一滑点、万三佣金（5元门槛）、不允许卖空、北京时区
var aStockBroker = new SimulatedBroker(new BrokerConfig
{
    InitialCash = 1_000_000.0,
    SettleDays = 1,
    AllowShort = false,
    FeeRate = 0.0003,
    MinFee = 5.0,
    Slippage = 0.001,
    TimeZoneId = "Asia/Shanghai"
});

// 期货保证金回测配置：T+0 结算、万一手续费、允许双向开平仓
var futureBroker = new SimulatedBroker(new BrokerConfig
{
    InitialCash = 1_000_000.0,
    SettleDays = 0,
    AllowShort = true,
    FeeRate = 0.0001,
    MinFee = 0.0,
    MarginRate = 0.1,
    Multiplier = 10.0
});
```

### 2. YAML 声明式配置
```yaml
broker:
  initial_cash: 1000000.0
  settle_days: 1
  allow_short: false
  fee_rate: 0.0003
  min_fee: 5.0
  slippage: 0.001
  volume_limit: 0.05
  check_limit: true
  time_zone_id: "Asia/Shanghai"
```
