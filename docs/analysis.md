# 绩效分析与报表子系统 (Analysis & Reporting System)

本文档面向系统架构师、量化研究员与策略开发者，系统介绍 CarrotQuant.NET 中**绩效分析与报表子系统**（`CarrotBacktesting.NET/Analysis/` 与 `CarrotBacktesting.NET/Abstraction/Analysis/`）的整体架构、两阶段解耦设计、四层量化统计体系（对齐 QuantConnect Lean）、独立 Alpha 信号前向评估（升级 V3）、富领域模型静态工厂范式以及终端报表呈现。

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
- [四、富领域模型与 Create 静态工厂范式](#四富领域模型与-create-静态工厂范式)
- [五、分析调度引擎 (AnalysisEngine)](#五分析调度引擎-analysisengine)
- [六、控制台仪表盘与 HTML 导出 (ConsoleExporter)](#六控制台仪表盘与-html-导出-consoleexporter)
- [七、历史版本对比与演进 (V3 vs Lean vs CarrotQuant.NET)](#七历史版本对比与演进-v3-vs-lean-vs-carrotquantnet)

---

## 一、架构总览与两阶段解耦设计

在工业级量化交易系统中，高频回测与实盘交易对热路径（Hot Path）的吞吐与延迟有极高要求。CarrotQuant.NET 遵循**“两阶段严格物理解耦”**原则：

```mermaid
graph TD
    subgraph Phase1 [第一阶段：运行期流水记账 (Hot Path - 纳秒级吞吐)]
        Engine[BacktestingEngine / LivingEngine]
        Recorder[TradingRecorder<br>纯事实流水沉淀 / ICheckpointable]
        Engine -->|RecordStep / RecordSettlement / RecordClosedTrade| Recorder
    end

    Recorder -->|GetResult| Facts[TradingResult<br>不可变单一事实来源 SSOT]

    subgraph Phase2 [第二阶段：收尾期纯函数式后处理 (Post-Processing)]
        Facts --> EngineAnalysis[AnalysisEngine 统一分析引擎]
        Timeseries[ITimeseriesRegistry 只读行情矩阵 (可选)] --> EngineAnalysis

        EngineAnalysis --> L1[PortfolioStatistics.Create<br>组合净值与风险统计]
        EngineAnalysis --> L2[CapitalStatistics.Create<br>资本利用与敞口统计]
        EngineAnalysis --> L3[TradeStatistics.Create<br>交易行为与归因统计]
        EngineAnalysis --> L4[CostStatistics.Create<br>交易成本与摩擦损耗]
        EngineAnalysis --> L5[SignalStatistics.Create<br>Alpha 信号前向质量]

        L1 & L2 & L3 & L4 & L5 --> Report[AnalysisReport 综合分析报告]
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

CarrotQuant.NET 对齐 QuantConnect Lean 与现代资管标准，将所有分析结果归纳为**四层递进统计量**加**一层独立 Alpha 评估轨**：

### 1. 第 1 层：组合净值与宏观风险 (PortfolioStatistics)
面向资管与基金层面的宏观资金收益与抗风险能力评估。

- **总收益率 (`TotalReturn`)**：$(Equity_{end} - Equity_{start}) / Equity_{start}$。
- **年化复合收益率 (`AnnualizedReturn` / CAGR)**：$(Equity_{end} / Equity_{start})^{365.25 / TotalDays} - 1.0$。
- **年化波动率 (`AnnualizedVolatility`)**：周期收益率的样本标准差 $\times \sqrt{252}$。
- **下行半方差 (`DownsideVolatility`)**：低于目标无风险日收益率的下行离差方差折算年化。
- **最大回撤 (`MaxDrawdown`)**：
  - 峰谷追踪：记录峰值时间点 (`MaxDrawdownPeakTime`) 与谷底时间点 (`MaxDrawdownTroughTime`)；
  - 动态修复：若后续资产创新高，记录修复时刻 (`MaxDrawdownRecoveryTime`)；
  - 持续步数 (`MaxDrawdownDurationSteps`)：从峰值到修复（或回测结束）的时间跨度。单调无回撤时严格归零。
- **夏普比率 (`SharpeRatio`)**：$(CAGR - R_f) / \sigma_{ann}$。
- **索提诺比率 (`SortinoRatio`)**：$(CAGR - R_f) / \sigma_{down}$（不惩罚上行波动）。
- **卡玛比率 (`CalmarRatio`)**：$CAGR / MaxDD$（每承担 1 单位回撤获得的年化回报）。
- **日度在险价值 (`DailyVaR95`) 与 条件在险价值 (`DailyCVaR95`)**：95% 置信度下的历史分位数极值与尾部期望损失。
- **基准相对统计**（挂载 Benchmark 标的时自动激活）：
  - `BenchmarkReturn`：基准指数累计收益率；
  - `Beta`：策略收益对基准收益的敏感度 $\frac{Cov(R_s, R_b)}{Var(R_b)}$；
  - `Alpha`：詹森阿尔法超额回报；
  - `InformationRatio`：信息比率，基于主动超额收益的标准差（跟踪误差）计算。
- **月度收益矩阵 (`MonthlyReturns`)**：按自然年份和月份切片的月度收益率列表。

---

### 2. 第 2 层：资本利用与资金敞口 (CapitalStatistics)
从资金管理与风控合规角度衡量资金利用率与杠杆安全边际。

- **平均/最大持仓比例 (`AveragePositionRatio` / `MaxPositionRatio`)**：持仓名义市值占动态总资产的均值与峰值。
- **平均/最大保证金率 (`AverageMarginRatio` / `MaxMarginRatio`)**：占用保证金占总资产的均值与峰值（杠杆与期货交易核心安全指标）。
- **现金闲置率 (`CashDrag`)**：可用现金占总资产的平均比例，量化现金沉淀对收益的拖累。
- **年化双向换手率 (`AnnualizedTurnover`)**：$(\sum Turnover / 2) / \overline{Equity} / Years$。

---

### 3. 第 3 层：交易行为与执行归因 (TradeStatistics)
以闭环平仓交易（Round-trip Trade）为最小样本进行微观统计。

- **笔数与胜率**：总笔数、盈利笔数、亏损笔数、保本笔数、总胜率 (`WinRate`)。
- **多空独立拆分**：多头笔数/胜率 (`LongTrades`, `LongWinRate`)、空头笔数/胜率 (`ShortTrades`, `ShortWinRate`)。
- **盈亏比 (`WinLossRatio`)**：平均盈利幅度 / 平均亏损幅度。全胜无亏损时安全封顶为 `999.0` 哨兵值。
- **盈利因子 (`ProfitFactor`)**：总毛利 / 总毛亏。无亏损时安全封顶为 `999.0`。
- **连续统计**：最大连续盈利笔数 (`MaxConsecutiveWins`)、最大连续亏损笔数 (`MaxConsecutiveLosses`)。
- **持仓时长**：平均持仓步数 (`AverageHoldingSteps`)，以及盈利持仓与亏损持仓的持仓时长对比。
- **交易捕获效率 (`TradeEfficiency` / MFE)**：
  - **多头交易**：$\frac{ExitPrice - EntryPrice}{\max(Price) - EntryPrice}$；
  - **空头交易**：$\frac{EntryPrice - ExitPrice}{EntryPrice - \min(Price)}$；
  - 多空双向对称衡量交易是否捕获到了持仓周期内的大部分潜在利润。
- **卖点时机反思 (`ExitTiming`)**：
  - 统计平仓后未来 T+1..T+N 步标的后续走势的平均收益、中位数收益与上涨概率；
  - 量化评估卖点是否过早（卖在起飞前）或成功逃顶。
- **多标签下钻 (`TagBreakdowns`)**：按策略标签（`EntryTag`）自动分桶，提供同维度的完整子级 `TradeStatistics`。

---

### 4. 第 4 层：现实交易成本与损耗 (CostStatistics)
衡量交易摩擦与税费损耗对策略实际利润的侵蚀。

- **累计佣金税费 (`TotalCommission`)**：交割单累计手续费与印花税。
- **累计滑点冲击 (`TotalSlippage`)**：所有平仓交易累计滑点损耗总额。
- **总摩擦成本 (`TotalFrictionCost`)**：佣金 + 滑点。
- **摩擦侵蚀比 (`FeeToGrossProfitRatio`)**：总摩擦成本 / 策略总毛利润。
- **平均单笔摩擦 (`AverageFrictionPerTrade`)**：单笔交易的平均交易损耗。

---

### 5. 独立 Alpha 信号质量评估轨 (SignalStatistics)
继承并升级 V3 `SignalAnalyzer` 核心思想，基于时序连续内存矩阵对策略决策信号进行前向评估。

- **前向周期走势 (`Horizons`)**：计算信号发生后未来 T+1..T+N 期的统计表现。
- **双口径加权对比**：
  - **样本等权 (`SampleWeighted`)**：全量信号等权汇总；
  - **月度时间等权 (`TimeWeighted`)**：按自然月先均化再汇总，防止特定极端单月（如极端行情暴增信号）主导全局统计。
- **最优持有周期 (`PeakHorizonStep`)**：自动识别前向收益均值达到极大值点的时间窗口。
- **分组下钻 (`GroupBreakdowns`)**：按信号组别（`SignalGroup`）分桶独立评估。

---

## 四、富领域模型与 Create 静态工厂范式

CarrotQuant.NET 摒弃了外围分散、贫血的 `*Calculator` 类，采用了 **“富领域模型 + 纯函数式 `Create(...)` 静态工厂”** 范式：

```csharp
// 1. 各 Statistics 模型自身即是不可变数据载体，同时内聚纯静态构建工厂
public sealed record PortfolioStatistics(...)
{
    public static PortfolioStatistics Create(
        IReadOnlyList<EquityPoint> equityCurve,
        IReadOnlyList<DailySnapshot> dailySnapshots,
        AnalysisOptions options,
        IReadOnlyList<double>? benchmarkReturns = null) { ... }
}

public sealed record CapitalStatistics(...)
{
    public static CapitalStatistics Create(
        IReadOnlyList<DailySnapshot> dailySnapshots,
        IReadOnlyList<EquityPoint> equityCurve,
        IReadOnlyList<SettlementRecord> settlements,
        AnalysisOptions options) { ... }
}
```

### 设计优势
1. **类型极致自解释**：外部使用者无需寻找专门的计算器，通过 `PortfolioStatistics.Create(...)` 即可从事实账本纳秒级生成不可变对象。
2. **零符号泄露与单一职责**：内部辅助计算方法（如分位数、收益提取、峰值识别）完全封装于静态方法内，对外只暴露清洁的只读属性。

---

## 五、分析调度引擎 (AnalysisEngine)

`IAnalysisEngine` 是绩效分析子系统的统一门面契约：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Analysis;

public interface IAnalysisEngine
{
    AnalysisReport Analyze(
        TradingResult result,
        AnalysisOptions? options = null,
        ITimeseriesRegistry? timeseries = null);
}
```

调度实现极度清爽收敛：

```csharp
public AnalysisReport Analyze(TradingResult result, AnalysisOptions? options = null, ITimeseriesRegistry? timeseries = null)
{
    var opt = options ?? new AnalysisOptions();
    var benchmarkReturns = TryCalculateBenchmarkReturns(result, opt, timeseries);

    return new AnalysisReport(
        Facts:     result,
        Portfolio: PortfolioStatistics.Create(result.EquityCurve, result.DailySnapshots, opt, benchmarkReturns),
        Capital:   CapitalStatistics.Create(result.DailySnapshots, result.EquityCurve, result.Settlements, opt),
        Trades:    TradeStatistics.Create(result.ClosedTrades, opt, timeseries),
        Cost:      CostStatistics.Create(result.Settlements, result.ClosedTrades),
        Signals:   SignalStatistics.Create(result.Signals, timeseries, opt)
    );
}
```

---

## 六、控制台仪表盘与 HTML 导出 (ConsoleExporter)

系统基于 `Spectre.Console` 提供现代交互式终端报告导出器 `ConsoleExporter`，采用**纯属性驱动的强类型配置**与**深色主题 HTML 报表一键持久化**，天然与 YAML / JSON 声明式配置无缝对接：

```csharp
// 1. 全局默认导出：终端打印全量四层量化与信号评估表格
ConsoleExporter.Default.Export(report);

// 2. 精准模块开关控制：仅输出组合净值与交易归因，并一键生成深色 HTML
var exporter = new ConsoleExporter(new ConsoleExporterOptions
{
    ShowCapital = false,       // 屏蔽资本利用模块
    ShowCost = false,          // 屏蔽交易成本模块
    ShowSignals = false,       // 屏蔽 Alpha 信号评估模块
    SaveHtml = true,
    HtmlOutputPath = "output/summary.html"
});
exporter.Export(report);

// 3. 极简主摘要预设 (SummaryOnly: 自动关闭逐月与下钻详情，仅展示主表)
new ConsoleExporter(ConsoleExporterOptions.SummaryOnly).Export(report);
```

### 配置选项与 YAML 对齐 (`ConsoleExporterOptions`)

| 配置属性 (Property) | YAML 键名 (Snake Case) | 默认值 | 对应层级与说明 |
| :--- | :--- | :---: | :--- |
| `ShowPortfolio` | `show_portfolio` | `true` | 第一层：组合净值与风险统计（累计收益、CAGR、波动率、夏普、索提诺、卡玛、VaR、Beta、Alpha 等） |
| `ShowMonthlyReturns`| `show_monthly_returns`| `true` | 第一层扩展：逐月收益率分布矩阵（依赖 `ShowPortfolio`） |
| `ShowCapital` | `show_capital` | `true` | 第二层：资本利用与敞口分析（平均/最大仓位、保证金占用、现金闲置率、年化换手率） |
| `ShowTrades` | `show_trades` | `true` | 第三层：交易行为与执行归因（交易笔数、胜率、多空对比、盈亏比、盈利因子、连胜连败、持仓周期） |
| `ShowTradeEfficiency`| `show_trade_efficiency`| `true` | 第三层扩展：多空对称最大有利偏离度 (MFE) 捕获效率（依赖 `ShowTrades`） |
| `ShowExitTiming` | `show_exit_timing` | `true` | 第三层扩展：卖点时机反思与踏空走势 T+1..T+N（依赖 `ShowTrades`） |
| `ShowTradeTags` | `show_trade_tags` | `true` | 第三层扩展：策略标签 (Tag) 下钻分组归因（依赖 `ShowTrades`） |
| `ShowCost` | `show_cost` | `true` | 第四层：现实成本与摩擦损耗（手续费、滑点、综合摩擦、费毛比、单笔摩擦） |
| `ShowSignals` | `show_signals` | `true` | 第五层：Alpha 信号前向收益分布评估（最优前向持有周期、每日收益均值/中位数/胜率/盈亏比） |
| `ShowSignalGroups` | `show_signal_groups` | `true` | 第五层扩展：Alpha 信号分组下钻分析（依赖 `ShowSignals`） |
| `SaveHtml` | `save_html` | `false`| 是否在控制台渲染的同时生成深色 HTML 报表文件 |
| `HtmlOutputPath` | `html_output_path` | `"summary.html"` | HTML 报表持久化文件路径 |

---

## 七、历史版本对比与演进 (V3 vs Lean vs CarrotQuant.NET)

| 评价维度 | V3 旧版本 | QuantConnect Lean | CarrotQuant.NET 现代版本 |
| :--- | :--- | :--- | :--- |
| **组合净值统计** | ❌ 缺失（无资金账本，仅单笔平均） | ✔️ `PortfolioStatistics`（标准但依赖重） | ✔️ `PortfolioStatistics`（纯函数式，支持日度 VaR/CVaR 与动态回撤修复） |
| **资本利用统计** | ❌ 缺失 | 部分支持（现金与杠杆分散在各处） | ✔️ `CapitalStatistics`（统一聚焦持仓、保证金、现金闲置与换手率） |
| **微观交易归因** | ✔️ `TradeAnalyzer`（仅多头） | ✔️ `TradeStatistics` | ✔️ `TradeStatistics`（多空对称 MFE 交易捕获效率、平仓后 Exit Timing 卖点反思） |
| **现实摩擦成本** | ❌ 缺失 | ✔️ 手续费追踪 | ✔️ `CostStatistics`（佣金 + 滑点冲击 + 利润侵蚀比） |
| **Alpha 信号评估** | ✔️ `SignalAnalyzer`（二分查找慢） | ❌ 缺失（需外挂研究环境） | ✔️ `SignalStatistics`（时序矩阵纳秒切片，单趟遍历零 GC，支持最优峰值持有期） |
| **软件架构与范式** | 弱类型 Context + 反射工厂 | 外部集中式 `StatisticsBuilder` | **富领域模型自内聚 `Create` 静态工厂**，零外围冗余类 |
