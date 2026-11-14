# 数据层内存架构与供给体系 (Data Layer Architecture)

本文档面向系统架构师与策略开发者，深入介绍 CarrotQuant.NET 数据层的内存数据组织、三大内存供给子系统（时序矩阵、异构事件流、标的静态维表）、分块轮转缓存机制以及存储调度与元数据推断体系。

---

## 目录
- [一、数据层系统架构与多模态双轨设计](#一数据层系统架构与多模态双轨设计)
- [二、时序子系统 (Timeseries Subsystem)](#二时序子系统-timeseries-subsystem)
  - [1. 核心容器：ChunkMatrix2D 与切片视图](#1-核心容器chunkmatrix2d-与切片视图)
  - [2. 全局日历对齐与物理真相投影 (MarketDataAligner)](#2-全局日历对齐与物理真相投影-marketdataaligner)
  - [3. 分块持久化缓存与轮转调度 (TimeseriesCache & TimeseriesCacheManager)](#3-分块持久化缓存与轮转调度-timeseriescache--timeseriescachemanager)
  - [4. 时序注册表统一门面 (TimeseriesRegistry)](#4-时序注册表统一门面-timeseriesregistry)
  - [5. 位掩码变更感知 (IsUpdated)](#5-位掩码变更感知-isupdated)
- [三、异构事件流子系统 (Events Subsystem)](#三异构事件流子系统-events-subsystem)
  - [1. 稀疏事件核心容器：EventTable](#1-稀疏事件核心容器eventtable)
  - [2. 表达式树高性能原生映射 (EntityMapper)](#2-表达式树高性能原生映射-entitymapper)
  - [3. 事件注册表与到达探测 (EventRegistry & HasNewEvents)](#3-事件注册表与到达探测-eventregistry--hasnewevents)
- [四、标的属性与静态维表子系统 (Fields Subsystem)](#四标的属性与静态维表子系统-fields-subsystem)
  - [1. 单一内核维表容器：FieldTable](#1-单一内核维表容器fieldtable)
  - [2. 多模式查询与全局倒排选股](#2-多模式查询与全局倒排选股)
  - [3. 维表注册中心 (FieldRegistry)](#3-维表注册中心-fieldregistry)
- [五、存储调度与元数据推断 (StorageResolver & TableMetadata)](#五存储调度与元数据推断-storageresolver--tablemetadata)
  - [1. 不可变元数据模型 (TableMetadata)](#1-不可变元数据模型-tablemetadata)
  - [2. 无状态流式样本推断 (TableMetadataInferer)](#2-无状态流式样本推断-tablemetadatainferer)
  - [3. 物理寻址与直通优化 (StorageResolver)](#3-物理寻址与直通优化-storageresolver)

---

## 一、数据层系统架构与多模态双轨设计

在量化投研系统中，数据形态天然具有多模态特征：
1. **稠密高频时序数据**：如行情 K 线（OHLCV），具备固定周期、规整时间轴、全市场标的多维度的特征，计算密集且极度依赖连续内存向量化；
2. **稀疏离散事件数据**：如分红配股、龙虎榜、业绩预告、停牌，发生频次低、时间不规则、字段异构；
3. **平铺静态维表数据**：如概念板块、行业分类、标的基础资料，无时间轴，呈现 1:1、1:N 或 N:1 关系。

为保证高吞吐计算与低延迟点查，CarrotQuant.NET 采用**多模态双轨架构**，将稠密矩阵与稀疏事件物理分离：

| 特性 | **时序子系统 (ITimeseriesRegistry)** | **异构事件子系统 (IEventRegistry)** | **静态维表子系统 (IFieldRegistry)** |
| :--- | :--- | :--- | :--- |
| **内存形态** | 非托管连续物理块 (`ChunkMatrix2D<T>`) | 复合字典树 (`Dictionary<DateTime, ...>`) | 标的平铺哈希桶与双向倒排索引 |
| **访问范式** | 二维坐标 `(row, col)`、行/列切片视图 | 二维复合点查 `[date, symbol]`、LINQ 流 | 键值链式索引 `[symbol]["col"]`、倒排反查 |
| **应用场景** | 向量化特征计算、滑动均线、横向排序调仓 | 决策分支、复权计算、事件驱动交易 | 标的池初筛、板块分类过滤、品种乘数查询 |
| **对齐要求** | **强对齐**（全市场标的 $\times$ 全局交易日历） | **弱对齐**（按发生时记录，无空位补齐） | **无对齐**（标的静态快照） |
| **性能目标** | **高吞吐 (Throughput)**，L1/L2 预取友好，零 GC | **低延迟 (Latency)**，$O(1)$ 常数时间点查 | **高表达 (Expressive)**，动态免定义与强类型双模 |

---

## 二、时序子系统 (Timeseries Subsystem)

时序子系统负责向策略层提供全市场标的高性能 K 线时序数据。

### 1. 核心容器：ChunkMatrix2D 与切片视图
`ChunkMatrix2D<T>` 是时序数据的底层核心容器（`T : unmanaged`），统一继承自 `IReadOnlyMatrix2D<T>`：

```
       Symbol 0    Symbol 1    ...    Symbol M-1
Row 0   [ val ]     [ val ]    ...     [ val ]   <── ReadOnlyRowView<T> (横向截面快照)
Row 1   [ val ]     [ val ]    ...     [ val ]
 ...      ...         ...      ...       ...
Row N-1 [ val ]     [ val ]    ...     [ val ]
           ▲
           └────────────── ReadOnlyColumnView<T> (纵向时序序列)
```

- **非托管连续内存**：底层通过 `NativeMemory.AllocZeroed` 或连续物理块管理，内存布局紧凑，严格规避 .NET 托管堆 GC 开销；
- **消除 JIT 边界检查 (BCE)**：行切片 `ReadOnlyRowView<T>` 与列切片 `ReadOnlyColumnView<T>` 基于指针或固定跨度索引提供 `ref readonly T this[int index]`，提速 20%~30%；
- **标的维度单调递增 (SSOT)**：标的代码对应的列索引（`SymbolIndex`）单调递增，已有列索引绝不修改；退市股票保留列位置并填充 NaN，彻底消除幸存者偏差。

### 2. 全局日历对齐与物理真相投影 (MarketDataAligner)
原始行情源由于标的停牌、上市时间差异，其物理文件时间戳是参差不齐的。`MarketDataAligner` 负责将离散数据投射到全局交易日历网格：
- **物理事实投影**：物理文件存在的交易记录原样写入对应网格单元格；
- **标准哨兵填充**：物理缺失的单元格（停牌、未上市或退市）统一填充 IEEE 754 标准 `NaN`（整型使用 `long.MinValue` / `int.MinValue` 作为哨兵）；
- **停牌前置契约**：回测系统绝不在底层隐式猜数据或执行向前沿用（`ffill`）。停牌价格沿用必须由上游 ETL 宽表明确处理，数据层严格反映物理数据真相。

### 3. 分块持久化缓存与轮转调度 (TimeseriesCache & TimeseriesCacheManager)
- **4096 行物理分块与 LZ4 块压缩**：针对长周期大体量数据，`TimeseriesCache` 将时序按 4096 行切块（Bin），每个分块独立自描述其包含的列宽（Variable Width per Bin）；
- **零写放大增量追加**：在实盘或增量回测中，调用 `AppendRow` 追加数据时，仅当前活跃分块标记为脏并执行磁盘落盘（`Commit`），历史已固化的分块保持只读，彻底消除全量重写带来的写放大；
- **动态列宽自动拓展**：当新增标的接入时，旧分块载入内存后自动补齐新列宽并填充 NaN，存储空间零膨胀。

### 4. 时序注册表统一门面 (TimeseriesRegistry)
`TimeseriesRegistry` 实现 `ITimeseriesRegistry` 只读契约与 `ITimeseriesWriter` 写入契约，内部聚合各字段的 `TimeseriesCacheManager<T>`：
- **只读输出保障**：`GetMatrix<T>(fieldName)` 必须返回 `IReadOnlyMatrix2D<T>`，禁止外部篡改；
- **单标的时序便利获取**：针对单标的回测或基准指数对比，提供 `GetSeries<T>(fieldName, symbol)`，直接返回高效的一维只读列视图（`ReadOnlyColumnView<T>`）。

### 5. 位掩码变更感知 (IsUpdated)
引擎推进主时钟时，为了在策略层以位掩码常数时间判定各时序表当前步是否存在有效更新：
- 采用 64 位无符号整数 `_updatedMask` 记录更新状态；
- `IsUpdated(int tableIndex)` 通过单条 CPU 寄存器位运算 `(_updatedMask & (1UL << tableIndex)) != 0` 完成判定，零 GC、零装箱；
- 序号容量防护：分配表索引时若 `tableIndex >= 64`，显式抛出 `NotSupportedException` 防御状态穿透。

---

## 三、异构事件流子系统 (Events Subsystem)

针对复权因子、分红派息、龙虎榜明细、停牌事件等弱对齐或异构数据，事件子系统采用独立的扁平模型。

### 1. 稀疏事件核心容器：EventTable
`EventTable` 为单一内核事件表，同时提供动态无类型与强类型实体流访问：
- **二维复合索引**：`table[date, symbol][col]` 免定义即时点查；
- **当日截面字典**：`table[date]` 返回当日全部发生事件的标的字典（`IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>`），遍历时无空跑标的；
- **全局广播事件**：通过 `table.TryGetGlobal(date, out var globalRows)` 获取全市场宏观事件（如降息降准、大盘公告）；
- **时间单调递增集合**：`table.Dates` 返回所有发生过事件的去重日期集合，支持时间区间快速定位。

### 2. 表达式树高性能原生映射 (EntityMapper)
为了消除传统 ORM 或反射在百万级事件解析时的性能损耗：
- **表达式树编译委托**：`EntityMapper` 动态解析目标 Record/Class 的构造函数参数名，在运行时动态编译生成原生委托 `Func<string[], T>`；
- **无序自适应绑定**：CSV/Parquet 的列顺序与模型构造函数参数顺序无关，自动按名称映射并内联类型转换代码；
- **零装箱、零反射开销**：编译后的委托与手写纯 C# 代码执行效率完全相同。

### 3. 事件注册表与到达探测 (EventRegistry & HasNewEvents)
- **统一注册管理**：`EventRegistry` 支持索引器 `Events["tableName"]` 获取特定事件表；
- **常数时间到达轻量探测**：
  ```csharp
  bool hasEvents = events.HasNewEvents(currentTime, "dragon_tiger");
  ```
  在主时钟推进时，$O(1)$ 时间探测当前时间戳是否存在新事件到达，未命中时直接跳过后续事件处理，避免无意义的遍历损耗。

---

## 四、标的属性与静态维表子系统 (Fields Subsystem)

管理板块成分、东财行业归属、合约规格乘数等无时间轴平铺资料维表。

### 1. 单一内核维表容器：FieldTable
`FieldTable` 实现 `IFieldTable` 契约，支持动态弱类型、强类型 Record 与双向倒排索引：
```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public interface IFieldTable : IEnumerable<KeyValuePair<string, IReadOnlyList<IDataRow>>>
{
    string Name { get; }
    int Count { get; }
    IEnumerable<string> Keys { get; }
    IDataRow this[string key] { get; }

    bool TryGetRow(string key, out IDataRow? row);
    bool TryGetRows(string key, out IReadOnlyList<IDataRow> rows);
    IReadOnlyList<IDataRow> GetRows(string key);
    IReadOnlyList<string> GetValues(string key, string column);
    IReadOnlyList<string> GetKeys(string value, string? column = null);

    T Get<T>(string key) where T : class;
    bool TryGet<T>(string key, out T? value) where T : class;
    IReadOnlyList<string> GetKeys<T>(Func<T, bool> predicate) where T : class;
    IEnumerable<T> As<T>() where T : class;
    IEnumerable<KeyValuePair<string, T>> AsPairs<T>() where T : class;
}
```

### 2. 多模式查询与全局倒排选股
- **动态链式索引**：`fields["industry"]["sh.600000"]["board_name"]`；
- **1:N 多值提取**：`fields["concept"].GetValues("sh.600000", "board_name")`；
- **全局倒排选股 (省略列名)**：
  ```csharp
  var pool = fields["concept"].GetKeys("低空经济"); // 基于倒排索引直接返回标的池
  ```
- **强类型编译期安全选股**：
  ```csharp
  public record IndustryProfile(string BoardName, string BoardCode);
  var stocks = fields["industry"].GetKeys<IndustryProfile>(x => x.BoardName == "钼");
  ```
  具有强类型语法保障与 IDE 自动补全，彻底消除字符串拼写错误风险。

### 3. 维表注册中心 (FieldRegistry)
通过统一门面 `Fields["tableId"]` 提供对全部静态维表的集中索引，纯单向被 `EngineContext` 持有，供策略层及经纪商 Hook 查询。

---

## 五、存储调度与元数据推断 (StorageResolver & TableMetadata)

`StorageResolver` 担当从物理磁盘数据文件到内存数据供给层之间的权威桥梁。

### 1. 不可变元数据模型 (TableMetadata)
`TableMetadata` 是数据表元数据的**单一事实来源（SSOT）**，自包含以下不可变属性：
- `TableId`: 点分命名规范标识符；
- `StorageFormat`: `Csv` 或 `Parquet`；
- `StorageLayout`: `Hive` 分区布局或 `Flat` 单目录布局；
- `Category`: `Timeseries`、`Event` 或 `Field`；
- `PartitionMode`: `Symbol` 拆分或 `None` 单文件；
- `Schema`: 强类型字段映射；
- `NullableFields`: 允许放行空值的白名单集合。

### 2. 无状态流式样本推断 (TableMetadataInferer)
当物理表目录下缺失 `metadata.json` 时：
- `TableMetadataInferer` 作为无状态静态纯函数，流式嗅探首个样本数据文件（CSV 仅读首行表头与前 5 行样本，Parquet 读取 FileMetaData 的 Schema）；
- 结合命名规范（如 Table ID 包含 `.kline.` 则推断为 Timeseries，包含 `.concept.` 则推断为 Field）完成缺省推断；
- 用完即走，不保留任何内部实例状态。

### 3. 物理寻址与直通优化 (StorageResolver)
- **双缓存隔离**：内部独立维护 `_metadataCache`（元数据字典）与 `_topologyCache`（物理目录拓扑结构字典）；随实例销毁被 GC 安全回收，退出零脏状态；
- **直通优化 (Bypass Hash Lookup)**：数据源底层（如 `ParquetTimeseriesSource`）直接持有不可变 `TableMetadata` 实例，在冷块加载调用 `ResolvePhysicalFiles(_metadata, ...)` 时，直通跳过字典哈希寻址，将物理寻址耗时降为 $O(1)$；
- **年份分区剪枝 (Partition Pruning)**：当回测指定了起止时间时，仅扫描命中年份的分区目录，大幅缩减冷启动耗时。
