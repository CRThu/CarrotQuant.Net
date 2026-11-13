# CarrotQuant .NET API 参考手册 (API Reference)

本文档面向策略研发工程师与框架二次开发者，作为底层接口字典收录 CarrotQuant.NET v4 的核心契约签名与数据模型。

> [!TIP]
> **业务原理与使用指南**：若需了解端到端回测流程与配置范例，请参阅 [标准研发工作流 (workflow.md)](workflow.md) 及 [配置体系指南 (configuration.md)](configuration.md)。

---

## 目录
- [一、配置层 (CarrotBacktesting.NET.Config)](#一配置层-carrotbacktestingnetconfig)
- [二、存储调度与数据物理层 (Abstraction.Data & Data)](#二存储调度与数据物理层-abstractiondata--data)
- [三、内存数据供给层 (ITimeseriesRegistry & ChunkMatrix2D)](#三内存数据供给层-idataprovider--chunkmatrix2d)
- [四、异构事件流系统 (Event Stream System)](#四异构事件流系统-event-stream-system)
- [五、标的静态属性与维表系统 (Fields System)](#五标的静态属性与维表系统-fields-system)
- [六、引擎与黑板上下文 (Engine & Execution Context)](#六引擎与黑板上下文-engine--execution-context)
- [七、策略与管线开发 (Strategy & Pipeline)](#七策略与管线开发-strategy--pipeline)
- [八、流水记录与分析层 (Analysis & Recorder)](#八流水记录与分析层-analysis--recorder)

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
- **`DataConfig`**: `Root` (string)、`Timeseries` (`List<TimeseriesConfig>`)、`Events` (`List<EventStreamConfig>`)、`Fields` (`List<FieldTableConfig>`)。
- **`TimeseriesConfig`**: `TableId` (string)、`Fields` (`List<string>?`)、`Cache` (bool)、`PageSize` (int)、`Refresh` (string)、`Compression` (string)、`CommitBatchSize` (int)、`MaxPages` (int)、`Prefetch` (bool)。
- **`EventStreamConfig`**: `Name` (string)、`TableId` (string)。
- **`FieldTableConfig`**: `Name` (string)、`TableId` (string)、`KeyColumn` (string?)、`ValueColumns` (`List<string>?`)。

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
数据区物理文件的全局调度管理器，负责统一解析 `metadata.json`、探测存储布局与执行按时间维度的动态分区剪枝。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public enum StorageLayout { Hive, Flat }

public interface IStorageResolver
{
    string StorageRoot { get; }
    bool HasTable(string tableId);
    string GetMetadataPath(string tableId);
    string GetFormat(string tableId);                 // "csv" 或 "parquet"
    StorageLayout GetLayout(string tableId);           // Hive 或 Flat
    string GetCategory(string tableId);               // "timeseries" 或 "events"
    string GetPartition(string tableId);              // "symbol", "date" 或 "none"
    IReadOnlyList<string> GetFieldNames(string tableId);
    Type GetFieldType(string tableId, string fieldName);
    
    /// <summary>
    /// 按代码路由与起止日期剪枝返回物理数据文件路径列表。
    /// </summary>
    IReadOnlyList<string> ResolvePhysicalFiles(
        string tableId, 
        string? symbol = null, 
        DateTime? startDate = null, 
        DateTime? endDate = null);
}
```

#### 调用范例
```csharp
var resolver = new StorageResolver("D:/QuantData");

// 按时间范围执行 Hive 年份/月份剪枝解析
var files = resolver.ResolvePhysicalFiles(
    tableId: "ashare.kline.1m.raw.custom", 
    startDate: new DateTime(2025, 1, 1), 
    endDate: new DateTime(2025, 3, 31)
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
  - `CsvTimeseriesSource`: 继承基类，基于 Sylvan.Data.Csv 流式解析提供高性能无装箱行提取。
  - `ParquetTimeseriesSource`: 继承基类，基于 Parquet 列式存储与 YearCache 列缓存提供极速列切片提取。
- **`TimeseriesSourceFactory`**: 时序数据源工厂类，依据 `IStorageResolver.GetFormat(tableId)` 自动派发构建对应数据源：
  ```csharp
  ITimeseriesSource source = TimeseriesSourceFactory.Create(storageRoot, tableId);
  ```

> [!NOTE]
> **快照与时序的统一供给**：CarrotQuant.NET v4 废除了独立的流式 `IMarketSnapshotSource` 接口，全市场行情统一由 `IReadOnlyMatrix2D<T>`（`ChunkMatrix2D<T>`）提供：横向全市场快照直接通过行切片 `GetRowView(dateIndex)` 消费，纵向个股时序通过列切片 `GetColumnView(symbolIndex)` 消费。

---

## 三、内存数据供给层 (ITimeseriesRegistry & ChunkMatrix2D)

### 1. ITimeseriesRegistry & ITimeseriesWriter (CQRS 读写分离)
数据层向引擎与策略提供只读数据访问契约（`ITimeseriesRegistry`），向实盘数据录入与持久化提供写入契约（`ITimeseriesWriter`）。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

/// <summary>
/// 纯只读数据契约（面向策略与回测消费）。
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

### 4. ITimeseriesCache 与 TimeseriesCache
基于 4096 行分块与原生 LZ4 块压缩的时序二进制持久化存储器抽象与实现。支持分块独立自描述宽度（Variable Width per Bin），在内存载入时根据目标宽度自动拓展列并填充 NaN，仅重刷当前活跃分块，彻底消除历史分块的写放大与无意义存储膨胀。

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
    void WriteMatrix<T>(string fieldName, ChunkMatrix2D<T> matrix, IReadOnlyList<string> symbols, IReadOnlyList<DateTime> tradeDates) where T : unmanaged;
    void RewriteChunk<T>(string fieldName, int pageIndex, Action<int, Span<T>> rowProvider, int? newChunkWidth = null, IReadOnlyList<string>? updatedSymbols = null) where T : unmanaged;
    void AppendRows<T>(string fieldName, int deltaRows, Action<int, Span<T>> rowProvider, IReadOnlyList<DateTime> newTradeDates) where T : unmanaged;
    void Clear(string fieldName);
    void ClearTable();
}

public class TimeseriesCache : ITimeseriesCache
{
    public TimeseriesCache(string storeRoot, string tableId, int pageSize = 4096, string compression = "lz4");
    // (实现 ITimeseriesCache 契约)
}
```

### 5. TimeseriesCacheManager<T> 与 TimeseriesRegistry
时序连续分块状态机与高性能供给门面：
- **TimeseriesCacheManager<T>**: 单表单字段连续多分块调度器，管理非托管内存矩阵与磁盘分块持久化存储间的分块挂载、实盘单行追加与跨页分块轮转（Rollover，每 4096 行开辟新块），以及精准增量提交（`Commit`，仅落盘活跃分块，历史分块零写放大）。
- **TimeseriesRegistry**: 满足 `ITimeseriesRegistry` 契约的高性能数据门面。遵循单一职责原则，底层存储与分块工厂完全外包隔离，对内纯粹持有并协调各字段的 `TimeseriesCacheManager`，对外提供只读二维时序矩阵（`GetMatrix<T>`）、分块调度器检索（`GetCacheManager<T>`）与原子落盘（`Commit`）。

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
    public void StepTo(int currentLogicalRow);
    public void AppendRow(DateTime timestamp, ReadOnlySpan<T> rowValues);
    public void AppendRow(ReadOnlySpan<T> rowValues); // 兼容重载
    public void AppendCol(string symbol);
    public void UpdateRow(int rowIndex, ReadOnlySpan<T> rowValues);
    public void MarkDirty();
    public void Commit();
    public void Dispose();
}

public class TimeseriesRegistry : ITimeseriesRegistry, ITimeseriesWriter, IDisposable
{
    public IMarketMetadata Metadata { get; }
    public bool HasField(string fieldName);
    public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged;
    public TimeseriesCacheManager<T> GetCacheManager<T>(string fieldName) where T : unmanaged;
    public void StepTo(int currentLogicalRow);
    public void AppendRow<T>(string fieldName, DateTime timestamp, ReadOnlySpan<T> rowValues) where T : unmanaged;
    public void AppendCol(string symbol);
    public void Commit();
    public void Dispose();
}
```

### 6. MarketDataAligner 与 MatrixExtensions
市场行情时间序列数据日历对齐组件与扩展方法。
将物理数据文件的离散时序投射至全市场权威交易日历网格。遵循第一性原理，数据层忠实反映物理数据真相：
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
var pool1 = conceptTable.GetKeys("低空经济");                     // 全局倒排秒级圈选 (无需记忆列名)
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
策略层运行时的核心宿主环境，隔离具体回测撮合引擎或实盘网关细节。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface IEngineContext
{
    DateTime CurrentTime { get; }         // 当前时间步
    int CurrentIndex { get; }             // 当前交易日行索引
    ITimeseriesRegistry Timeseries { get; } // 全局时序注册表与行情矩阵输入
    IEventRegistry Events { get; }        // 异构事件流
    IFieldRegistry Fields { get; }        // 标的静态属性与维表字典
    IBroker Broker { get; }               // 账户持仓与报单
    IMarketState Market { get; }          // 宏观市场决策黑板
    T? GetExtension<T>() where T : class; // 引擎扩展组件与插件查询
}
```

### 2. IMarketState (通用强类型黑板)
策略间正交解耦与异步数据交换的通用强类型容器，底层基于 `ConcurrentDictionary<string, object>` 保障多核并发下的绝对线程安全：
- **`GetState<T>(string? key = null) where T : class`**: 获取强类型状态/信号。缺省 key 时以类型全名作为权威单例索引；指定 key 时从对应命名槽位读取。
- **`SetState<T>(T state, string? key = null) where T : class`**: 发布强类型状态/信号。
- **`HasState<T>(string? key = null) where T : class`**: 探测黑板中是否存在指定类型（与可选 Key）的状态。
- **`Clear()`**: 清理黑板中所有状态数据。
- **协作约定**: 缺省 key 时为类型全局单例（如统一发布的宏观指标或目标持仓）；显式指定 key 时代表独立命名通道/槽位，支持同类型多实例隔离（如多因子并发打分）与跨策略协同消费。

### 3. IBroker 与订单交互
```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface IBroker
{
    double GetCash();
    decimal GetPosition(string symbol);
    string SubmitOrder(OrderRequest request);
    void CancelOrder(string orderId);
    OrderStatus GetOrderStatus(string orderId);
    
    event Action<string, OrderStatus> OnOrderStatusChanged;
    event Action<ExecutionReport> OnTrade;
}
```

#### 核心数据模型
- **`OrderRequest`**: `(string Symbol, OrderDirection Direction, OrderType Type, double Price, decimal Quantity)`
- **`ExecutionReport`**: `(string OrderId, string Symbol, double FillPrice, decimal FillQuantity, double Commission, DateTime Time)`
- **`PositionEvent`**: `(string Symbol, decimal NewQuantity, double AveragePrice, DateTime Time)`
- **`OrderStatus`**: `Pending`, `Submitted`, `PartialFilled`, `Filled`, `Canceled`, `Rejected`

---

## 七、策略与管线开发 (Strategy & Pipeline)

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

### 2. 核心派生契约与模板基类
- **`ISignalStrategy`**: 纯信号语义标记契约，面向特征分析与信号生产，通过黑板（`context.Market.SetState<T>`）发布打分或状态，解耦交易执行。
- **`RuleStrategyBase`**: 规则交易便利模板基类，内部按标的遍历并分发至 `CheckEntry(context, symbol)` 与 `CheckExit(context, symbol, position)`。

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
        // 1. 纳秒级变更感知（仅在日线更新的 Bar 才触发低频计算）
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

## 八、流水记录与分析层 (Analysis & Recorder)

### 1. IRecorder
回测与实盘过程的流水采集器。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Analysis;

public interface IRecorder
{
    void RecordOrder(OrderContext order);
    void RecordTrade(TradeContext trade);
    void RecordSnapshot(PortfolioSnapshot snapshot);
    BacktestReport GetReport();
}
```

### 2. 流水数据模型
- **`OrderContext`**: `(string OrderId, string Symbol, OrderDirection Direction, double Price, double Volume, OrderStatus Status, DateTime Time)` —— 记录订单状态变更快照。
- **`TradeContext`**: `(string TradeId, string Symbol, double EntryPrice, double ExitPrice, double ProfitAndLoss, DateTime EntryTime, DateTime ExitTime)` —— 记录一笔完整开平仓交易明细与损益。
- **`PortfolioSnapshot`**: `(DateTime Time, double AvailableCash, double TotalEquity, int OpenPositionsCount)` —— 记录时步点投资组合净值与持仓快照。
- **`BacktestReport`**: 量化统计指标综合报告。
- 导出的结果数据可直接供给控制台格式化输出或后续报表导出器渲染。
