# CarrotQuant .NET 核心架构与开发规范 (v4)

## 一、系统架构总览 (v4 Software Architecture)

CarrotQuant.NET v4 采用 **三层分离架构**，各层之间通过接口契约解耦，支持回测与实盘双引擎。

### 1. 架构拓扑

```mermaid
graph TD
    subgraph DataLayer [Data Layer - 数据层]
        subgraph Common [Common - 基础寻址]
            StorageResolver[StorageResolver]
        end

        subgraph FieldsSubsystem [Fields - 标的属性与维表子系统]
            FieldSourceFactory[FieldSourceFactory]
            FieldTable[FieldTable]
            FieldRegistry[FieldRegistry]
            FieldRegistryFactory[FieldRegistryFactory]
            IFieldRegistry[IFieldRegistry]

            FieldSourceFactory --> FieldTable
            FieldTable --> FieldRegistry
            FieldRegistryFactory --> FieldRegistry
            FieldRegistry --> IFieldRegistry
        end

        subgraph TimeseriesSubsystem [Timeseries - 时序子系统]
            TimeseriesSourceBase[TimeseriesSourceBase]
            CsvTimeseriesSource[CsvTimeseriesSource]
            ParquetTimeseriesSource[ParquetTimeseriesSource]
            TimeseriesSourceFactory[TimeseriesSourceFactory]
            MarketDataAligner[MarketDataAligner]
            DynamicMarketMetadata[DynamicMarketMetadata]
            TimeseriesCache[TimeseriesCache]
            TimeseriesCacheManager[TimeseriesCacheManager&lt;T&gt;]
            ITimeseriesCache[ITimeseriesCache]
            ChunkMatrix2D[ChunkMatrix2D&lt;T&gt;]
            TimeseriesRegistry[TimeseriesRegistry]
            TimeseriesRegistryFactory[TimeseriesRegistryFactory]
            ITimeseriesRegistry[ITimeseriesRegistry]
            ITimeseriesWriter[ITimeseriesWriter]

            TimeseriesSourceBase --> CsvTimeseriesSource
            TimeseriesSourceBase --> ParquetTimeseriesSource
            MarketDataAligner --> TimeseriesSourceBase
            DynamicMarketMetadata --> TimeseriesRegistry
            TimeseriesSourceFactory --> CsvTimeseriesSource
            TimeseriesSourceFactory --> ParquetTimeseriesSource
            TimeseriesRegistryFactory --> TimeseriesRegistry
            CsvTimeseriesSource --> TimeseriesRegistry
            ParquetTimeseriesSource --> TimeseriesRegistry
            TimeseriesRegistry --> TimeseriesCacheManager
            TimeseriesCacheManager --> ITimeseriesCache
            TimeseriesCacheManager --> ChunkMatrix2D
            TimeseriesRegistry --> ITimeseriesRegistry
            TimeseriesRegistry --> ITimeseriesWriter
        end

        subgraph EventsSubsystem [Events - 事件子系统]
            EventSourceFactory[EventSourceFactory]
            CsvEventSource[CsvEventSource]
            ParquetEventSource[ParquetEventSource]
            EntityMapper[EntityMapper]
            EventTable[EventTable]
            EventRegistry[EventRegistry]
            EventRegistryFactory[EventRegistryFactory]
            IEventTable[IEventTable]
            IEventRegistry[IEventRegistry]

            EventSourceFactory --> CsvEventSource
            EventSourceFactory --> ParquetEventSource
            EntityMapper --> CsvEventSource
            EntityMapper --> ParquetEventSource
            CsvEventSource --> EventTable
            ParquetEventSource --> EventTable
            EventTable --> IEventTable
            EventRegistry --> IEventTable
            EventRegistryFactory --> EventRegistry
        end

        StorageResolver --> TimeseriesSourceFactory
        StorageResolver --> EventSourceFactory
        StorageResolver --> FieldSourceFactory
    end

    subgraph EngineExecutionLayer [Engine and Execution Layer - 引擎与执行层]
        BacktestingEngine[BacktestingEngine]
        LivingEngine[LivingEngine]
        IEngine[IEngine]
        IEngineContext[IEngineContext]
        IExchangeGateway[IExchangeGateway]
        IBroker[IBroker]
        IMonitor[IMonitor]

        BacktestingEngine --> IEngine
        LivingEngine --> IEngine
        ITimeseriesRegistry --> IEngine
        IEventRegistry --> IEngine
        IFieldRegistry --> IEngine
        IEngine --> IExchangeGateway
        IEngine --> IBroker
        IEngine --> IMonitor
        IEngine --> IEngineContext
    end

    subgraph StrategyLayer [Strategy Layer - 策略层]
        IStrategy[IStrategy]
        IStrategyPipeline[IStrategyPipeline]

        IEngineContext --> IStrategy
        IStrategy --> IStrategyPipeline
    end
```

### 2. 核心分层职责与流向

| 分层 | 核心职责 | 核心组件 / 契约 |
| :--- | :--- | :--- |
| **Data Layer (数据层)** | 数据获取、格式转换、内存缓冲与统一供给 | `StorageResolver`, `CsvTimeseriesSource`, `ParquetTimeseriesSource`, `TimeseriesRegistry`, `TimeseriesCacheManager<T>`, `ITimeseriesRegistry`, `IEventTable`, `IEventRegistry`, `IFieldRegistry`, `FieldRegistry`, `FieldSourceFactory`, `FieldTable` |
| **Engine Layer (引擎层)** | 回测撮合与实盘路由、账户资产管理、上下文注入 | `IEngine`, `BacktestingEngine`, `LivingEngine`, `IEngineContext`, `IExchangeGateway`, `IMatchingEngine`, `IBroker`, `IMonitor` |
| **Strategy Layer (策略层)** | 宏观决策、信号生成、组合管理与交易执行 | `IStrategy`, `IStrategyPipeline`, `IMarketStrategy`, `ISignalStrategy`, `IPortfolioStrategy` |

- **数据流向**: (`ITimeseriesRegistry`, `IEventRegistry`, `IFieldRegistry`) → `IEngine` → (`IExchangeGateway` / `IBroker` / `IMonitor`) → `IEngineContext` → 策略层
- **策略调度链**: `IPortfolioStrategy` → `IMarketStrategy` → `ISignalStrategy`

### 3. 策略协作机制

#### 3.1 Context 黑板模式
通过 `IEngineContext` 作为统一的信息交换中心（黑板），实现策略解耦与状态共享：
- **信号发生器**: 负责行情分析与信号生产（Signal Generation），输出结论至上下文，不触碰交易接口。
- **执行处理器**: 监听黑板上的信号并转化为物理订单（Order Placement），对信号来源解耦。
- **风控监察员**: 监听账户回报（ExecutionReport/PositionEvent），实时执行风险对冲与合规校验。
- **松耦合机制**: 遵循“状态生产 → Context 存储 → 状态消费”模式，单一策略职责聚焦且支持自由组合。

#### 3.2 策略管线 (Pipeline)
基于组合模式（Composite Pattern）编排策略拓扑：
- **串联管线 (Sequential)**: 按序执行，适用于因果依赖链（如：`宏观策略` → `个股信号` → `执行策略`）。
- **并联管线 (Parallel)**: 并发执行，适用于相互独立、计算密集型的策略组，利用多核性能。
- **编译优化**: 引擎启动前通过 `Compile()` 密封优化管线拓扑，保障时间步事件分发性能。

---

## 二、市场策略 (IMarketStrategy) 体系

### 1. RaUA 架构管线
- `IMarketStrategy` 产生的宏观决策通过 `context.Market` 注入到个股扫描 (`ISignalStrategy`) 和交易执行上下文中。
- **MarketBias** (市场偏向): `Up` (看多/活跃), `Neutral` (中性), `Down` (看空/冰点)。

### 2. 决策消费与剪枝
- **剪枝机制**: 引擎在执行 `ISignalStrategy` 前检查 `context.Market`。若 `MarketContext.SkipAlpha` 为 `true`，自动跳过当日所有个股的信号计算。
- **状态访问**: 策略通过 `context.Market.GetState<T>()` 获取强类型自定义状态（如：`var metrics = context.Market.GetState<MyMarketMetrics>();`）。

---

## 三、数据层契约 (Data Layer Contracts)

所有接口位于 `CarrotBacktesting.NET/Abstraction/Data/`，禁止上层绕过接口直接访问物理存储。

### 1. 物理层抽象（ETL 层）

| 接口 / 类型 | 文件 | 职责 |
| :--- | :--- | :--- |
| `IMarketMetadata` | `Abstraction/Data/IMarketMetadata.cs` | 统一元数据契约（描述时间与代码维度及物理寻址） |
| `ITimeseriesSource`  | `Abstraction/Data/ITimeseriesSource.cs`   | 批量读取个股序列（Col = 股票，Row = 时间序列） |

**物理实现与设计细节**：
- `TimeseriesSourceBase`: 模板方法抽象基类，收敛参数边界检查、ArrayPool 内存池租借与自适应翻倍扩容、时间戳排序、以及 `MarketDataAligner.AlignAndFill` 全局交易日历投射与 NaN 哨兵填充管线。
- `TimeseriesSourceFactory`: 时序数据源工厂，根据 `StorageResolver.GetFormat(tableId)` 自动派发构建 `CsvTimeseriesSource` 或 `ParquetTimeseriesSource`，调用端零物理格式耦合。
- `ReadSymbolSeries<T>(symbol, fieldName, startIndex, length, destination)`: 直接写入调用方提供的内存块，严格零拷贝；`T : unmanaged` 约束兼容 MMF/连续内存布局。
- `CsvTimeseriesSource`: 支持 Hive 分区 `{root}/{table_id}/year={yyyy}/{symbol}.csv` 与 Flat 结构；基于 Sylvan.Data.Csv 流式解析，泛型分发 `ValueConverter<T>` 零装箱转换。
- `ParquetTimeseriesSource`: 支持按年/月分区 `{root}/{table_id}/year={yyyy}/[month={mm}/]data.parquet` 或单文件；基于 `YearCache` 按需列缓存解压，通过 `Unsafe.As` 零拷贝转换。
- **单一数据结构与零抽象分叉**：系统统一采用 `ChunkMatrix2D<T>`（`IReadOnlyMatrix2D<T>`）作为全市场稠密数据载体。横向截面快照统一由行切片（`ReadOnlyRowView<T>`）提供，纵向时序统一由列切片（`ReadOnlyColumnView<T>`）提供，废除孤立的流式 `IMarketSnapshotSource`，回测与实盘统一走 Matrix 供给。

### 2. 内存层接口与 Matrix 访问

| 接口 / 类型 | 文件 | 职责 |
| :--- | :--- | :--- |
| `ITimeseriesRegistry` | `Abstraction/Data/ITimeseriesRegistry.cs` | 数据层只读输出入口，`GetMatrix<T>(fieldName)` 返回 `IReadOnlyMatrix2D<T>` |
| `ITimeseriesWriter` | `Abstraction/Data/ITimeseriesWriter.cs` | 实盘时序写入与一致性落盘契约（`AppendRow`, `AppendCol`, `Commit`） |
| `IFieldTable` | `Abstraction/Data/IFieldTable.cs` | 标的静态维表契约，单一内核支持动态点查与 `.Get<T>()`/`.As<T>()` 强类型流式 LINQ |
| `IFieldRegistry` | `Abstraction/Data/IFieldRegistry.cs` | 标的静态属性与维表对应契约，支持 1:1 单值点查、1:N 多值标签及 N:1 双向倒排索引选股 |
| `IDataRow` | `Abstraction/Data/IDataRow.cs` | 动态数据行纯契约抽象，物理隔离具体实现 |
| `IEventTable` | `Abstraction/Data/IEventTable.cs` | 异构事件与离散数据表契约，单一内核支持 `[date, symbol]` 复合索引、截面字典遍历与流式 LINQ |
| `IEventRegistry` | `Abstraction/Data/IEventRegistry.cs` | 事件注册表，统一管理外部时态事件流 |

**核心内存约束 (SSOT)**：
1. `ITimeseriesRegistry.GetMatrix<T>()` 必须返回只读类型 `IReadOnlyMatrix2D<T>`，严禁暴露可写 Matrix。单标的（$N=1$）回测通过 `provider.GetSeries<T>()` 获取只读一维时序序列。
2. 维度严格对齐：`IMarketMetadata.Symbols` 顺序对应列索引；`TradeDates` 顺序对应行索引。
3. 零内存暴露：严禁对外暴露 `List<T>`、`T[]` 等分配型集合供外部持有。
4. **Symbol 维度契约（只增不减，只多不少）**：在系统生命周期内，标的代码（Symbols）具有全局唯一且单调递增的列索引（`SymbolIndex`）。退市标的绝不剔除（以 `NaN` 占位），新增标的只追加在尾部。已有列索引绝对不可变动，杜绝幸存者偏差。

### 3. ChunkMatrix2D 内存容器与视图契约

```
ITimeseriesSource ──(构造时扫描)──► IMarketMetadata (TradeDates, Symbols)
         │
         │  ReadSymbolSeries<T>()
         ▼
  ChunkMatrix2D<T> ──(实现)──► IReadOnlyMatrix2D<T> ──► ITimeseriesRegistry.GetMatrix<T>()
```
- 架构归属：核心抽象契约与切片内存视图统一位于 `CarrotBacktesting.NET.Abstraction.Data`。
- 关键类型：`ChunkMatrix2D<T>`、`IReadOnlyMatrix2D<T>`、`IMatrix2D<T>`、`ReadOnlyRowView<T>`、`ReadOnlyColumnView<T>`、`RowView<T>`、`ColumnView<T>`、`MatrixExtensions`。

### 4. 异构事件流系统 (Event System)

针对复权因子、龙虎榜、分红、停牌等异构 KV 数据，采用与稠密矩阵分离的多模态双轨架构：

| 特性 | **ITimeseriesRegistry (稠密矩阵)** | **IEventRegistry (稀疏事件/KV)** |
| :--- | :--- | :--- |
| **物理形态** | 连续内存块 (`ChunkMatrix2D`, MMF) | 字典树、实体列表 (`Dictionary<DateTime, Dictionary<string, IReadOnlyList<T>>>`) |
| **访问范式** | 批量切片、二维索引 `(row, col)` 访问 | 随机、点对点 `(date, symbol)` 检索 |
| **应用场景** | 向量化指标计算（OHLCV、MACD、因子合成） | 决策分支（停牌拦截、分红除权、事件驱动） |
| **对齐要求** | **强对齐**（日期与代码矩阵完整填补） | **弱对齐**（发生时存在记录，无需空位对齐） |
| **优化目标** | **高吞吐 (Throughput)**，连续内存向量化批处理 | **低延迟 (Latency)**，O(1) 常数时间点查 |

- **EventTable**: 单一内核事件表，支持二维复合索引 `table[date, symbol][col]`、强类型点查 `table.Get<T>(date, symbol)` 与流式 LINQ `table.As<T>(date)` / `table.As<T>(symbol)` / `table.As<T>()`；支持时间单调递增日期集合 `table.Dates`；支持通过 `table[date]` 获取当日截面字典（`IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>`）与全局广播事件（`TryGetGlobal`）。
- **EventSourceFactory**: 工厂类，自动检测 CSV/Parquet 格式，通过 `EntityMapper` 表达式树预编译原生委托 `Func<string[], T>` 高速映射，支持构造函数参数名与列名无序自适应绑定，零装箱、零反射开销；支持按回测时间区间动态执行物理年份分区剪枝。
- **EventRegistry**: 事件流注册中心，统一管理事件表，支持索引器 `Events["name"]` 与 `Events.GetTable("name")`。

### 5. 标的静态属性与维表子系统 (Fields System)

管理板块、概念、行业及标的静态资料等无时间戳平铺维表（如东财行业对应表、东财概念对应表）：
- **IFieldTable**: 静态维表契约，单一内核同时提供免定义动态链式索引 `Fields["industry"][symbol]["col"]`、强类型点查 `table.Get<T>(symbol)`、强类型谓词倒排选股 `table.GetKeys<T>(predicate)`、强类型流式 LINQ `table.As<T>()` 与代码键值对流 `table.AsPairs<T>()`，支持 `table.Keys` 与 `foreach` 迭代，以及双向倒排选股池圈选与多模查询：
  - 动态全局倒排选股：`table.GetKeys("低空经济")`（省略列名，全表全列秒级检索）；
  - 动态指定列倒排选股：`table.GetKeys("钼", "board_name")`；
  - 强类型谓词选股：`table.GetKeys(x => x.BoardName == "钼")`（编译期安全保障，杜绝魔法字符串拼写错误）；
  - 1:N 便捷多值提取：`table.GetValues("sh.600000", "board_name")`；
  - 1:N 多行安全获取：`table.GetRows("sh.600000")`。
- **FieldRegistry**: 标的维表注册中心，支持 `Fields["table"]` 索引器获取维表。
- **FieldSourceFactory**: 物理维表加载工厂，支持 CSV 与 Parquet 平铺表直接映射为单一内核动态维表（`FieldTable`）。

### 6. 存储调度与批量加载 (StorageResolver & Bulk Loader)

- **IStorageResolver** (`Abstraction/Data/IStorageResolver.cs`): 基于 `tableId` 统一调度物理文件：
  - **元数据缓存**: 内部采用 `ConcurrentDictionary<string, TableMetadataCache>` 避免重复 I/O。
  - **决策机制**: 优先读取表目录下 `metadata.json`；缺失时自动由 Parquet Footer 或 CSV 表头回退推断。
  - **布局模式**:
    - `Hive`: `{root}/{table_id}/year={yyyy}/[month={mm}/]`，传入 `startDate` 与 `endDate` 执行年份/月份动态剪枝。
    - `Flat`: `{root}/{table_id}/` 平铺存储，支持直接按 symbol 精确匹配或全量枚举。
- **纵向批量加载流程**:
  1. `StorageResolver.ResolvePhysicalFiles()` 按表与日期区间返回物理文件列表。
  2. `CsvTimeseriesSource` / `ParquetTimeseriesSource` 构造时扫描文件建立代码与全局交易日并集（Schema 字段由 `IMarketMetadata` 自包含权威供给，零外部耦合）。
  3. `TimeseriesRegistry.GetMatrix<T>()` 调度 `TimeseriesCacheManager<T>`，优先检查 `ITimeseriesCache`（支持 LZ4 原生块压缩与 4096 行分块），命中时直接单块解压注入非托管连续内存返回 `ChunkMatrix2D<T>`；未命中或不兼容时从源数据加载并持久化至 `.cache/{tableId}/{fieldName}/`。内存采用定长环形页表槽位管理（`max_pages`，默认 4），通过单指令双边界检查拦截越界访问；引擎推进时通过 `StepTo` 自动滑动窗口淘汰冷块，并由后台异步预读（`prefetch`，默认 true；实盘 Living 下一票否决强行关闭）下一块。实盘单行追加由 `TimeseriesCacheManager.AppendRow` 驱动，达到 4096 行时自动执行跨页轮转（Rollover），并通过 `Commit()` 精准增量落盘活跃分块，历史分块零写放大。单标的回测（$N=1$）可通过 `provider.GetSeries<T>()` 一键获取全周期只读一维列视图。策略回溯深度通过 `IStrategy.Lookback`（默认 200）与 `IStrategyPipeline.Lookback`（子策略自动取最大值）统筹定义。
- **ReadSymbolSeries 核心机制**:
  - 方法契约：`ReadSymbolSeries(symbol, fieldName, startIndex, length, destination)`
  - CSV：定位 `{symbol}.csv` 逐行流式解析，按日期二分查找写入 `destination`，通过 `ValueConverter<T>` 零装箱转换。
  - Parquet：从 `YearCache` 预解压列直接按股票偏移批量拷贝，通过 `Unsafe.As` 零拷贝转换。

### 7. 数据层模块化物理布局 (Data Directory Structure)

`CarrotBacktesting.NET/Data/` 遵循领域高内聚（Package by Feature）与单一事实来源原则，纵向按业务子领域划分，统一归纳于 `CarrotBacktesting.NET.Data` 根命名空间：
- `Timeseries/`: 时序数据完整纵向子系统（稠密矩阵数据流）。
  - `Storage/`: 底层时序物理源驱动 (`TimeseriesSourceBase`, `CsvTimeseriesSource`, `ParquetTimeseriesSource`, `TimeseriesSourceFactory`)。
  - `Cache/`: 时序分块本地存储与内存页槽位管理 (`ITimeseriesCache`, `TimeseriesCache`, `TimeseriesCacheManager`)。
  - `Matrix/`: 连续二维非托管矩阵容器与行列切片视图 (`ChunkMatrix2D`, `MatrixExtensions`, `Views/`)。
  - `Metadata/`: 二维矩阵坐标系（代码池与日历网格）(`DynamicMarketMetadata`)。
  - `Registry/`: 统一供给门面与装配工厂 (`TimeseriesRegistry`, `TimeseriesRegistryFactory`)。
  - `Utils/`: 交易日历网格对齐与缺失值填充 (`MarketDataAligner`)。
- `Events/`: 异构事件完整纵向子系统（离散稀疏数据流）。
  - `Storage/`: 底层事件物理抽取驱动 (`CsvEventSource`, `ParquetEventSource`, `EventSourceFactory`)。
  - 核心结构：事件表 (`EventTable`)、注册中心 (`EventRegistry`) 与装配工厂 (`EventRegistryFactory`)。
- `Fields/`: 标的静态属性与维表子系统（行业/概念/分类维表与双向倒排选股）。
  - `FieldTable`: 标的维表容器（支持动态 `IDataRow` 与强类型流式映射）。
  - `FieldRegistry`: 标的维表注册中心。
  - `FieldRegistryFactory`: 标的维表注册中心装配工厂（支持从配置清单批量装配）。
  - `FieldSourceFactory`: 静态维表加载驱动（支持 CSV/Parquet 平铺表直接映射为维表）。
- `Common/`: 跨模态共享底层基础设施。
  - `StorageResolver`: 统一物理存储寻址（Hive/Flat 格式调度，TS/EV/Fields 共享）。
  - `DefaultDataColumns`: 默认标的与双时间轴基础列名契约（收敛 symbol/datetime/timestamp/date 保留字段与候选列表）。
  - `DataRow`: 轻量动态行数据结构（实现 `IDataRow`）。
  - `EntityMapper`: 通用表达式树预编译构造器（原生 IL 速度映射，零装箱，带字段上下文强类型异常防御）。
  - `CsvReaderExtensions`: CSV 流式读取通用扩展（安全探测列、列序号字典映射、混合日期解析、无装箱泛型转换）。
  - `ParquetReaderExtensions`: Parquet 列式读取通用扩展（大小写不敏感字段查找、全类型批量字符串列归一化、强类型日期列读取、Task.Run 异步隔离防死锁）。

---

## 四、引擎、策略与执行层契约

### 1. 引擎层核心契约 (`CarrotBacktesting.NET.Abstraction.Engine`)

| 契约 / 类型 | 文件 | 职责说明 |
| :--- | :--- | :--- |
| `IEngine` | `Abstraction/Engine/IEngine.cs` | 核心驱动接口，支持 `Run` / `Pause` / `Stop` 与状态快照 (`SaveState` / `RestoreState`) |
| `IEngineContext` | `Abstraction/Engine/IEngineContext.cs` | 策略运行环境，封装 `ITimeseriesRegistry`, `IBroker`, `IMarketState`, `IEventRegistry`, `IFieldRegistry` |
| `IBroker` | `Abstraction/Engine/IBroker.cs` | 账户资产与持仓管理，提供订单提交与 `OnTrade` 成交回报事件 |
| `IExchangeGateway`| `Abstraction/Engine/IExchangeGateway.cs` | 交易所物理网关抽象（屏蔽回测撮合器与实盘交易所 API） |
| `IMatchingEngine` | `Abstraction/Engine/IMatchingEngine.cs` | 回测撮合引擎，按时间步驱动撮合逻辑 |
| `IMarketState` | `Abstraction/Engine/MarketContext.cs` | 市场黑板上下文，支持策略间跨层异步状态共享 |
| `IMonitor` | `Abstraction/Engine/IMonitor.cs` | 统一监控抽象，管理日志输出、指标采集与告警 |
| **数据模型** | `Abstraction/Engine/OrderTypes.cs` | `OrderRequest` (下单意图), `ExecutionReport` (成交明细), `OrderStatus` (生命周期), `PositionEvent` |

### 2. 策略与分析层契约

| 契约 / 类型 | 命名空间 | 职责说明 |
| :--- | :--- | :--- |
| `IStrategy` | `Abstraction.Strategy` | 策略基础契约，包含 `Initial`/`Start`/`Update`/`Stop` 及订单/成交回调 |
| `IStrategyPipeline` | `Abstraction.Strategy` | 策略管线契约，支持管线编排与密封编译 |
| `ICheckpointable<T>`| `Abstraction.Strategy` | 强类型状态快照扩展，实现断点续传 |
| `IRecorder` | `Abstraction.Analysis` | 全过程流水采集（订单、成交、净值快照），通过 `GetReport()` 输出 `BacktestReport` |

---

## 五、数据存储与元数据规范 (CarrotQuant.Data)

### 1. 核心存储标准
- **存储格式**: 统一使用 `CSV` 或 `Parquet`。
- **双时间轴契约**: 必须遵循 (`timestamp` Int64 毫秒戳, `symbol` String, `datetime` String)。
- **原子写入**: 文件写入遵循“先写 `.tmp`，后原子替换 (`os.replace`)”。
- **复权口径**: 严禁使用前复权；复权数据仅限 `raw` (原始不复权) 或 `adj` (后复权)。
- **Table ID 命名规范**: `{market}.{category}.[sub_category/freq/adj].{source}`（例如 `ashare.kline.1m.raw.custom`）。
- **物理路径布局**:
  - TimeSeries (TS): `{root}/{table_id}/year={yyyy}/[month={mm}/]{symbol}.csv` 或 `data.parquet`
  - Event (EV): `{root}/{table_id}/year={yyyy}/data.{format}`
- **分区模式 (Partition)**:
  - `symbol`: 物理文件按股票代码拆分（如 `{symbol}.csv`）。
  - `none`: 无代码分区，整表单文件存储（如 `data.parquet`）。

### 2. 元数据规范 (metadata.json)
每个表目录下应提供 `metadata.json`，核心定义：
- `schema`: 显式字段类型映射。
- `statistics` (可选): 物理巡检产出的状态描述，仅用于分析辅助。

### 3. 类型映射字典 (Polars 对齐)

| Schema 声明 | .NET 类型 | Polars 类型 | 说明 |
| :--- | :--- | :--- | :--- |
| `Int64` | `long` | `pl.Int64` | 整数/时间戳 |
| `Float64` | `double` | `pl.Float64` | 浮点数值 |
| `String` | `string` | `pl.String` | 代码、文本 |
| `Boolean` | `bool` | `pl.Boolean` | 布尔状态 |
| `Date` | `DateTime` / `DateOnly` | `pl.Date` | 日期格式 |
| `Datetime` | `DateTime` | `pl.Datetime`| 日期时间 |

---

## 六、YAML 配置规范 (`CarrotBacktesting.NET.Config`)

系统采用蛇形命名（`snake_case`）YAML 文件驱动引擎与数据层；相对路径均以当前配置文件所在物理目录为基准自动合并为绝对路径。

- `engine`: 运行模式 (`mode: backtest/living`)、起止时间 (`start_date`, `end_date`)、初始资金 (`initial_cash`)。
- `data`: 数据层统一多态入口（单一事实来源）：
  - **外链独立配置**: 赋外部文件相对路径字符串（如 `data: "shared/data.yaml"`），支持多策略复用；
  - **直接内嵌配置**: 内嵌数据层映射对象，包含 `root`、`timeseries`（含 `table_id` 与可选 `fields`）、`events`（含 `name` 与 `table_id`）、`fields`（含 `name`、`table_id`、可选 `key_column` 与 `value_columns`）。
- **配置模板与完整 YAML 范例**: 详见 [docs/API.md](docs/API.md#3-yaml-配置范例)。

---

## 七、历史版本 (v3) 架构与报表规范参考

> ⚠️ **历史文档指引**: v3 版本系统规范已完整归档至 [CarrotBacktesting.NET/V3/README.md](CarrotBacktesting.NET/V3/README.md)。
> v4 重构中若涉及历史分析器、图表生成与 Excel 报表迁移，请直接查阅该归档文档。

### v3 核心机制速查索引：
1. **AnalysisContext**: 历史分析器上下文载体，提供 `SetArtifact<T>`, `SetFileArtifact`, `GetFileArtifact` 契约。
2. **插件生命周期与配置**: `IAnalyzer` / `IExporter` 通过 `Init(AnalyzerConfig/ExporterConfig)` 初始化，由 `AnalysisRunner` 队列驱动。
3. **图表工件命名**: 遵循 `Plot_{GroupName}_{PlotType}`（如 `Plot_Total_Heatmap_Signal`）。
4. **Excel 专业报表规范**:
   - 五大分区结构：`Dashboard` (总览), `[Group]_Signal` (T+N 双层表头与 Range-based 区域图表锚定), `[Group]_Monthly` (月度稳定性), `[Group]_Exit` (平仓收益分析), `All_Trades` (全量流水，通过反射将 `Trade.MarketSnapshot` 自动展开为独立列), `Market_Daily` (宏观日报)。
   - 技术深度：中文列宽自适应额外增加 2 字符缓冲区；基于单元格区域固定图表防止错位。
5. **停牌向前填充 (Forward Fill)**: 缺失交易日填充 `StockFrame`，收盘价向前沿用，成交量置 0，状态置为 `TradeStatus.Halted`。
6. **双路径并行模型**: TimeSeries（纵向个股并行）与 MarketSnapshot（横向日期截面并行）。

---

## 八、工程研发与文档维护规范

1. **单一事实来源 (SSOT & DRY)**: 核心契约与数据结构单点权威定义，严禁冗余拷贝或散落多处定义。
2. **显式优于隐式 (Explicit > Implicit)**: 优先读取 `metadata.json` 显式类型，配置与参数清晰透明，严禁未经对齐的隐式推断与静默容错。
3. **专业工程手册文案与精炼注释（严禁营销夸大与哲学浮夸）**:
   - **专业手册定位与领域中立**: 代码注释、文档与提交信息必须定位为严谨的“专业工程手册”，重点陈述技术事实、算法复杂度、物理边界与指标契约；坚决杜绝“设计哲学”、“颠覆性”、“极致强大”、“完美无瑕”、“终极方案”、“压榨性能”、“泥沙俱下”等哲学思辨、戏剧化修辞与 AI 营销套话。
   - **示例驱动与全面详实**: 文档与 API 规范必须全面、精准，关键环节（配置、数据加载、策略生命周期、订单交互）必须提供完整、清晰、可直接对照执行的代码或 YAML 范例，杜绝空泛说明。
   - **文档链接规范**: Markdown 文档内部的接口、类与路径引用严禁使用开发机绝对路径（如 `file:///`），统一采用反引号代码标记或相对路径。
   - **精炼合理的中文注释**: 注释重点阐明“设计意图 (Why)”与“边界约束 (Constraints)”，解释非直观算法或架构决策；坚决杜绝代码复述式废话（如不要在 `GetMatrix` 上写“获取 Matrix”）。
4. **文档即代码真相（实时同步）**: 任何接口、数据结构、物理路径或配置模型的调整，必须第一时间同步维护本文档（[AGENTS.md](AGENTS.md)）、API 参考手册（[docs/API.md](docs/API.md)）与 Wiki 文档（[docs/](docs/)），消除过时信息。
5. **最小改动与精准执行**: 修改遵循最小可行原则，严禁未查明根因盲目修改无关代码或无因格式重排。
   - **编辑警示**: 在使用 `edit` 工具修改代码时，请确保 `oldText` 块精准且最小化，严禁通过包含大量无关的前后文增加匹配难度，防止误伤不相关的代码区域。
6. **测试闭环与真实断言**: 代码重构与新增特性必须同步执行单元测试（`dotnet test`），严禁使用弱条件分支掩盖故障。
