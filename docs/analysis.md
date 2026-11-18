# 绩效分析与报表子系统 (Analysis & Reporting System)

本文档介绍 CarrotQuant.NET 绩效分析与报表子系统（`CarrotBacktesting.NET/Analysis/` 与 `CarrotBacktesting.NET/Abstraction/Analysis/`）的架构设计、两阶段解耦机制、四层量化统计模型、独立 Alpha 信号前向评估、垂直切片单文件组织以及报表导出体系。

---

## 目录
- [一、架构总览与两阶段解耦设计](#一架构总览与两阶段解耦设计)
- [二、事实数据集单一事实来源 (TradingResult)](#二事实数据集单一事实来源-tradingresult)
- [三、四层量化统计与独立 Alpha 评估体系](#三四层量化统计与独立-alpha-评估体系)
  - [1. 第 1 层：组合净值与宏观风险 (PortfolioStatistics)](#1-第-1-层组合净值与宏观风险-portfoliostatistics)
  - [2. 第 2 层：资本利用与资金敞口 (CapitalStatistics)](#2-第-2-层资本利用与资金敞口-capitalstatistics)
  - [3. 第 3 层：交易行为与执行归因 (TradeStatistics)](#3-第-3-层交易行为与执行归因-tradestatistics)
  - [4. 第 4 层：现实交易成本与损耗 (CostStatistics)](#4-第-4-层现实交易成本与损耗-coststatistics)
  - [5. 独立 Alpha 信号质量评估轨 (SignalStatistics)](#5-独立-alpha-信号质量评估轨-signalstatistics)
- [四、垂直切片单文件组织与职责归位 (Vertical Slice Architecture)](#四垂直切片单文件组织与职责归位-vertical-slice-architecture)
- [五、统一单路径分析管线 (Single-Path Analyzer Pipeline)](#五统一单路径分析管线-single-path-analyzer-pipeline)
- [六、报表呈现与导出集成 (Reporting & Exporter Integration)](#六报表呈现与导出集成-reporting--exporter-integration)
- [七、历史版本对比与演进 (V3 vs Lean vs CarrotQuant.NET)](#七历史版本对比与演进-v3-vs-lean-vs-carrotquantnet)

---

## 一、架构总览与两阶段解耦设计

在量化交易系统中，高频回测与实盘交易对运行期热路径（Hot Path）的吞吐与延迟有明确约束。CarrotQuant.NET 采用**两阶段解耦**架构：

```mermaid
graph TD
    subgraph Phase1 [第一阶段：运行期流水记账 (Hot Path - 低开销流水)]
        Engine[BacktestingEngine / LivingEngine]
        Recorder[TradingRecorder<br>纯事实流水沉淀 / ICheckpointable]
        Engine -->|RecordStep / RecordSettlement / RecordClosedTrade| Recorder
    end

    Recorder -->|GetResult| Facts[TradingResult<br>不可变单一事实来源 SSOT]

    subgraph Phase2 [第二阶段：收尾期纯函数式后处理 (Post-Processing)]
        Facts --> EngineAnalysis[AnalysisEngine 统一分析引擎]
        Timeseries[ITimeseriesRegistry 只读行情矩阵 (可选)] --> EngineAnalysis

        subgraph Pipeline [一路径分析管线 (Single-Path Pipeline)]
            EngineAnalysis --> A1[PortfolioAnalyzer]
            EngineAnalysis --> A2[CapitalAnalyzer]
            EngineAnalysis --> A3[TradeAnalyzer]
            EngineAnalysis --> A4[CostAnalyzer]
            EngineAnalysis --> A5[SignalAnalyzer]
            EngineAnalysis --> A6[CustomAnalyzer...]

            A1 --> L1[PortfolioStatistics : IAnalysisResult]
            A2 --> L2[CapitalStatistics : IAnalysisResult]
            A3 --> L3[TradeStatistics : IAnalysisResult]
            A4 --> L4[CostStatistics : IAnalysisResult]
            A5 --> L5[SignalStatistics : IAnalysisResult]
            A6 --> L6[CustomResult : IAnalysisResult]
        end

        L1 & L2 & L3 & L4 & L5 & L6 --> Report[AnalysisReport 综合分析报告]
        Report --> Console[ConsoleExporter 终端彩色仪表盘 / summary.html 导出]
        Report --> Exporter[Excel / HTML Tear Sheet 报表]
    end
```

### 核心解耦准则
1. **运行期绝对零高阶指标计算**：回测循环与实盘事件驱动过程中，严禁在线计算夏普、回撤、波动率等高开销统计量，仅由 `TradingRecorder` 记录原始账目事实。
2. **收尾期纯函数式后处理**：回测终止后，将权威的 `TradingResult` 输入分析引擎，走无状态、零额外堆分配的纯数学运算管线。

---

## 二、事实数据集单一事实来源 (TradingResult)

`TradingResult`（位于 `CarrotBacktesting.NET.Abstraction.Analysis`）作为全系统唯一的交易事实载体，完整聚合了回测或实盘产生的一切基础数据：

| 属性 | 内部元素类型 | 核心内容 | 业务用途 |
| :--- | :--- | :--- | :--- |
| **`EquityCurve`** | `EquityPoint` (struct) | 时间戳、总动态权益、可用现金、冻结现金、保证金占用、持仓总市值、浮动盈亏 | 组合净值曲线、最大回撤、波动率计算基石 |
| **`DailySnapshots`** | `DailySnapshot` (record) | 收盘时间戳、全量资产明细、标的持仓列表（含成本价、市价、市值、浮盈、保证金占用、资产权重） | 自然月度收益矩阵、跨日资产敞口、实盘对账 |
| **`ClosedTrades`** | `Trade` (record) | 标的代码、交易方向 (Long/Short)、开平仓时间与价格、配对数量、合约乘数、净已实现盈亏、收益率、手续费、滑点损耗、持仓步数、开平标签 | 交易胜率、盈亏比、盈利因子、MFE 交易效率、卖点反思 |
| **`Settlements`** | `SettlementRecord` (record) | 券商交割发生时间、标的、订单号、买卖方向、成交价格与数量、成交金额 (Turnover)、佣金税费、已实现盈亏、交割后可用现金余额、策略归因标签 | 资金双向换手率、真实佣金损耗 |
| **`OpenPositions`** | `PositionSnapshot` (record) | 期末未平仓标的持仓明细（含盯市盈亏与占用保证金） | 终局持仓结构审视 |
| **`Signals`** | `SignalRecord` (record) | 信号时间戳、标的代码、信号数值、信号组别 (SignalGroup)、触发原因 | Alpha 信号前向收益分布、最优持有周期评估 |

---

## 三、四层量化统计与独立 Alpha 评估体系

CarrotQuant.NET 对齐 QuantConnect Lean 与现代资管标准，将所有分析结果归纳为**四层量化统计模型**与**一层独立 Alpha 评估轨**。每个统计模型均明确界定**数学公式**、**边界约束**与**金融业务用途**：

### 1. 第 1 层：组合净值与宏观风险 (PortfolioStatistics)
面向资管与基金层面的宏观资金收益与抗风险能力评估。

#### 1.1 累计总收益率 (TotalReturn)
- **数学公式**：
  $$R_{total} = \frac{E_{end} - E_{start}}{E_{start}}$$
- **边界约束**：当期初资产 $E_{start} \le 0$ 时，总收益率定义为 $0.0$。
- **业务用途**：衡量回测或实盘全生命周期的资本总增值幅度。

#### 1.2 年化复合收益率 (AnnualizedReturn / CAGR)
- **数学公式**：
  $$CAGR = \left(\frac{E_{end}}{E_{start}}\right)^{\frac{365.25}{T_{days}}} - 1$$
- **边界约束**：
  - 当回测跨度不足 1 个交易日（$T_{days} < \frac{365.25}{K}$，其中 $K$ 为年交易日数）时，设置最小时间跨度底线 $Years_{min} = \frac{1}{K}$，防止分母趋零引发浮点指数溢出；
  - 当期末资产归零或穿仓（$E_{end} \le 0$）时，CAGR 判定为 $-100\%$。
- **业务用途**：消除回测区间长度差异，提供跨策略、跨周期的标准化复合增长率基准。

#### 1.3 年化波动率 (AnnualizedVolatility)
- **数学公式**：
  $$\sigma_{ann} = \sqrt{\frac{1}{N-1}\sum_{t=1}^N (R_t - \bar{R})^2} \times \sqrt{K}$$
  其中 $R_t = \frac{E_t - E_{t-1}}{E_{t-1}}$ 为日度周期收益率，$K$ 为年交易日数（默认 252）。
- **业务用途**：衡量组合净值围绕期望收益率的离散离差程度，反映资产价格总波动风险。

#### 1.4 年化下行半方差 (DownsideVolatility)
- **数学公式**：
  $$\sigma_{down} = \sqrt{\frac{1}{N}\sum_{t=1}^N \min\left(0, R_t - \frac{R_f}{K}\right)^2} \times \sqrt{K}$$
  其中 $R_f$ 为年化无风险利率。
- **业务用途**：区分“良性波动”（上涨）与“恶性波动”（亏损），仅对低于无风险日基准的下行亏损施加惩罚，是索提诺比率的核心风险分母。

#### 1.5 最大回撤与四维生命周期 (MaxDrawdown, PeakTime, TroughTime, RecoveryTime, DurationSteps)
- **数学公式**：
  $$DD_t = \frac{\max_{0 \le s \le t}(E_s) - E_t}{\max_{0 \le s \le t}(E_s)}, \quad MaxDD = \max_{0 \le t \le T} (DD_t)$$
- **生命周期四维坐标**：
  - 峰值时间点 ($t_{peak}$)：产生最大回撤的历史净值最高点时刻；
  - 谷底时间点 ($t_{trough}$)：回撤达到最大幅度的资产最低点时刻；
  - 修复时间点 ($t_{recovery}$)：资产后续首次超越前期历史峰值 $E(t_{peak})$ 的时刻；若未修复则为 `null`；
  - 持续步数 (`DurationSteps`)：若已修复，为 $t_{recovery} - t_{peak}$；若未修复，为 $t_{now} - t_{peak}$。资产单调递增无回撤时持续步数严格归零。
- **业务用途**：刻画策略历史最极端的资本缩水风险、承压持仓时长与净值爬坡修复能力，是实盘承载力评估的硬性指标。

#### 1.6 风险调整收益比率 (Sharpe, Sortino, Calmar)
- **数学公式**：
  $$Sharpe = \frac{CAGR - R_f}{\sigma_{ann}}$$
  $$Sortino = \frac{CAGR - R_f}{\sigma_{down}}$$
  $$Calmar = \frac{CAGR}{MaxDD}$$
- **业务用途**：
  - 夏普比率（Sharpe）：衡量每承担 1 单位总风险所换取的超额收益；
  - 索提诺比率（Sortino）：更公允评价具有肥右尾收益特征的趋势跟踪策略，不惩罚暴涨带来的上行波动；
  - 卡玛比率（Calmar）：年化收益对历史最恶劣回撤的覆盖倍数，是对冲基金与大资金管理的关键风控指标。

#### 1.7 日度在险价值与条件在险价值 (DailyVaR95, DailyCVaR95)
- **数学公式**（历史模拟法）：
  $$VaR_{0.95} = -Q_{0.05}(R)$$
  $$CVaR_{0.95} = -\frac{1}{M}\sum_{i=1}^M R_{(i)}, \quad R_{(i)} \le -VaR_{0.95}$$
- **业务用途**：历史分位数统计，衡量在 95% 置信水平下单日最大预期亏损极限；CVaR（预期尾部损失）度量突破 VaR 极端行情下的平均损失幅度，防范黑天鹅穿仓风险。

#### 1.8 基准相对统计 (BenchmarkReturn, Beta, Alpha, InformationRatio)
挂载 Benchmark 指数（如沪深300）时计算：
- **贝塔系数 (Beta)**：$\beta = \frac{Cov(R_s, R_b)}{Var(R_b)}$，策略收益对大盘基准收益的系统性敏感度；
- **詹森阿尔法 (Alpha)**：$\alpha = CAGR_s - [R_f + \beta (CAGR_b - R_f)]$，剥离市场系统性收益后的真实超额收益；
- **信息比率 (Information Ratio)**：
  $$Active_t = R_{s, t} - R_{b, t}, \quad TE = \sqrt{\frac{1}{N-1}\sum_{t=1}^N (Active_t - \overline{Active})^2} \times \sqrt{K}$$
  $$IR = \frac{CAGR_s - CAGR_b}{TE}$$
  衡量主动投资承担每单位跟踪误差所获得的超额收益稳定性。

#### 1.9 自然月度收益分布矩阵 (MonthlyReturns)
- **数学逻辑**：按自然年份与自然月份统计期初与期末净值变动：$R_{m} = \frac{E_{month\_end} - E_{month\_start}}{E_{month\_start}}$。
- **业务用途**：直观展示策略在不同自然月份的盈利稳定性，防止策略收益由单一极端大月主导而掩盖其余月份的常态化回撤。

---

### 2. 第 2 层：资本利用与资金敞口 (CapitalStatistics)
从资金管理与风控合规角度衡量资金利用率与杠杆安全边际。

#### 2.1 平均与最大持仓比例 (AveragePositionRatio, MaxPositionRatio)
- **数学公式**：
  $$Ratio_{pos, t} = \frac{\sum MarketValue_i}{TotalEquity_t}, \quad \overline{Ratio}_{pos} = \frac{1}{T}\sum_{t=1}^T Ratio_{pos, t}$$
- **业务用途**：监控策略是处于满仓进攻还是轻仓防御状态，防范仓位暴露过度集中。

#### 2.2 平均与最大保证金率 (AverageMarginRatio, MaxMarginRatio)
- **数学公式**：
  $$Ratio_{margin, t} = \frac{OccupiedMargin_t}{TotalEquity_t}, \quad MaxMarginRatio = \max_t (Ratio_{margin, t})$$
- **业务用途**：衍生品与期货杠杆交易的核心安全指标，防范保证金占用接近警戒线引发强制平仓。

#### 2.3 现金闲置率 (CashDrag)
- **数学公式**：
  $$CashDrag = \frac{1}{T}\sum_{t=1}^T \frac{AvailableCash_t}{TotalEquity_t}$$
- **业务用途**：量化非生息可用现金沉淀对策略整体资本回报的拖累程度。

#### 2.4 年化双向换手率 (AnnualizedTurnover)
- **数学公式**：
  $$Turnover_{ann} = \frac{\sum Turnover_{settlement} / 2}{\overline{Equity} \times Years}$$
- **业务用途**：衡量策略交易频繁程度，用于测算策略资金容量上限及潜在的冲击成本承载能力。

---

### 3. 第 3 层：交易行为与执行归因 (TradeStatistics)
以闭环平仓交易（Round-trip Trade）为最小样本进行微观行为统计。

#### 3.1 胜率与多空拆分 (WinRate, LongWinRate, ShortWinRate)
- **数学公式**：
  $$WinRate = \frac{N_{win}}{N_{total}}, \quad LongWinRate = \frac{N_{win, long}}{N_{long}}, \quad ShortWinRate = \frac{N_{win, short}}{N_{short}}$$
- **业务用途**：评估策略方向预测准确度，排查多头与空头是否存在显著的非对称偏科。

#### 3.2 盈亏比 (WinLossRatio) 与 盈利因子 (ProfitFactor)
- **数学公式**：
  $$WinLossRatio = \frac{\overline{Win}}{\lvert\overline{Loss}\rvert}, \quad ProfitFactor = \frac{\sum GrossProfit}{\sum \lvert GrossLoss\rvert}$$
- **边界约束**：全胜场景下分母为 0 时安全封顶为 `999.0` 哨兵值。
- **业务用途**：判断策略盈利逻辑（是高胜率低盈亏比的收割型，还是低胜率高盈亏比的趋势型），评估胜率与赔率的搭配合理性。

#### 3.3 连续盈亏 (MaxConsecutiveWins, MaxConsecutiveLosses)
- **数学逻辑**：单趟遍历平仓单序列，统计连胜与连败的最大连续长度。
- **业务用途**：评估资金曲线的连续回撤心理承载力，为动态仓位管理（如马丁格尔/凯利公式下调）提供依据。

#### 3.4 交易捕获效率 (TradeEfficiency / MFE)
- **数学公式**（多空双向对称）：
  - 多头交易：$$Eff_{long} = \frac{P_{exit} - P_{entry}}{\max_{t \in [entry, exit]}(P_t) - P_{entry}}$$
  - 空头交易：$$Eff_{short} = \frac{P_{entry} - P_{exit}}{P_{entry} - \min_{t \in [entry, exit]}(P_t)}$$
- **业务用途**：衡量平仓点位是否捕获到了持仓周期内的最大潜在有利偏离（MFE），评估出场机制是否过早或过晚。

#### 3.5 卖点时机反思 (ExitTiming)
- **数学公式**：
  统计平仓后未来 T+1..T+N 期的前向收益率分布：
  $$R_{forward, k} = \begin{cases} \frac{P_{exit+k} - P_{exit}}{P_{exit}}, & \text{Long} \\ \frac{P_{exit} - P_{exit+k}}{P_{exit}}, & \text{Short} \end{cases}$$
- **业务用途**：反思平仓后价格是否继续大幅顺向奔跑（提示卖早了/踏空），还是反向反转（提示成功逃顶），用于优化止盈止损策略。

#### 3.6 策略标签下钻 (TagBreakdowns)
- **业务用途**：按开仓标签（`EntryTag`）自动分桶，拆解不同子策略逻辑（如网格、趋势突破、均值回归）的独立胜率与盈亏贡献。

---

### 4. 第 4 层：现实交易成本与损耗 (CostStatistics)
衡量交易摩擦与税费损耗对策略实际利润的侵蚀。

#### 4.1 总摩擦成本 (TotalFrictionCost)
- **数学公式**：
  $$TotalFriction = \sum Commission + \sum SlippageCost$$
- **业务用途**：统筹券商佣金、经手费、印花税及成交滑点冲击损耗总额。

#### 4.2 摩擦侵蚀比 (FeeToGrossProfitRatio) 与 单笔平均摩擦 (AverageFrictionPerTrade)
- **数学公式**：
  $$FeeToProfit = \frac{TotalFriction}{\sum GrossProfit}, \quad AvgFriction = \frac{TotalFriction}{N_{trades}}$$
- **业务用途**：评估策略利润对摩擦成本的敏感度。若侵蚀比超过 30%，表明策略过于频繁或滑点控制不足，需优化换手或订单执行。

---

### 5. 独立 Alpha 信号质量评估轨 (SignalStatistics)
基于时序行情矩阵，完全脱离账户与撮合状态机对策略决策信号进行纯前向走势检验。

#### 5.1 前向周期收益与多空对称性
- **数学公式**：
  $$R_{sig, k} = Sign(SignalValue) \times \frac{P_{t+k} - P_t}{P_t}, \quad k \in [1, N]$$
  其中 $Sign(SignalValue) = 1.0$（做多信号）或 $-1.0$（做空信号）。
- **业务用途**：在不考虑资金限制与滑点的情况下，直接评估信号逻辑本身的前向超额预测能力。

#### 5.2 双口径加权对比 (SampleWeighted vs TimeWeighted)
- **样本等权**：
  $$\overline{R}_{sample} = \frac{1}{M}\sum_{i=1}^M R_{sig, i}$$
- **月度时间等权**：
  $$\overline{R}_{time} = \frac{1}{K}\sum_{m=1}^K \left( \frac{1}{M_m}\sum_{j=1}^{M_m} R_{sig, m, j} \right)$$
- **业务用途**：样本等权易受信号密集期（如行情活跃月信号暴增）主导产生幸存者偏差；时间等权按月先均化再汇总，可有效评估 Alpha 在不同时间周期上的均匀性与稳定性。

#### 5.3 最优持有周期识别 (PeakHorizonStep)
- **数学公式**：
  $$PeakStep = \arg\max_{k \in [1, N]} (\overline{R}_k)$$
- **业务用途**：自动寻找前向平均收益达到峰值的时间步长，为策略出场参数设定提供定量依据。

---

## 四、垂直切片单文件组织与职责归位 (Vertical Slice Architecture)

为保持计算逻辑与数据载体的职责分离，避免将算法置入数据载体造成模型臃肿，CarrotQuant.NET 采用**垂直切片单文件组织范式**：

1. **职责严格归位**：
   - **`*Analyzer`**：计算引擎（纯函数式、无状态），包含对应维度的全部数学统计公式、时序对齐、回撤追踪与切片算法；
   - **`*Statistics`**：纯数据载体（`sealed record : IAnalysisResult`），仅声明不可变字段与 `GetMetrics()` 自描述指标流；
   - **向后兼容转发**：`*Statistics` 上保留极简静态 `Create(...)` 转发至对应 Analyzer 的 `Calculate(...)`，保证对现有调用与测试套件 100% 兼容。
2. **单文件高内聚组织**：
   - 官方五大分析器及其专属产物均位于 `CarrotBacktesting.NET/Analysis/Analyzers/` 目录下的同名文件中（如 `PortfolioAnalyzer.cs` 同时包含 `PortfolioAnalyzer` 与 `PortfolioStatistics`）；
   - 编写扩展分析器时，只需新建单个 `.cs` 文件（上半部为计算类，下半部为结果 Record），无需跨目录维护多个文件。

```csharp
namespace CarrotBacktesting.NET.Analysis.Analyzers;

// 1. 上半部：计算引擎 (Analyzer)
public sealed class PortfolioAnalyzer : AnalyzerBase<PortfolioStatistics>
{
    public override string Name => "Portfolio";

    public override PortfolioStatistics? Analyze(
        TradingResult facts,
        AnalysisOptions options,
        ITimeseriesRegistry? timeseries)
    {
        IReadOnlyList<double>? benchmarkReturns = TryCalculateBenchmarkReturns(facts, options, timeseries);
        return Calculate(facts.EquityCurve, facts.DailySnapshots, options, benchmarkReturns);
    }

    public static PortfolioStatistics Calculate(...)
    {
        // 包含起止资产、CAGR、波动率、下行半方差、最大回撤、夏普/索提诺/卡玛、月度矩阵、基准 Alpha/Beta 等量化计算与辅助方法
    }
}

// 2. 下半部：纯数据载体 (Record)
public sealed record PortfolioStatistics(...) : IAnalysisResult
{
    public string Name => "portfolio";
    public string Title => "组合净值与风险收益统计";
    public IEnumerable<MetricItem> GetMetrics() { ... }

    // 静态工厂转发，保持兼容
    public static PortfolioStatistics Create(...) => PortfolioAnalyzer.Calculate(...);
}
```

---

## 五、统一单路径分析管线 (Single-Path Analyzer Pipeline)

CarrotQuant.NET 采用**统一单路径分析器管线（Single-Path Pipeline）**统一调度所有分析器：

### 1. 核心抽象契约 (`IAnalysisResult` 与 `IAnalyzer`)

所有分析产物（包括官方核心统计与外部扩展）均实现自描述分析结果契约 `IAnalysisResult`：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Analysis;

public readonly record struct MetricItem(string Label, string Value, string? Category = null);

public interface IAnalysisResult
{
    string Name { get; }
    string Title { get; }
    IEnumerable<MetricItem> GetMetrics(); // 自描述指标流，供任何 Exporter 无需硬编码通排通显
}

public interface IAnalyzer
{
    string Name { get; }
    IAnalysisResult? Analyze(TradingResult facts, AnalysisOptions options, ITimeseriesRegistry? timeseries);
}

public abstract class AnalyzerBase<TResult> : IAnalyzer where TResult : class, IAnalysisResult
{
    public abstract string Name { get; }
    public abstract TResult? Analyze(TradingResult facts, AnalysisOptions options, ITimeseriesRegistry? timeseries);
    IAnalysisResult? IAnalyzer.Analyze(...) => Analyze(facts, options, timeseries);
}
```

官方五大标准分析器（位于 `CarrotBacktesting.NET.Analysis.Analyzers`）与任何自定义扩展地位完全均等，其产出的五大强类型统计模型统一归属于纯抽象层 `CarrotBacktesting.NET.Abstraction.Analysis.StatisticsModels`：
- `PortfolioAnalyzer`: 包含全部净值宏观与基准算法，产出 `PortfolioStatistics`；
- `CapitalAnalyzer`: 包含持仓暴露、保证金与换手率算法，产出 `CapitalStatistics`；
- `TradeAnalyzer`: 包含胜率、盈亏比、连胜连败、MFE 与 Exit Timing 算法，产出 `TradeStatistics`；
- `CostAnalyzer`: 包含佣金、滑点与侵蚀比算法，产出 `CostStatistics`；
- `SignalAnalyzer`: 包含行情矩阵前向切片与双口径加权算法，产出 `SignalStatistics`。

### 2. 纯管线调度引擎 (`AnalysisEngine`)

`AnalysisEngine` 是一个纯粹的单路径调度器，自身不包含任何具体金融业务计算，仅对已注册的 `IAnalyzer` 序列依次调用并组装不可变报告：

```csharp
public sealed class AnalysisEngine : IAnalysisEngine
{
    private readonly List<IAnalyzer> _analyzers = new();

    public IReadOnlyList<IAnalyzer> Analyzers => _analyzers;

    // 默认构造函数：挂载五大标准分析器
    public AnalysisEngine()
    {
        _analyzers.Add(new PortfolioAnalyzer());
        _analyzers.Add(new CapitalAnalyzer());
        _analyzers.Add(new TradeAnalyzer());
        _analyzers.Add(new CostAnalyzer());
        _analyzers.Add(new SignalAnalyzer());
    }

    public AnalysisEngine(IEnumerable<IAnalyzer> analyzers) { ... }

    public IAnalysisEngine Add(IAnalyzer analyzer) { _analyzers.Add(analyzer); return this; }
    public IAnalysisEngine Remove<T>() where T : IAnalyzer { _analyzers.RemoveAll(a => a is T); return this; }

    // 单一路径执行：严格按序调度，结果注入不可变列表
    public AnalysisReport Analyze(TradingResult result, AnalysisOptions? options = null, ITimeseriesRegistry? timeseries = null)
    {
        var opt = options ?? new AnalysisOptions();
        var results = new List<IAnalysisResult>(_analyzers.Count);

        for (int i = 0; i < _analyzers.Count; i++)
        {
            var analyzer = _analyzers[i];
            var res = analyzer.Analyze(result, opt, timeseries);
            if (res != null)
            {
                results.Add(res);
            }
        }

        return new AnalysisReport(result, results);
    }
}
```

### 3. 全景聚合根报告 (`AnalysisReport`)

`AnalysisReport` 统一持有事实账本 `Facts` 与全量分析产物列表 `Results`。对外提供**单一且统一的查询接口**，杜绝任何二元分立：

```csharp
public sealed class AnalysisReport
{
    public TradingResult Facts { get; }

    // 全量平权产物序列（官方指标与第三方扩展毫无二致）
    public IReadOnlyList<IAnalysisResult> Results { get; }

    // 唯一的统一强类型检索接口（构造期固化 Type/Name 双哈希，O(1) 绝对确定性）
    public T? Get<T>(string? name = null) where T : class, IAnalysisResult;

    // 统一按名称索引器：report["portfolio"]、report["omega"]
    public IAnalysisResult? this[string name] { get; }

    // 统一探针方法
    public bool Has<T>(string? name = null) where T : class, IAnalysisResult;

    // 常用快捷门面（底层完全由 Get<T>() 派生，零特权）
    public PortfolioStatistics? Portfolio => Get<PortfolioStatistics>();
    public CapitalStatistics? Capital     => Get<CapitalStatistics>();
    public TradeStatistics? Trades        => Get<TradeStatistics>();
    public CostStatistics? Cost           => Get<CostStatistics>();
    public SignalStatistics? Signals      => Get<SignalStatistics>();
}
```

### 4. 外部扩展分析器实现示例

外部开发者编写自定义分析器，与官方分析器遵循相同的契约：

```csharp
// 1. 定义分析产物（实现 IAnalysisResult 并暴露自描述指标）
public sealed record BrinsonResult(double AllocationEffect, double SelectionEffect, double InteractionEffect) : IAnalysisResult
{
    public string Name => "brinson";
    public string Title => "Brinson 行业资产配置归因";

    public IEnumerable<MetricItem> GetMetrics()
    {
        yield return new("大类资产配置效应 (Allocation)", $"{AllocationEffect:P2}", "行业归因");
        yield return new("个股选择效应 (Selection)", $"{SelectionEffect:P2}", "行业归因");
        yield return new("交叉交互效应 (Interaction)", $"{InteractionEffect:P2}", "行业归因");
    }
}

// 2. 编写分析器
public sealed class BrinsonAttributionAnalyzer : AnalyzerBase<BrinsonResult>
{
    public override string Name => "brinson";

    public override BrinsonResult? Analyze(TradingResult facts, AnalysisOptions options, ITimeseriesRegistry? timeseries)
    {
        return new BrinsonResult(0.045, 0.082, -0.012);
    }
}

// 3. 挂载与消费：与官方分析器一致
var engine = new AnalysisEngine()
    .Add(new BrinsonAttributionAnalyzer());

var report = engine.Analyze(result);

// 强类型安全直取
var brinson = report.Get<BrinsonResult>();

// 控制台导出器通过 GetMetrics() 自动渲染表格
```

### 5. 声明式配置对齐 (YAML 驱动)

分析管线与报表导出器支持通过 YAML 声明式配置驱动装配（由 `AnalysisConfig` 与 `AnalysisEngineFactory` 统筹）：

```yaml
analysis:
  options:
    risk_free_rate: 0.02
    annual_trading_days: 252
    benchmark_symbol: "sh.000300"
    price_field_name: "close"
    exit_timing_steps: 20
    signal_horizon_steps: 30
  analyzers:
    - type: "portfolio"
    - type: "capital"
    - type: "trades"
    - type: "cost"
    # - type: "signals"   # 注释或设 enabled: false 即可移除
    - type: "BrinsonAttributionAnalyzer" # 注册外部扩展分析器
  exporters:
    - type: "console"
      params:
        save_html: true
        html_output_path: "output/summary.html"
    - type: "json"
      params:
        output_path: "output/report.json"
        indented: true
```

---

## 六、报表呈现与导出集成 (Reporting & Exporter Integration)

CarrotQuant.NET 遵循**表现层物理隔离**原则，分析内核（`AnalysisEngine`）仅负责单路径执行分析管线并生成不可变的 `AnalysisReport`，报表的多介质呈现与持久化（ANSI 终端彩色表格、深色主题 HTML 报表、JSON API 等）完全移交至独立的报表导出子系统（`CarrotBacktesting.NET.Analysis.Exporters`）。

有关报表导出的详细架构设计、`IReportExporter` 契约规范、自描述指标流机制、`ConsoleExporter` 配置项、深色主题模板定制及自定义导出器开发范式，请参阅专门白皮书：

👉 **[报表导出与呈现子系统开发指南 (docs/exporter.md)](exporter.md)**

```csharp
// 典型导出集成调用示例
var engine = new BacktestingEngine(context);
TradingResult result = engine.Run();

// 方式 1：声明式配置驱动装配与一键导出 (AnalysisConfig & AnalysisEngineFactory)
AnalysisConfig config = sessionConfig.Analysis ?? new AnalysisConfig();
IAnalysisEngine analysisEngine = AnalysisEngineFactory.Create(config);

// 方式 1.1：一站式分析并按配置自动完成终端/HTML/JSON全渠道导出
AnalysisReport report = analysisEngine.AnalyzeAndExport(result, config);

// 方式 1.2：分步分析与内聚导出
// AnalysisReport report = analysisEngine.Analyze(result, config.Options.ToOptions());
// config.Export(report);

// 方式 2：纯代码显式装配
IAnalysisEngine defaultEngine = new AnalysisEngine();
AnalysisReport directReport = defaultEngine.Analyze(result);
ConsoleExporter.Default.Export(directReport);
```


---

## 七、历史版本对比与演进 (V3 vs Lean vs CarrotQuant.NET)

| 评价维度 | V3 旧版本 | QuantConnect Lean | CarrotQuant.NET 现代版本 |
| :--- | :--- | :--- | :--- |
| **组合净值统计** | ❌ 缺失（无资金账本，仅单笔平均） | ✔️ `PortfolioStatistics`（标准但依赖重） | ✔️ `PortfolioStatistics`（纯函数式，支持日度 VaR/CVaR 与动态回撤修复） |
| **资本利用统计** | ❌ 缺失 | 部分支持（现金与杠杆分散在各处） | ✔️ `CapitalStatistics`（统一聚焦持仓、保证金、现金闲置与换手率） |
| **微观交易归因** | ✔️ `TradeAnalyzer`（仅多头） | ✔️ `TradeStatistics` | ✔️ `TradeStatistics`（多空对称 MFE 交易捕获效率、平仓后 Exit Timing 卖点反思） |
| **现实摩擦成本** | ❌ 缺失 | ✔️ 手续费追踪 | ✔️ `CostStatistics`（佣金 + 滑点冲击 + 利润侵蚀比） |
| **Alpha 信号评估** | ✔️ `SignalAnalyzer`（二分查找慢） | ❌ 缺失（需外挂研究环境） | ✔️ `SignalStatistics`（基于时序矩阵切片，单趟遍历低内存开销，支持最优持有期识别） |
| **软件架构与范式** | 弱类型 Context + 反射工厂 | 外部集中式 `StatisticsBuilder` | **垂直切片单文件组织**（计算引擎与结果模型同文件内聚） |
