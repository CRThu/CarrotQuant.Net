# CarrotQuant .NET API 参考手册 (API Reference)

本文档面向策略研发工程师与框架二次开发者，作为底层接口字典收录 CarrotQuant.NET v4 的核心契约签名与数据模型。

> [!TIP]
> **业务原理与使用指南**：若需了解端到端回测流程与配置范例，请参阅 [标准研发工作流 (workflow.md)](workflow.md) 及 [配置体系指南 (configuration.md)](configuration.md)。

---

## 目录
- [一、配置层 (CarrotBacktesting.NET.Config)](#一配置层-carrotbacktestingnetconfig)
- [二、存储调度与数据物理层 (Abstraction.Data & Data)](#二存储调度与数据物理层-abstractiondata--data)
- [三、内存数据供给层 (ITimeseriesRegistry & ChunkMatrix2D)](#三内存数据供给层-itimeseriesregistry--chunkmatrix2d)
- [四、异构事件流系统 (Event Stream System)](#四异构事件流系统-event-stream-system)
- [五、标的静态属性与维表系统 (Fields System)](#五标的静态属性与维表系统-fields-system)
- [六、引擎与黑板上下文 (Engine & Execution Context)](#六引擎与黑板上下文-engine--execution-context)
- [七、独立指标与计算属性子系统 (Indicators & Computed Properties)](#七独立指标与计算属性子系统-indicators--computed-properties)
- [八、策略与管线开发 (Strategy & Pipeline)](#八策略与管线开发-strategy--pipeline)
- [九、交易事实沉淀与分析层 (Analysis & Recorder)](#九交易事实沉淀与分析层-analysis--recorder)

---

## 一、配置层 (CarrotBacktesting.NET.Config)

### 1. SessionConfigLoader
配置加载入口静态类，负责将 YAML 文件反序列化为强类型配置对象并完成文件路径的层级解析。

```csharp
namespace CarrotBacktesting.NET.Config;

public static class SessionConfigLoader
{
    /// <summary>
    /// 从指定 YAML 配置文件加载，自动完成多态路径外链与相对路径归一化。
    /// </summary>
    /// <param name="configFilePath">YAML 配置文件物理路径。</param>
    /// <returns>规范化后的完整 SessionConfig 实例。</returns>
    /// <exception cref="ArgumentException">路径为空时抛出。</exception>
    /// <exception cref="FileNotFoundException">主文件或引用的外部数据配置文件不存在时抛出。</exception>
    public static SessionConfig Load(string configFilePath);
}
```

### 2. 配置模型
- **`SessionConfig`**: 顶层聚合配置四大支柱：`EngineConfig Engine`、`BrokerConfig Broker`、`DataConfig Data` 与可选的 `PipelineConfiguration? Pipeline`。
- **`EngineConfig`**: 引擎自身参数：`Mode` (`backtest`/`living`)、`StartDate` (DateTime?)、`EndDate` (DateTime?)。
- **`BrokerConfig`**: `InitialCash` (double)、`SettleDays` (int: 0 即 T+0, 1 即 T+1)、`AllowShort` (bool)、`FeeRate` (double)、`MinFee` (double)、`Slippage` (double)、`VolumeLimit` (double)、`CheckLimit` (bool)、`Multiplier` (double)、`MarginRate` (double)，对称覆盖 Hook，以及内置静态市场预设（`Universal`, `AStock`, `Futures`, `Crypto`）。
- **`DataConfig`**: `Root` (string)、`Timeseries` (`List<TimeseriesConfig>`)、`Events` (`List<EventStreamConfig>`)、`Fields` (`List<FieldTableConfig>`)、`Adjust` (`AdjustmentConfig`)。
- **`AdjustmentConfig`**: `Mode` (`AdjustmentMode`: `None` / `Post`)、`TableId` (string: 默认 "adjustments")、`FactorField` (string: 默认 "back_adj_factor")、`PriceFields` (`List<string>`: 默认 ["open", "high", "low", "close"])、`Cache` (bool: 默认 true)。
- **`TimeseriesConfig`**: `TableId` (string)、`Fields` (`List<string>?`)、`TimeZone` (string?)、`Cache` (bool)、`PageSize` (int)、`Refresh` (`CacheRefreshMode`: `Lazy` / `Auto` / `Force`)、`Compression` (`CacheCompression`: `Lz4` / `None`)、`CommitBatchSize` (int)、`MaxPages` (int)、`Prefetch` (bool)、`NullableFields` (`List<string>?`)。
- **`EventStreamConfig`**: `Name` (string)、`TableId` (string)、`Fields` (`List<string>?`)、`TimeZone` (string?)、`NullableFields` (`List<string>?`)。
- **`FieldTableConfig`**: `Name` (string)、`TableId` (string)、`KeyColumn` (string?)、`ValueColumns` (`List<string>?`)、`NullableFields` (`List<string>?`)。

> 详细参数字典、类型、默认值与必填约束详见 [配置参数全量字典 (configuration.md)](configuration.md#二配置参数全量字典-parameter-reference)。

#### 调用范例
```csharp
// 加载策略主配置（自动识别并合并外链的 shared/data.yaml 与 pipeline.yaml）
SessionConfig config = SessionConfig.Load("configs/strategy_cta.yaml");

Console.WriteLine($"回测模式: {config.Engine.Mode}, 初始资金: {config.Broker.InitialCash}, 结算: T+{config.Broker.SettleDays}");
Console.WriteLine($"数据根目录: {config.Data.Root}");
```

### 3. YAML 配置范例

#### 模式 A：直接内嵌模式 (Inline)
```yaml
engine:
  mode: "backtest"            # backtest / living
  start_date: "2025-01-01"    # 回测起始时间
  end_date: "2025-03-31"      # 回测结束时间

broker:
  initial_cash: 1000000.0     # 初始资金
  settle_days: 1              # 结算天数 (0 即 T+0, 1 即 T+1)
  fee_rate: 0.0003            # 手续费率
  min_fee: 5.0                # 最低手续费
  slippage: 0.001             # 滑点

data:
  root: "./data"              # 数据存储根目录
  timeseries:
    - table_id: "ashare.kline.1m.raw.custom"
      fields: ["open", "high", "low", "close", "volume"]
  adjust:
    mode: "post"              # none / post
    table_id: "ashare.adj_factor.baostock"
  events:
    - name: "adjustments"
      table_id: "ashare.adj_factor.baostock"
  fields:
    - name: "industry"
      table_id: "ashare.industry.eastmoney"
      key_column: "symbol"
      value_columns: ["board_name"]
    - name: "concept"
      table_id: "ashare.concept.eastmoney"
      key_column: "symbol"
      value_columns: ["board_name"]
```

#### 模式 B：外链复用独立配置 (Linked / External)
- **主策略配置文件 (`strategy.yaml`)**:
  ```yaml
  engine:
    mode: "backtest"
    start_date: "2025-01-01"
    end_date: "2025-03-31"

  broker:
    initial_cash: 2000000.0
    settle_days: 1

  data: "shared/data.yaml"    # 外部数据配置文件路径
  ```
- **共享数据配置文件 (`shared/data.yaml`)**:
  ```yaml
  root: "../data_store"
  timeseries:
    - table_id: "ashare.kline.1m.raw.custom"
      fields: ["open", "close", "volume"]
  adjust:
    mode: "post"
    table_id: "ashare.adj_factor.baostock"
  events:
    - name: "adjustments"
      table_id: "ashare.adj_factor.baostock"
  fields:
    - name: "industry"
      table_id: "ashare.industry.eastmoney"
      key_column: "symbol"
      value_columns: ["board_name"]
  ```

---

## 二、存储调度与数据物理层 (Abstraction.Data & Data)

### 1. IStorageResolver 与 StorageResolver
数据区物理存储调度与元数据注册中心单一内核，负责管理指定 `StorageRoot` 内的多表元数据解析缓存、物理文件寻址与 Hive 年月分区动态剪枝。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public enum StorageFormat { Parquet, Csv }
public enum StorageLayout { Hive, Flat }
public enum TableCategory { Timeseries, Event, Field }
public enum PartitionMode { None, Symbol }

/// <summary>
/// 数据表不可变元数据聚合模型（单一事实来源 SSOT）。
/// </summary>
public sealed class TableMetadata
{
    public string TableId { get; }
    public string TableDir { get; }
    public string? MetadataPath { get; }
    public StorageFormat Format { get; }
    public StorageLayout Layout { get; }
    public TableCategory Category { get; }
    public PartitionMode Partition { get; }
    public IReadOnlySet<string> NullableFields { get; }
    public IReadOnlyList<string> FieldNames { get; }
    public IReadOnlyDictionary<string, Type> FieldTypes { get; }
    public string FileExtension => Format == StorageFormat.Parquet ? "parquet" : "csv";
    public Type GetFieldType(string fieldName);
}

/// <summary>
/// 存储调度与元数据统一瘦契约。
/// </summary>
public interface IStorageResolver
{
    string StorageRoot { get; }
    bool HasTable(string tableId);
    
    /// <summary>
    /// 获取指定表的权威元数据聚合模型（单点权威缓存）。
    /// </summary>
    TableMetadata GetMetadata(string tableId);

    /// <summary>
    /// 基于表 ID 解析物理数据文件列表，支持按代码路由与起止日期分区剪枝。
    /// </summary>
    IReadOnlyList<string> ResolvePhysicalFiles(
        string tableId, 
        string? symbol = null, 
        DateTime? startDate = null, 
        DateTime? endDate = null);

    /// <summary>
    /// 直通重载：直接基于不可变表元数据解析物理文件列表，跳过字典哈希查找实现 O(1) 路由。
    /// </summary>
    IReadOnlyList<string> ResolvePhysicalFiles(
        TableMetadata metadata, 
        string? symbol = null, 
        DateTime? startDate = null, 
        DateTime? endDate = null);
}
```

#### 调用范例
```csharp
var resolver = new StorageResolver("D:/QuantData");

// 1. 获取强类型不可变元数据聚合实体
TableMetadata metadata = resolver.GetMetadata("ashare.kline.1d.raw.baostock");
Console.WriteLine($"格式: {metadata.Format}, 布局: {metadata.Layout}, 字段数: {metadata.FieldNames.Count}");

// 2. 直通解析物理文件（执行 Hive 年份/月份动态剪枝）
var files = resolver.ResolvePhysicalFiles(
    metadata: metadata,
    startDate: new DateTime(2024, 1, 1), 
    endDate: new DateTime(2024, 12, 31)
);
```

### 2. IMarketMetadata
市场全局维度元数据契约，负责定义股票列索引与交易日行索引的对应关系。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public interface IMarketMetadata
{
    IReadOnlyList<string> Symbols { get; }            // 列维度，顺序对应列索引
    IReadOnlyList<DateTime> TradeDates { get; }       // 行维度，顺序对应行索引
    IReadOnlyList<string> FieldNames { get; }
    Type GetFieldType(string fieldName);
    int GetSymbolIndex(string symbol);                // O(1) 或二分查找
    int GetDateIndex(DateTime date);                  // O(1) 或二分查找
    int AppendDate(DateTime date);                    // 实盘时序原子推进（默认抛出 NotSupportedException）
    int AppendSymbol(string symbol);                  // 新标的上市原子扩列（默认抛出 NotSupportedException）
}
```

### 3. 数据抽取源契约
- **`ITimeseriesSource`**: 纵向代码序列读取器契约。
  ```csharp
  void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged;
  ```
- **`TimeseriesSourceBase`**: 抽象基类（模板方法模式），收敛参数边界检查、ArrayPool 内存池租借与自适应翻倍扩容、时间戳排序、以及 `MarketDataAligner.AlignAndFill` 全局交易日历投射与 NaN 哨兵填充管线。
  - `CsvTimeseriesSource`: 继承基类，遵循 `PartitionMode.Symbol` 强制规范（`{symbol}.csv`），标的代码 100% 权威绑定物理文件名，基于 Sylvan.Data.Csv 流式解析提供高性能无装箱行提取。
  - `ParquetTimeseriesSource`: 继承基类，基于 Parquet 列式存储与 YearCache 列缓存提供列切片按需提取。
- **`TimeseriesSourceFactory`**: 时序数据源工厂类，依据 `TableMetadata.Format` 强类型枚举自动派发构建对应数据源：
  ```csharp
  // 方式一：基于物理根目录快速构建
  ITimeseriesSource source = TimeseriesSourceFactory.Create(storageRoot, tableId);

  // 方式二：基于已加载的元数据与调度器精准装配
  ITimeseriesSource source = TimeseriesSourceFactory.Create(metadata, resolver);
  ```

> [!NOTE]
> **快照与时序的统一供给**：CarrotQuant.NET v4 废除了独立的流式 `IMarketSnapshotSource` 接口，全市场行情统一由 `IReadOnlyMatrix2D<T>`（`ChunkMatrix2D<T>`）提供：横向全市场快照直接通过行切片 `GetRowView(dateIndex)` 消费，纵向个股时序通过列切片 `GetColumnView(symbolIndex)` 消费。

---

## 三、内存数据供给层 (ITimeseriesRegistry & ChunkMatrix2D)

### 1. ITimeseriesRegistry, ITimeseriesScheduler & ITimeseriesWriter (CQRS 读写分离)
数据层向引擎与策略提供只读数据访问契约（`ITimeseriesRegistry`），向引擎主时钟提供槽位调度契约（`ITimeseriesScheduler`），向实盘数据录入与持久化提供写入契约（`ITimeseriesWriter`）。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 纯只读数据契约（面向策略与回测消费，零调度污染）。
/// </summary>
public interface ITimeseriesRegistry
{
    IMarketMetadata Metadata { get; }
    bool HasField(string fieldName);
    
    /// <summary>
    /// 获取指定字段的全局二维只读时序矩阵（Row=交易日/时间步，Col=股票代码）。
    /// </summary>
    IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged;
}

/// <summary>
/// 时序调度驱动契约（面向引擎主时钟，用于滑动分块内存槽位与异步预读）。
/// </summary>
public interface ITimeseriesScheduler
{
    void StepTo(int currentLogicalRow);
}

/// <summary>
/// 写入与持久化提交契约（面向实盘引擎与数据录入）。
/// </summary>
public interface ITimeseriesWriter
{
    void AppendRow<T>(string fieldName, DateTime timestamp, ReadOnlySpan<T> rowValues) where T : unmanaged;
    void AppendCol(string symbol);
    void Commit();
}
```

### 2. ChunkMatrix2D<T>
基于连续定长分页（Chunk）的高性能二维无锁只读/可写内存矩阵。底座采用非托管内存管理（`NativeMemory`），寻址全面采用位运算（Shift/Mask）实现零除法开销；读取路径提供无锁（Lock-Free）零并发竞争保证。

```csharp
namespace CarrotBacktesting.NET.Data;

public sealed unsafe class ChunkMatrix2D<T> : IMatrix2D<T>, IReadOnlyMatrix2D<T>, IDisposable 
    where T : unmanaged
{
    public int RowCount { get; }
    public int BaseRowIndex { get; }
    public int MaxPages { get; }
    public int Width { get; }
    public int PageSize { get; }
    public int PageCount { get; }

    public ChunkMatrix2D(int width, int pageSize = 4096, int initialRowCount = 0, int maxPages = 0);

    // 读写索引器（无锁快速通道）
    public ref T this[int r, int c] { get; }
    public IReadOnlyMatrix2D<T> AsReadOnly();

    // 批量向量化 I/O
    public void SetElement(int r, int c, T value);
    public void SetRow(int r, int c, ReadOnlySpan<T> data);
    public void SetColumn(int r, int c, ReadOnlySpan<T> data);
    public void CopyRow(int r, int c, Span<T> destination);
    public void CopyColumn(int r, int c, Span<T> destination);

    // 切片视图
    public RowView<T> GetRowView(int row, int col, int len);
    public ColumnView<T> GetColumnView(int row, int col, int len);

    // 块级物理连续切片（用于高速持久化零拷贝传输）
    public Span<T> GetPageSpan(int pageIndex, int rowsInPage);
    public ReadOnlySpan<T> GetReadOnlyPageSpan(int pageIndex, int rowsInPage);

    public void EnsureCapacity(int targetRowCount);
    public void AdvanceBaseRow(int newBaseRowIndex);
    public void ExpandWidth(int newWidth);
    public void Dispose();
}
```

### 3. 核心访问模式
`ITimeseriesRegistry.GetMatrix<T>` 返回 `IReadOnlyMatrix2D<T>`，支持无 GC 压力的批量截面切片与序列点查：

```csharp
// 1. 获取收盘价时序矩阵（全市场二维宽表）
IReadOnlyMatrix2D<double> closeMatrix = provider.GetMatrix<double>("close");

// 2. 根据元数据索引定位物理坐标
int dateIdx = provider.Metadata.GetDateIndex(new DateTime(2025, 1, 15));
int symbolIdx = provider.Metadata.GetSymbolIndex("sh.600000");

// 3. 常数时间点查 (O(1))
double closePrice = closeMatrix[dateIdx, symbolIdx];

// 4. 行切片（当日全市场截面，零拷贝）
ReadOnlyRowView<double> marketSnapshot = closeMatrix.GetRowView(dateIdx, 0, closeMatrix.Width);

// 5. 列切片（单只个股全时序序列，零拷贝）
ReadOnlyColumnView<double> stockHistory = closeMatrix.GetColumnView(0, symbolIdx, closeMatrix.RowCount);

// 6. 单标的回测退化便捷语法（CTA、期货、加密货币单币回测）
ReadOnlyColumnView<double> singleStockSeries = provider.GetSeries<double>("close");
ReadOnlyColumnView<double> btcSeries = provider.GetSeries<double>("close", "BTCUSDT");
```

### 4. 时序缓存存储体系 (ITimeseriesCache, TimeseriesIndexStore, TimeseriesFieldStore & TimeseriesCache)
基于 4096 行分块与原生 LZ4 块压缩的时序二进制持久化存储体系。采用单一职责与分层隔离设计：
- **TimeseriesIndexStore**：专职负责表级双标尺索引（`symbols.idx` 纯文本标的代码词表与 `time.idx` 纯二进制 8 字节 `MarketTime` 绝对时间戳轴）的单一事实维护、原子写入、前缀单调扩充与幂等保护；
- **TimeseriesFieldStore**：专职负责单字段特征分块文件（`chunk_{i}.bin`）与自描述元数据（`meta.json`）的存储管理、LZ4 高速块流式解包、单字段私有锁隔离与列自动拓宽；
- **TimeseriesCache**：统一精炼门面（Facade），编排 `TimeseriesIndexStore` 与 `TimeseriesFieldStore`，完整实现 `ITimeseriesCache` 契约。

```csharp
namespace CarrotBacktesting.NET.Data;

public interface ITimeseriesCache
{
    string TableId { get; }
    int PageSize { get; }

    bool HasField(string fieldName);
    TimeseriesMetadata? ReadMetadata(string fieldName);
    bool IsCompatible(string fieldName, IReadOnlyList<string> expectedSymbols, Type expectedType, out TimeseriesMetadata? metadata);

    ChunkMatrix2D<T> LoadMatrix<T>(string fieldName, int? targetWidth = null) where T : unmanaged;
    bool TryReadChunk<T>(string fieldName, int pageIndex, Span<T> destination, out int rowsRead) where T : unmanaged;
    void WriteMatrix<T>(string fieldName, ChunkMatrix2D<T> matrix, IReadOnlyList<string> symbols, IReadOnlyList<MarketTime> tradeDates) where T : unmanaged;
    void RewriteChunk<T>(string fieldName, int pageIndex, Action<int, Span<T>> rowProvider, int? newChunkWidth = null, IReadOnlyList<string>? updatedSymbols = null) where T : unmanaged;
    void AppendRows<T>(string fieldName, int deltaRows, Action<int, Span<T>> rowProvider, IReadOnlyList<MarketTime> newTradeDates) where T : unmanaged;
    void Clear(string fieldName);
    void ClearTable();

    string GetTableDir();
    string GetSymbolsIndexPath();
    string GetTimeIndexPath();
    IReadOnlyList<string> ReadSymbols();
    IReadOnlyList<MarketTime> ReadTradeDates();
    void WriteSymbols(IReadOnlyList<string> symbols);
    void WriteTradeDates(IReadOnlyList<MarketTime> tradeDates);
    void AppendTradeDates(IReadOnlyList<MarketTime> newTradeDates, int targetRowCount = -1);
}

public class TimeseriesIndexStore
{
    public const string SymbolsIndexFileName = "symbols.idx";
    public const string TimeIndexFileName = "time.idx";
    public TimeseriesIndexStore(string storeRoot, string tableId);
    public IReadOnlyList<string> ReadSymbols();
    public void WriteSymbols(IReadOnlyList<string> symbols);
    public IReadOnlyList<MarketTime> ReadTradeDates();
    public void WriteTradeDates(IReadOnlyList<MarketTime> tradeDates);
    public void AppendTradeDates(IReadOnlyList<MarketTime> newTradeDates, int targetRowCount = -1);
    public void ClearTable();
}

public class TimeseriesFieldStore
{
    public TimeseriesFieldStore(string storeRoot, string tableId, string fieldName, int pageSize, CacheCompression compression, TimeseriesIndexStore indexStore);
    public bool HasField();
    public TimeseriesMetadata? ReadMetadata();
    public void SaveMetadata(TimeseriesMetadata meta);
    public bool IsCompatible(IReadOnlyList<string> expectedSymbols, Type expectedType, out TimeseriesMetadata? metadata);
    public ChunkMatrix2D<T> LoadMatrix<T>(int? targetWidth = null) where T : unmanaged;
    public void WriteMatrix<T>(ChunkMatrix2D<T> matrix, IReadOnlyList<string> symbols, IReadOnlyList<MarketTime> tradeDates) where T : unmanaged;
    public void AppendRows<T>(int deltaRows, Action<int, Span<T>> rowProvider, IReadOnlyList<MarketTime> newTradeDates) where T : unmanaged;
    public void RewriteChunk<T>(int pageIndex, Action<int, Span<T>> rowProvider, int? newChunkWidth = null, IReadOnlyList<string>? updatedSymbols = null) where T : unmanaged;
    public bool TryReadChunk<T>(int pageIndex, Span<T> destination, out int rowsRead) where T : unmanaged;
    public void Clear();
}

public class TimeseriesCache : ITimeseriesCache
{
    public TimeseriesIndexStore IndexStore { get; }
    public TimeseriesFieldStore GetFieldStore(string fieldName);
    public TimeseriesCache(string storeRoot, string tableId, int pageSize = 4096, CacheCompression compression = CacheCompression.Lz4);
}
```

### 5. TimeseriesCacheManager<T> 与 TimeseriesRegistry
时序连续分块状态机与高性能供给门面：
- **TimeseriesCacheManager<T>**: 单表单字段连续多分块调度器，管理非托管内存矩阵与磁盘分块持久化存储间的分块挂载、实盘单行追加与跨页分块轮转（Rollover，每 4096 行开辟新块），以及精准增量提交（`Commit`，仅落盘活跃分块，历史分块零写放大）。
- **TimeseriesRegistry**: 满足 `ITimeseriesRegistry` 契约的高性能数据门面。遵循单一职责原则，底层存储与分块工厂完全外包隔离，对内纯粹持有并协调各字段的 `TimeseriesCacheManager`，对外提供只读二维时序矩阵（`GetMatrix<T>`）、分块调度器检索（`GetCacheManager`）与原子落盘（`Commit`）。

```csharp
namespace CarrotBacktesting.NET.Data;

public sealed class TimeseriesCacheManager<T> : ITimeseriesCacheManager where T : unmanaged
{
    public string FieldName { get; }
    public Type DataType { get; }
    public int TotalRows { get; }
    public int ActivePageIndex { get; }
    public int ActivePageRowCount { get; }
    public bool IsDirty { get; }
    public int UncommittedRowCount { get; }
    public int MaxPages { get; }
    public int BaseRowIndex { get; }
    public ChunkMatrix2D<T> Matrix { get; }

    public IReadOnlyMatrix2D<T> AsReadOnly();
    public void EnsureLoaded();
    public void StepTo(int currentLogicalRow);
    public void AppendRow(DateTime timestamp, ReadOnlySpan<T> rowValues);
    public void AppendRow(ReadOnlySpan<T> rowValues); // 兼容重载
    public void AppendCol(string symbol);
    public void UpdateRow(int rowIndex, ReadOnlySpan<T> rowValues);
    public void MarkDirty();
    public void Commit();
    public void Dispose();
}

public class TimeseriesRegistry : ITimeseriesRegistry, ITimeseriesScheduler, ITimeseriesWriter, IDisposable
{
    public IMarketMetadata Metadata { get; }
    public bool HasField(string fieldName);
    public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged;
    public ITimeseriesCacheManager GetCacheManager(string fieldName);
    public void StepTo(int currentLogicalRow);
    public void AppendRow<T>(string fieldName, DateTime timestamp, ReadOnlySpan<T> rowValues) where T : unmanaged;
    public void AppendCol(string symbol);
    public void Commit();
    public void Dispose();
}
```

### 6. MarketDataAligner 与 MatrixExtensions
市场行情时间序列数据日历对齐组件与扩展方法。
将物理数据文件的离散时序投射至全市场权威交易日历网格。数据层忠实反映物理数据序列：
- **物理事实投影**：物理文件存在的交易记录原样写入网格对应位置；物理缺失的单元格（停牌、未上市、已退市或漏采）统一严格置 `NaN`（整型采用标准哨兵值 `MinValue`，杜绝 `0` 歧义）；
- **停牌前置契约**：停牌日向前沿用（`ffill`）必须由上游数据源/离线 ETL 脚本预先处理完毕后落盘存入宽表；回测框架数据层严格反映物理数据真相，不对其执行隐式向前沿用或伪造。

```csharp
namespace CarrotBacktesting.NET.Data;

public static class MarketDataAligner
{
    // 获取非托管类型的标准 NaN/哨兵值（double/float 为 IEEE 754 NaN，整型为 long.MinValue / int.MinValue）
    public static T GetNaNValue<T>() where T : unmanaged;

    // 判定指定值是否为 NaN 或缺失哨兵值
    public static bool IsValueNaN<T>(T val) where T : unmanaged;

    // 将离散物理记录投射到目标全局交易日历网格，物理缺失单元格填充 NaN
    public static void AlignAndFill<T>(
        IReadOnlyList<DateTime> globalTradeDates,
        int startIndex,
        int length,
        ReadOnlySpan<DateTime> recordDates,
        ReadOnlySpan<T> recordValues,
        Span<T> destination) where T : unmanaged;
}

// 时序二维矩阵与单标的实用扩展方法
public static class MatrixExtensions
{
    public static bool IsNaN<T>(this IReadOnlyMatrix2D<T> matrix, int row, int col) where T : unmanaged;
    public static ReadOnlyColumnView<T> GetSeries<T>(this IReadOnlyMatrix2D<T> matrix, int symbolIndex = 0) where T : unmanaged;
    public static ReadOnlyColumnView<T> GetSeries<T>(this IReadOnlyMatrix2D<T> matrix, string symbol, IMarketMetadata metadata) where T : unmanaged;
    public static ReadOnlyColumnView<T> GetSeries<T>(this ITimeseriesRegistry provider, string fieldName, int symbolIndex = 0) where T : unmanaged;
    public static ReadOnlyColumnView<T> GetSeries<T>(this ITimeseriesRegistry provider, string fieldName, string symbol) where T : unmanaged;
}
```

### 7. DataLayerFactory
根据 `DataConfig` 配置声明，一键自闭环装配时序、事件与维表三大多模态数据层：

```csharp
namespace CarrotBacktesting.NET.Data;

public static class DataLayerFactory
{
    public static (ITimeseriesRegistry Timeseries, IEventRegistry Events, IFieldRegistry Fields) Create(
        DataConfig config,
        DateTime? startDate = null,
        DateTime? endDate = null);
}
```

---

## 四、异构事件流系统 (Event Stream System)

针对非对齐、稀疏或结构不规则数据（如复权因子、公告、龙虎榜席位），通过时态事件流进行点查。系统原生支持免定义动态链式索引与预编译强类型实体映射。

### 1. IEventTable (时态事件契约)
```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public interface IEventTable : IEnumerable<KeyValuePair<DateTime, IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>>>
{
    string Name { get; }
    int Count { get; }
    IEnumerable<DateTime> Dates { get; } // 按时间递增严格排序
    IReadOnlyDictionary<string, IReadOnlyList<IDataRow>> this[DateTime date] { get; } // 索引器: table[date] -> 当日截面字典
    IDataRow this[DateTime date, string key] { get; } // 二维复合索引器: table[date, symbol] -> 首条事件
    bool TryGetDay(DateTime date, out IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>? dayMap);
    IReadOnlyList<IDataRow> GetRows(DateTime timestamp, string key);
    bool TryGet(DateTime timestamp, string key, out IReadOnlyList<IDataRow> rows);
    bool TryGetFirst(DateTime timestamp, string key, out IDataRow? row);
    bool TryGetGlobal(DateTime timestamp, out IReadOnlyList<IDataRow> globalRows);
    bool ContainsDate(DateTime date);

    // 强类型即时点查与流式 LINQ
    T Get<T>(DateTime timestamp, string key) where T : class;
    bool TryGet<T>(DateTime timestamp, string key, out T? value) where T : class;
    IEnumerable<T> As<T>(DateTime timestamp) where T : class; // 当日截面流
    IEnumerable<T> As<T>(string key) where T : class;          // 单标的历史流
    IEnumerable<T> As<T>() where T : class;                   // 全量历史流
}
```

### 2. EventSourceFactory (高性能编译映射工厂)
框架通过读取表元数据自动识别 schema，并利用 `EntityMapper` 表达式树将数据字段一次性预编译为原生构造委托 `Func<string[], T>`，消除逐行反射与装箱开销；支持按回测时间区间动态执行物理年份分区剪枝。

```csharp
namespace CarrotBacktesting.NET.Data;

public static class EventSourceFactory
{
    public static IEventTable Create(IStorageResolver resolver, string tableId, DateTime? startDate = null, DateTime? endDate = null);
    public static IEventTable Create<T>(IStorageResolver resolver, string tableId, DateTime? startDate = null, DateTime? endDate = null) where T : class;
}
```

### 3. 调用范例
```csharp
// 1. 强类型即时点查与流式 LINQ
public record AdjustmentFactor(double BackAdjFactor);
var table = EventSourceFactory.Create(resolver, "ashare.adj_factor.baostock");

// 点查: table.Get<T>(date, symbol)
double factor = table.Get<AdjustmentFactor>(new DateTime(2021, 7, 21), "sh.600000").BackAdjFactor;

// 流式 LINQ: 当日截面投射
var factors = table.As<AdjustmentFactor>(new DateTime(2021, 7, 21)).ToList();

// 2. 免定义动态链式索引: table[date, symbol][col]
string factorStr = table[new DateTime(2021, 7, 21), "sh.600000"]["back_adj_factor"];

// 3. 全市场日截面快照字典遍历: table[date]
var todayEvents = table[new DateTime(2021, 7, 21)];
foreach (var (symbol, rows) in todayEvents)
{
    IDataRow row = rows[0];
    Console.WriteLine($"{symbol} 因子: {row.GetDouble("back_adj_factor")}");
}
```

---

## 五、标的静态属性与维表系统 (Fields System)

管理板块、概念、行业及标的静态资料等无时间戳平铺维表，系统提供统一的一维静态索引与双向倒排选股服务。

### 1. IFieldTable (维表契约)
```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public interface IFieldTable : IEnumerable<KeyValuePair<string, IReadOnlyList<IDataRow>>>
{
    string Name { get; }
    int Count { get; }
    IEnumerable<string> Keys { get; }
    IDataRow this[string key] { get; } // 索引器: table[symbol] -> IDataRow
    bool TryGetRow(string key, out IDataRow? row);
    bool TryGetRows(string key, out IReadOnlyList<IDataRow> rows);
    IReadOnlyList<IDataRow> GetRows(string key);
    IReadOnlyList<string> GetValues(string key, string column); // 1:N 多值便捷提取
    IReadOnlyList<string> GetKeys(string value, string? column = null); // 动态倒排选股 (省略列名全表全列反查)

    // 强类型即时点查与流式 LINQ
    T Get<T>(string key) where T : class;
    bool TryGet<T>(string key, out T? value) where T : class;
    IReadOnlyList<string> GetKeys<T>(Func<T, bool> predicate) where T : class; // 强类型谓词倒排选股
    IEnumerable<T> As<T>() where T : class;                                   // 强类型流式 LINQ
    IEnumerable<KeyValuePair<string, T>> AsPairs<T>() where T : class;        // 强类型键值对流式 LINQ
}
```

### 2. FieldSourceFactory (维表加载工厂)
```csharp
namespace CarrotBacktesting.NET.Data;

public static class FieldSourceFactory
{
    public static IFieldTable Create(IStorageResolver resolver, string tableId, string? keyColumn = null);
    public static IFieldTable Create(string storageRoot, string tableId, string? keyColumn = null);
}
```

### 3. 调用范例
```csharp
var resolver = new StorageResolver("D:/QuantData");

// 方式 A：免定义动态点查、多值提取与倒排选股
var conceptTable = FieldSourceFactory.Create(resolver, "ashare.concept.eastmoney");
string name = conceptTable["sh.600000"]["board_name"];            // 链式索引器
var allConcepts = conceptTable.GetValues("sh.600000", "board_name"); // 1:N 多值便捷提取
var pool1 = conceptTable.GetKeys("低空经济");                     // 全局倒排快速圈选 (无需记忆列名)
var pool2 = conceptTable.GetKeys("低空经济", "board_name");       // 精确指定列倒排

// 遍历标的池
foreach (var symbol in conceptTable.Keys) { ... }
foreach (var (symbol, rows) in conceptTable) { ... }

// 方式 B：强类型 Record 点查与 Lambda 选股（推荐）
public record IndustryProfile(string BoardName, string BoardCode, string StockName); // 字段无序自适应
var industryTable = FieldSourceFactory.Create(resolver, "ashare.industry.eastmoney");

IndustryProfile ind = industryTable.Get<IndustryProfile>("sh.601958");
string name = ind.BoardName;
var pool = industryTable.GetKeys<IndustryProfile>(x => x.BoardName == "钼"); // 编译期类型安全，杜绝拼写错误

// 方式 C：从配置清单批量装配（FieldRegistryFactory）
var configs = new List<FieldTableConfig>
{
    new() { Name = "industry", TableId = "ashare.industry.eastmoney" },
    new() { Name = "concept", TableId = "ashare.concept.eastmoney" }
};
IFieldRegistry fieldRegistry = FieldRegistryFactory.Create(resolver, configs);
```

---

## 六、引擎与黑板上下文 (Engine & Execution Context)

### 1. IEngineContext (黑板上下文)
策略层运行时的核心宿主环境，隔离具体回测撮合引擎或实盘网关细节，收敛极简当期取价门面与指标注册中心。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface IEngineContext
{
    DateTime CurrentTime { get; }           // 当前时间步
    int CurrentIndex { get; }               // 当前交易日行索引
    ITimeseriesRegistry Timeseries { get; } // 全局时序注册表与行情矩阵输入
    IEventRegistry Events { get; }          // 异构事件流
    IFieldRegistry Fields { get; }          // 标的静态属性与维表字典
    IBroker Broker { get; }                 // 账户持仓与报单
    IMarketState Market { get; }            // 宏观市场决策黑板
    IIndicatorRegistry Indicators { get; }  // 全局指标注册中心（瘦契约挂载，零反向穿透）
    T? GetExtension<T>() where T : class;   // 引擎扩展组件与插件查询

    // 标的元数据门面 (Symbols Metadata Facades)
    IReadOnlyList<string> Symbols { get; }      // 当前标的列表（固定顺序，与列索引一一对应）
    int SymbolCount { get; }                    // 当前标的总数量
    int GetSymbolIndex(string symbol);          // 获取标的列索引（无则 -1）

    // 高频交易动词门面 (High-Frequency Trading Verbs)
    string Buy(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "");
    string Sell(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "");
    string ClosePosition(string symbol, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "");

    // 当期账户与持仓门面 (Account & Position Facades)
    decimal GetPosition(string symbol);
    decimal GetAvailablePosition(string symbol);
    double GetPositionCost(string symbol);  // 加权持仓成本价（无持仓为 0.0）
    double AvailableCash { get; }
    double TotalEquity { get; }

    // 时序与复权取价门面（支持时序负向回溯 offset <= 0，offset > 0 严格抛出未来函数熔断异常，默认 Post 后复权）
    double GetClose(string symbol, int offset = 0, PriceMode mode = PriceMode.Post);
    double GetClose(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post);
    double GetOpen(string symbol, int offset = 0, PriceMode mode = PriceMode.Post);
    double GetOpen(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post);
    double GetHigh(string symbol, int offset = 0, PriceMode mode = PriceMode.Post);
    double GetHigh(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post);
    double GetLow(string symbol, int offset = 0, PriceMode mode = PriceMode.Post);
    double GetLow(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Post);
    double GetVolume(string symbol, int offset = 0, PriceMode mode = PriceMode.Raw); // 成交量默认保持 Raw 物理量
    double GetVolume(int symbolIndex, int offset = 0, PriceMode mode = PriceMode.Raw);
    double GetField(string symbol, string fieldName, int offset = 0, PriceMode mode = PriceMode.Post);
    double GetField(int symbolIndex, string fieldName, int offset = 0, PriceMode mode = PriceMode.Post);
}
```

### 2. IMarketState (通用强类型黑板)
策略间正交解耦与异步数据交换的通用强类型容器，底层基于 `ConcurrentDictionary<string, object>` 保障多核并发下的绝对线程安全：
- **`GetState<T>(string? key = null)`**: 获取强类型状态/信号（无 class 引用类型限制，原生支持 struct 只读结构体）。缺省 key 时以类型全名作为权威单例索引；指定 key 时从对应命名槽位读取。
- **`TryGetState<T>(out T? state, string? key = null)`**: 安全探测并获取强类型状态/信号。
- **`SetState<T>(T state, string? key = null)`**: 发布强类型状态/信号，记录更新时态戳。
- **`HasState<T>(string? key = null)`**: 探测黑板中是否存在指定类型（与可选 Key）的状态。
- **`Clear()`**: 清理黑板中所有状态数据。
- **协作约定**: 缺省 key 时为类型全局单例（如统一发布的宏观指标或目标持仓）；显式指定 key 时代表独立命名通道/槽位，支持同类型多实例隔离（如多因子并发打分）与跨策略协同消费。

### 3. IBroker 与订单交互

> 详细资产持仓账本、两阶段挂单冻结状态机、多时区自然日清算及实盘对账规范详见 [客户端经纪商与账户账本体系 (broker.md)](broker.md)；撮合机制与挂单簿详见 [交易所仿真与集中撮合体系 (exchange.md)](exchange.md)。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface IBroker
{
    double GetCash();
    double GetTotalEquity();
    decimal GetPosition(string symbol);
    decimal GetAvailablePosition(string symbol);
    double GetPositionCost(string symbol);
    IReadOnlyDictionary<string, decimal> GetPositions();
    string SubmitOrder(OrderRequest request);
    string Buy(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "");
    string Sell(string symbol, decimal quantity, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "");
    string ClosePosition(string symbol, double price = 0.0, OrderType type = OrderType.Market, string? clientOrderId = null, string orderTag = "");
    void CancelOrder(string orderId);
    OrderStatus GetOrderStatus(string orderId);
    void Step(DateTime currentTime);
    void SyncState(double cash, IReadOnlyDictionary<string, decimal> positions);
    
    event Action<string, OrderStatus> OnOrderStatusChanged;
    event Action<ExecutionReport> OnTrade;
    event Action<PositionEvent>? OnPositionChanged;
}
```

#### 核心数据模型
- **`OrderRequest`**: `(string Symbol, OrderDirection Direction, OrderType Type, double Price, decimal Quantity)`
- **`ExecutionReport`**: `(string OrderId, string Symbol, double FillPrice, decimal FillQuantity, double Commission, DateTime Time)`
- **`PositionEvent`**: `(string Symbol, decimal NewQuantity, double AveragePrice, DateTime Time)`
- **`OrderStatus`**: `Pending`, `Submitted`, `PartialFilled`, `Filled`, `Canceled`, `Rejected`

### 4. 交易所网关契约 (IExchangeGateway 与 ISimulatedExchange)

定义客户端与集中撮合交易所/实盘柜台之间的通信协议，实现回测仿真与实盘交易的一致性切换。详细撮合引擎与挂单簿规范详见 [交易所仿真与集中撮合体系 (exchange.md)](exchange.md)。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

/// <summary>
/// 通用交易所/柜台网关通信契约。
/// </summary>
public interface IExchangeGateway
{
    void SendOrder(string orderId, OrderRequest request);
    void CancelOrder(string orderId);

    event Action<ExecutionReport> OnExecution;
    event Action<OrderStateReport> OnOrderStateChanged;
}

/// <summary>
/// 回测环境仿真交易所驱动契约。
/// </summary>
public interface ISimulatedExchange : IExchangeGateway
{
    BarSnapshot GetBar(string symbol);
    void SetCurrentTime(MarketTime currentTime);
    void Step(MarketTime currentTime, ITimeseriesRegistry timeseries, int currentIndex);
    void OnMarketData(string symbol, MarketTime time, in BarSnapshot snapshot);
}

/// <summary>
/// 交易所网关提供者显式契约，受保护地获取底层网关。
/// </summary>
public interface IExchangeGatewayProvider
{
    IExchangeGateway Gateway { get; }
}
```

### 5. IEngine 与 BacktestingEngine (回测驱动核心)

统一统领回测生命周期、时间网格推进、T+N 跨日结算与策略管线执行。详细生命周期状态机与步进流水线详见 [核心回测引擎体系 (engine.md)](engine.md)。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface IEngine
{
    EngineStatus Status { get; }
    double Progress { get; }
    DateTime CurrentTime { get; }
    IEngineContext Context { get; }

    void Run();
    Task RunAsync(CancellationToken cancellationToken = default);
    void Pause();
    void Resume();
    void Stop();
}

public interface ICheckpointable
{
    byte[] SaveCheckpoint();
    void RestoreCheckpoint(byte[] checkpoint);
}
```

- **`BacktestingEngine` 单一权威内核**：
  - **二分区间裁剪**：依据 `StartDate` 与 `EndDate` 通过二分查找确定起止 `TradeDates` 索引；
  - **主时钟步进推进**：按时间步严格执行（`UpdateStep` → 调度器推进 `ITimeseriesScheduler.StepTo` → 推进经纪商时钟与交易所本地清算 `Broker.Step` → 离散事件探测派发 `pipeline.OnEvent` → 指标单步单次增量计算 `Indicators.Step` → 策略主决策 `pipeline.OnUpdate`）；
  - **热路径零堆分配 (零 GC)**：上下文单例常驻、复用事件内存缓冲区，单步执行零额外分配；
  - **状态机生命周期控制**：`Idle` → `Running` → `Paused` / `Completed` / `Faulted`，支持安全取消与暂停等待；
  - **会话门面 (`BacktestSession`)**：整合配置解析、引擎步进与绩效分析，支持通过 `BacktestSession.Run("session.yaml")` 直接执行回测并生成报告。

---

## 七、独立指标与计算属性子系统 (Indicators & Computed Properties)

为彻底避免全量预计算后复权价与指标导致物理二维矩阵膨胀（20GB~30GB），CarrotQuant.NET 建立了高性能增量流式指标子系统与当期极速复权因子供给机制。

### 1. IStreamIndicator (流式指标核心契约)
统一单值与多值指标为唯一抽象契约，原生支持负数时序索引、前瞻偏差防护（当 offset > 0 时抛出 ArgumentOutOfRangeException）与停牌冻结跳过（Skip & Freeze）。每步传入完整的 OHLCV 市场行情，杜绝假参数与伪实现：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Indicators;

public interface IStreamIndicator
{
    double this[int offset] { get; } // 统一负数索引: 0 为当期，-1 为上一期；offset > 0 抛出 ArgumentOutOfRangeException
    double Current => this[0];       // 便捷当期值
    bool IsReady { get; }            // 预热期感知
    void Reset();                    // 状态机重置守恒

    void Update(double open, double high, double low, double close, double volume);
}
```

### 2. 常用流式指标实现 (Indicators)
- **`Sma`**: 简单移动平均。定长滑动窗口与双精度累加器（`double _sum`），预计算倒数乘法，复杂度严格为 $O(1)$，彻底免疫单精度浮点舍入漂移。
- **`Ema`**: 指数移动平均。增量平滑递推，支持周期 $p$ 与自定义平滑系数 $\alpha \in (0, 1]$ 构造，前期简单平均对齐冷启动。
- **`Wma`**: 加权移动平均。基于差分递推公式 $W_t = W_{t-1} + N\cdot C_t - S_{t-1}$ 实现真 $O(1)$ 纳秒级加权计算。
- **`Vwap`**: 成交量加权平均价。支持全周期累计与定长滑动窗口，成交量为 0 边界安全防御与跨日 Reset 复原。
- **`Kdj`**: 随机指标。对齐通达信 A 股标准，极盘无波动防除零保护，通过只读属性暴露 `K`、`D`、`J` 子流。
- **`Sar`**: 抛物转向系统。严格遵循 Welles Wilder 物理状态机与 TA-Lib 对齐，支持动态加速因子与多空极值跟踪。
- **`Rsi`**: 相对强弱指标。基于 J. Welles Wilder 平滑递推算法。
- **`Atr`**: 真实波幅均值。自动处理跨日跳空高开/低开。
- **`Macd`**: 复合多值指标。采用组合模式实现 `IStreamIndicator`（索引器默认指向 `Dif`），通过强类型只读属性暴露 `Dif`、`Dea` 与 `Hist` 子流。
- **`BollingerBands`**: 布林带复合指标。通过只读属性暴露 `Upper`、`Middle` 与 `Lower` 子流。

### 3. IndicatorRegistry (全局指标注册中心与标的池容器)
- **零接口套娃与具象类挂载**：策略端直接操作纯具象类 `IndicatorRegistry`，消灭无多态价值的镜像接口与虚派发开销。
- **双模极速体验（Pool 模式 & Get 门面模式）**：
  - **标的池持有模式**：`var smaPool = context.Indicators.GetOrAdd("sma20", () => new Sma(20));` 策略直接持有 `IndicatorPool<T>` 实例，通过 `smaPool[symbolIndex]` 纳秒级纯数组下标直取，CPU L2 Cache 极致亲和；
  - **免声明门面模式**：通过 `context.Indicators.Get<T>("sma20", symbol)` 免去字段声明直接读取；
- **全市场稠密数组驱动与内部步进隔离**：`Step()` 与 `Reset()` 步进调度方法设为 `internal`，对策略彻底隐身，杜绝策略误调用导致状态污染。引擎主循环在进入策略前执行单次 `Step(_context)` 统一批量泵送，多策略共享同一指标计算状态，**单步单次计算，彻底消除重复开销**。
- **除权因子一一对应严格校验**：在复权模式下，系统在启动时及步进过程中对所有标的进行首日有效行情严格比对；凡在产生有效行情之前未在除权表中提供过复权因子的标的（如次新股、跨年加载未覆盖、数据源断档），系统强制发出 Warning 告警，杜绝静默隐式赋 1.0 带来的数据污染。

```csharp
// 策略端声明式体验范例:
// 1. 初始化声明（约定优于配置: 默认绑定 Post 后复权，引擎自动统一泵送 OHLCV）
// 可直接持有高频 Pool 实例：
var smaPool = context.Indicators.GetOrAdd("sma20", () => new Sma(20));
var macdPool = context.Indicators.GetOrAdd("macd", () => new Macd(12, 26, 9));

// 2. 决策时直接读取特定标的的指标（引擎已在每步进入 OnUpdate 前全自动泵送完毕）
// 方式 A：通过持有的 Pool 极速索引（O(1) 连续数组）
double fastSma = smaPool[symbolIndex].Current;
double prevSma = smaPool[symbolIndex][-1];

// 方式 B：通过门面免声明直接读取
var macd = context.Indicators.Get<Macd>("macd", symbol);
if (macd.Dif[0] > macd.Dea[0] && macd.Dif[-1] <= macd.Dea[-1])
{
    // DIF 上穿 DEA 金叉信号
}
```

---

## 八、策略与管线开发 (Strategy & Pipeline)

### 1. IStrategy 契约
所有策略的基础契约，定义策略在引擎推进过程中的统一驱动入口与生命周期：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Strategy;

public interface IStrategy
{
    string Name { get; }
    int Lookback => 200; // 历史最大回溯深度（行数，供常驻非托管分块容量自动推导）

    void OnStart(IEngineContext context) { }
    void OnUpdate(IEngineContext context); // 核心时步计算与决策入口
    void OnEvent(IEngineContext context, string tableName, IReadOnlyList<IDataRow> events) { } // 外部离散事件批量推送
    void OnStop(IEngineContext context) { }
    void OnOrderStatusChanged(IEngineContext context, string orderId, OrderStatus status) { } // 订单状态变更回报
    void OnTrade(IEngineContext context, ExecutionReport e) { } // 成交执行回报
    void OnPositionChanged(IEngineContext context, PositionEvent e) { } // 持仓状态变更
}
```

### 2. 模板基类与开发范式
- **`StrategyBase`**: 通用策略抽象基类，默认提供类名反射名称、Lookback（200）以及空生命周期安全实现。开发者仅需覆写 `OnUpdate(IEngineContext context)`，彻底告别样板代码。
- **`RuleStrategyBase`**: 规则交易便利模板基类，继承自 `StrategyBase`，内部按标的遍历并分发至 `CheckEntry(context, symbol)` 与 `CheckExit(context, symbol, position)`。
- **纯信号/Alpha 策略范式**: 直接实现 `IStrategy` 契约，专注于特征计算与信号生产，通过黑板（`context.Market.SetState<T>`）发布打分或状态，与后续执行策略正交解耦。

### 3. 组合策略管线 (StrategyPipeline)
采用组合模式（Composite Pattern）将特征分析、多信号生产、仓位规划与风控报单统一编排为单一标准的 `IStrategy`：
- `StrategyPipeline`: 抽象基类，管理子策略列表、Lookback 递归汇总、不可变数组密封固化与 Roslyn 动态编译上下文回收。
- `SequentialPipeline`: 串行管线，严格按序执行各阶段。
- `ParallelPipeline`: 并行管线，默认采用确定性单线程顺序执行（`Concurrent = false`）；显式开启 `Concurrent = true` 且子策略数大于 1 时，通过 `System.Threading.Tasks.Parallel.For` 零 GC 委托复用多核并发执行 `OnUpdate` 与 `OnEvent`。
- 便捷工厂方法：`StrategyPipeline.Sequential()` 与 `StrategyPipeline.Parallel()`。
- `StrategyPipelineFactory`: 支持基于强类型配置 `PipelineConfiguration` 或 YAML 文件物理路径（`Create`）装配管线并自动反射注入参数，底层配置解析由 `PipelineConfigLoader` 权威负责，严格校验未知参数并支持数组/集合/TimeSpan 转换。

#### YAML 管线配置范例 (`strategy.yaml`):
```yaml
name: ProductionPipeline
mode: sequential
pipeline:
  # 阶段 1：多因子多信号并发生产 (开启多核并发加速)
  - name: SignalStage
    mode: parallel
    concurrent: true
    pipeline:
      - source: "strategies/MomentumAlpha.cs"
        params:
          Period: 14
      - type: "MyProject.Strategies.MeanReversionAlpha"
        params:
          Window: 60
  # 阶段 2：仓位规划与执行风控 (串行执行)
  - type: "MyProject.Strategies.PortfolioExecutionStrategy"
    params:
      Lookback: 200
```

### 4. 典型策略编写范例
```csharp
public class MovingAverageCrossStrategy : IStrategy
{
    public string Name => "MA_Cross_Demo";
    public int Lookback => 60;

    public void OnUpdate(IEngineContext context)
    {
        // 1. 常数时间变更感知（仅在日线更新的 Bar 才触发低频计算）
        if (context.Timeseries.IsUpdated("kline.1d"))
        {
            // 更新宏观或日线低频指标
        }

        // 2. 外部离散事件到达探测
        if (context.Events.HasNewEvents(context.CurrentTime, "suspension"))
        {
            // 停牌事件处理
        }

        // 3. 获取行情只读矩阵
        var closeMatrix = context.Timeseries.GetMatrix<double>("close");
        int row = context.CurrentIndex;
        if (row < 20) return; // 历史数据不足

        // 4. 个股遍历与信号计算
        for (int col = 0; col < context.Timeseries.Metadata.Symbols.Count; col++)
        {
            string symbol = context.Timeseries.Metadata.Symbols[col];
            double todayClose = closeMatrix[row, col];
            double ma20 = 0.0;
            for (int k = 0; k < 20; k++) ma20 += closeMatrix[row - k, col];
            ma20 /= 20.0;

            // 5. 交易执行
            decimal position = context.Broker.GetPosition(symbol);
            if (todayClose > ma20 && position == 0)
            {
                context.Broker.SubmitOrder(new OrderRequest(
                    symbol, OrderDirection.Buy, OrderType.Limit, todayClose, 1000m));
            }
            else if (todayClose < ma20 && position > 0)
            {
                context.Broker.SubmitOrder(new OrderRequest(
                    symbol, OrderDirection.Sell, OrderType.Limit, todayClose, position));
            }
        }
    }
}
```

---

## 九、交易事实沉淀与分析层 (Analysis & Recorder)

### 1. ITradingRecorder
回测与实盘量化事实纯接收记录器，实现 `ITradingRecorder` 与 `ICheckpointable<TradingRecorderSnapshot>`。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Analysis;

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

### 2. 核心数据模型
- **`EquityPoint`**: `(DateTime Time, double TotalEquity, double AvailableCash, double FrozenCash, double OccupiedMargin, double TotalMarketValue, double UnrealizedPnL)` —— 56 字节值类型步进点，热路径 0 GC，派生 `MarginRatio`。
- **`SettlementRecord`**: 标准券商交割单流水（`Time`, `Symbol`, `OrderId`, `Direction`, `Price`, `Quantity`, `Turnover`, `Commission`, `RealizedPnL`, `BalanceCash`, `OrderTag`）。
- **`Trade`**: 已平仓对冲交易明细（开平仓时间戳与标签、开平仓价格、数量、乘数、已实现盈亏、手续费、持仓步数）。
- **`DailySnapshot`**: 跨日收盘全景资产与持仓透视快照，包含 `List<PositionSnapshot>`，用于 Barra 风格归因与实盘对账。
- **`TradingResult`**: 综合不可变结果集，聚合 `Settlements`, `ClosedTrades`, `OpenPositions`, `EquityCurve`, `DailySnapshots`, `Signals`，作为下游绩效分析与报告渲染的单一事实来源。

### 3. 统计模型体系与垂直切片范式 (Statistics & Vertical Slice Architecture)
遵循垂直切片单文件高内聚设计：计算逻辑归位于 `*Analyzer`（如 `PortfolioAnalyzer.Calculate`），产物 `*Statistics` 作为纯数据不可变 Record（实现 `IAnalysisResult`）在同文件末尾声明，并保留静态 `Create(...)` 转发，详见 [绩效分析与报表子系统 (analysis.md)](analysis.md)：
- **`PortfolioStatistics`**: 第一层组合净值与风险统计（`TotalReturn`, `AnnualizedReturn`, `AnnualizedVolatility`, `DownsideVolatility`, `MaxDrawdown`, `MaxDrawdownPeakTime`, `MaxDrawdownTroughTime`, `MaxDrawdownRecoveryTime`, `MaxDrawdownDurationSteps`, `SharpeRatio`, `SortinoRatio`, `CalmarRatio`, `DailyVaR95`, `DailyCVaR95`, `BenchmarkReturn`, `Alpha`, `Beta`, `InformationRatio`, `MonthlyReturns`）。由 `PortfolioAnalyzer` 计算构建。
- **`CapitalStatistics`**: 第二层资本利用与敞口统计（`AveragePositionRatio`, `MaxPositionRatio`, `AverageMarginRatio`, `MaxMarginRatio`, `CashDrag`, `AnnualizedTurnover`）。由 `CapitalAnalyzer` 计算构建。
- **`TradeStatistics`**: 第三层交易行为与执行归因（`TotalTrades`, `WinningTrades`, `LosingTrades`, `BreakevenTrades`, `WinRate`, `LongTrades`, `LongWinningTrades`, `LongWinRate`, `ShortTrades`, `ShortWinningTrades`, `ShortWinRate`, `AverageReturn`, `MedianReturn`, `AverageWinReturn`, `AverageLossReturn`, `WinLossRatio`, `ProfitFactor`, `MaxConsecutiveWins`, `MaxConsecutiveLosses`, `AverageHoldingSteps`, `AverageWinHoldingSteps`, `AverageLossHoldingSteps`, `AverageTradeEfficiency` 多空对称 MFE 捕获效率, `MedianTradeEfficiency`, `ExitTiming` 卖点反思走势, `TagBreakdowns`）。由 `TradeAnalyzer` 计算构建。
- **`CostStatistics`**: 第四层现实交易成本与损耗统计（`TotalCommission`, `TotalSlippage`, `TotalFrictionCost`, `FeeToGrossProfitRatio` 利润侵蚀比, `AverageFrictionPerTrade`）。由 `CostAnalyzer` 计算构建。
- **`SignalStatistics`**: 独立 Alpha 信号前向质量统计（`TotalSignals`, `HorizonSteps`, `Horizons`, `PeakHorizonStep`, `GroupBreakdowns`）。支持**样本等权 (`SampleWeighted`)** 与 **月度时间等权 (`TimeWeighted`)** 双口径，由 `SignalAnalyzer` 计算构建。

### 4. 统一单路径分析管线 (Single-Path Analyzer Pipeline)
- **`IAnalysisResult`**: 统一分析产物自描述契约，包含 `string Name`、`string Title` 与 `IEnumerable<MetricItem> GetMetrics()`，供报表与导出器通排通显。
- **`IAnalysisEngine`**: 统一单路径分析管线契约，暴露 `Analyzers` 只读序列，支持 `Add(IAnalyzer)` 链式注册、`Remove<T>()` 动态剪枝以及 `Analyze(TradingResult, AnalysisOptions?, ITimeseriesRegistry?)` 统一调度。
- **`AnalysisEngine`**: 管线调度实现类，单一路径驱动所有已注册的 `IAnalyzer`，默认挂载五大标准分析器（`PortfolioAnalyzer`, `CapitalAnalyzer`, `TradeAnalyzer`, `CostAnalyzer`, `SignalAnalyzer`），支持运行期剪枝与替换。
- **`IAnalyzer` / `AnalyzerBase<TResult>`**: 统一分析器契约与泛型基类，提供 `string Name` 与 `IAnalysisResult? Analyze(TradingResult facts, AnalysisOptions options, ITimeseriesRegistry? timeseries)`。
- **`AnalysisReport`**: 综合报告聚合根，持有不可变事实 `Facts` 与统一产物列表 `Results`；暴露统一强类型检索 `report.Get<T>()`、统一索引器 `report[name]`、探针 `report.Has<T>()` 以及常用只读门面属性 `Portfolio`, `Capital`, `Trades`, `Cost`, `Signals`。
- **`IReportExporter`**: 报表导出器契约，定义 `void Export(AnalysisReport report)`，详见专门白皮书 [报表导出与呈现子系统开发指南 (exporter.md)](exporter.md)。
- **`ConsoleExporter`**: 控制台报表导出器，支持强类型布尔配置开关与深色主题 HTML 报表持久化：
  ```csharp
  // 1. 动态编排管线：剪枝默认信号评估并链式挂载自定义分析器
  var engine = new AnalysisEngine()
      .Remove<SignalAnalyzer>()
      .Add(new MyCustomBrinsonAnalyzer());

  var report = engine.Analyze(result);

  // 2. 强类型直取分析结果（统一接口）
  var portfolio = report.Portfolio; // 快捷门面
  var brinson   = report.Get<BrinsonResult>(); // 统一泛型直取

  // 3. 控制台导出呈现 (支持自定义模块开关)
  ConsoleExporter.Default.Export(report);
  ```
