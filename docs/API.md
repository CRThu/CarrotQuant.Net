# CarrotQuant .NET API 参考手册 (API Reference)

本文档面向策略研发工程师与框架二次开发者，作为底层接口字典收录 CarrotQuant.NET v4 的核心契约签名与数据模型。

> [!TIP]
> **业务原理与使用指南**：若需了解端到端回测流程与配置范例，请参阅 [标准研发工作流 (workflow.md)](workflow.md) 及 [配置体系指南 (configuration.md)](configuration.md)。

---

## 目录
- [一、配置层 (CarrotBacktesting.NET.Config)](#一配置层-carrotbacktestingnetconfig)
- [二、存储调度与数据物理层 (Abstraction.Data & Data)](#二存储调度与数据物理层-abstractiondata--data)
- [三、内存数据供给层 (IDataProvider & ChunkMatrix2D)](#三内存数据供给层-idataprovider--chunkmatrix2d)
- [四、异构事件流系统 (Event Stream System)](#四异构事件流系统-event-stream-system)
- [五、引擎与黑板上下文 (Engine & Execution Context)](#五引擎与黑板上下文-engine--execution-context)
- [六、策略与管线开发 (Strategy & Pipeline)](#六策略与管线开发-strategy--pipeline)
- [七、流水记录与分析层 (Analysis & Recorder)](#七流水记录与分析层-analysis--recorder)

---

## 一、配置层 (CarrotBacktesting.NET.Config)

### 1. EngineConfigLoader
配置加载入口静态类，负责将 YAML 文件反序列化为强类型配置对象并完成文件路径的层级解析。

```csharp
namespace CarrotBacktesting.NET.Config;

public static class EngineConfigLoader
{
    /// <summary>
    /// 从指定 YAML 配置文件加载，自动完成多态路径外链与相对路径归一化。
    /// </summary>
    /// <param name="configFilePath">YAML 配置文件物理路径。</param>
    /// <returns>规范化后的完整 EngineConfig 实例。</returns>
    /// <exception cref="ArgumentException">路径为空时抛出。</exception>
    /// <exception cref="FileNotFoundException">主文件或引用的外部数据配置文件不存在时抛出。</exception>
    public static EngineConfig Load(string configFilePath);
}
```

### 2. 配置模型
- **`EngineConfig`**: 顶层聚合配置，包含 `EngineSettings Engine` 与 `DataConfig Data`。
- **`EngineSettings`**: `Mode` (`backtest`/`living`)、`StartDate` (DateTime?)、`EndDate` (DateTime?)、`InitialCash` (double)。
- **`DataConfig`**: `Root` (string)、`Timeseries` (`List<TimeseriesConfig>`)、`Events` (`List<EventStreamConfig>`)。
- **`TimeseriesConfig`**: `TableId` (string)、`Fields` (`List<string>?`)。
- **`EventStreamConfig`**: `Name` (string)、`TableId` (string)。

> 详细参数字典、类型、默认值与必填约束详见 [配置参数全量字典 (configuration.md)](configuration.md#二配置参数全量字典-parameter-reference)。

#### 调用范例
```csharp
// 加载策略主配置（自动识别并合并外链的 shared/data.yaml）
EngineConfig config = EngineConfigLoader.Load("configs/strategy_cta.yaml");

Console.WriteLine($"回测模式: {config.Engine.Mode}, 初始资金: {config.Engine.InitialCash}");
Console.WriteLine($"数据根目录: {config.Data.Root}");
```

### 3. YAML 配置范例

#### 模式 A：直接内嵌模式 (Inline)
```yaml
engine:
  mode: "backtest"            # backtest / living
  start_date: "2025-01-01"    # 回测起始时间
  end_date: "2025-03-31"      # 回测结束时间
  initial_cash: 1000000.0     # 初始资金

data:
  root: "./data"              # 数据存储根目录
  timeseries:
    - table_id: "ashare.kline.1m.raw.custom"
      fields: ["open", "high", "low", "close", "volume"]
  events:
    - name: "adjustments"
      table_id: "ashare.adj_factor.baostock"
```

#### 模式 B：外链复用独立配置 (Linked / External)
- **主策略配置文件 (`strategy.yaml`)**:
  ```yaml
  engine:
    mode: "backtest"
    start_date: "2025-01-01"
    end_date: "2025-03-31"
    initial_cash: 2000000.0

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
- **`IMarketSeriesSource`**: 纵向代码序列读取器。
  ```csharp
  void ReadSymbolSeries<T>(string symbol, string fieldName, int startIndex, int length, Span<T> destination) where T : unmanaged;
  ```
  - `CsvMarketSeriesSource`: 基于 CSV 文件提供跨时间序列的高性能提取。
  - `ParquetMarketSeriesSource`: 基于 Parquet 列式存储提供跨时间序列的高性能提取。
- **`IMarketSnapshotSource`**: 横向全市场快照读取器。
  ```csharp
  bool MoveNext();
  DateTime CurrentDate { get; }
  void ReadFieldSnapshot<T>(string fieldName, Span<T> destination) where T : unmanaged;
  ```

---

## 三、内存数据供给层 (IDataProvider & ChunkMatrix2D)

### 1. IDataProvider
数据层向引擎与策略提供只读数据访问契约，统一返回非托管时序矩阵的只读视图。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public interface IDataProvider
{
    IMarketMetadata Metadata { get; }
    bool HasField(string fieldName);
    
    /// <summary>
    /// 获取指定字段的全局二维只读时序矩阵（Row=交易日/时间步，Col=股票代码）。
    /// </summary>
    IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged;

    /// <summary>
    /// 原子提交所有字段缓存数据，增量落盘持久化至 TimeseriesStore。
    /// </summary>
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
    public int Width { get; }
    public int PageSize { get; }
    public int PageCount { get; }

    public ChunkMatrix2D(int width, int pageSize = 4096, int initialRowCount = 0);

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
    public void ExpandWidth(int newWidth);
    public void Dispose();
}
```

### 3. 核心访问模式
`IDataProvider.GetMatrix<T>` 返回 `IReadOnlyMatrix2D<T>`，支持无 GC 压力的批量截面切片与序列点查：

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

### 4. ITimeseriesStore 与 TimeseriesStore
基于 4096 行分块与原生 LZ4 块压缩的时序二进制持久化存储器抽象与实现。支持分块独立自描述宽度（Variable Width per Bin），在内存载入时根据目标宽度自动拓展列并填充 NaN，仅重刷当前活跃分块，彻底消除历史分块的写放大与无意义存储膨胀。

```csharp
namespace CarrotBacktesting.NET.Data;

public interface ITimeseriesStore
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

public class TimeseriesStore : ITimeseriesStore
{
    public TimeseriesStore(string storeRoot, string tableId, int pageSize = 4096, string compression = "lz4");
    // (实现 ITimeseriesStore 契约)
}
```

### 5. TimeseriesChunkManager<T> 与 CachedDataProvider
时序连续分块状态机与高性能供给门面：
- **TimeseriesChunkManager<T>**: 单表单字段连续多分块调度器，管理非托管内存矩阵与磁盘分块持久化存储间的分块挂载、实盘单行追加与跨页分块轮转（Rollover，每 4096 行开辟新块），以及精准增量提交（`Commit`，仅落盘活跃分块，历史分块零写放大）。
- **CachedDataProvider**: 满足 `IDataProvider` 契约的高性能数据门面。遵循单一职责原则，底层存储与分块工厂完全外包隔离，对内纯粹持有并协调各字段的 `TimeseriesChunkManager`，对外提供只读二维时序矩阵（`GetMatrix<T>`）、分块调度器检索（`GetChunkManager<T>`）与原子落盘（`Commit`）。

```csharp
namespace CarrotBacktesting.NET.Data;

public sealed class TimeseriesChunkManager<T> : ITimeseriesChunkManager where T : unmanaged
{
    public string FieldName { get; }
    public Type DataType { get; }
    public int TotalRows { get; }
    public int ActivePageIndex { get; }
    public int ActivePageRowCount { get; }
    public bool IsDirty { get; }
    public int UncommittedRowCount { get; }
    public ChunkMatrix2D<T> Matrix { get; }

    public IReadOnlyMatrix2D<T> AsReadOnly();
    public void AppendRow(DateTime timestamp, ReadOnlySpan<T> rowValues);
    public void AppendRow(ReadOnlySpan<T> rowValues); // 兼容重载
    public void AppendCol(string symbol);
    public void UpdateRow(int rowIndex, ReadOnlySpan<T> rowValues);
    public void MarkDirty();
    public void Commit();
    public void Dispose();
}

public class CachedDataProvider : IDataProvider, IDisposable
{
    public IMarketMetadata Metadata { get; }
    public bool HasField(string fieldName);
    public IReadOnlyMatrix2D<T> GetMatrix<T>(string fieldName) where T : unmanaged;
    public TimeseriesChunkManager<T> GetChunkManager<T>(string fieldName) where T : unmanaged;
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
    public static ReadOnlyColumnView<T> GetSeries<T>(this IDataProvider provider, string fieldName, int symbolIndex = 0) where T : unmanaged;
    public static ReadOnlyColumnView<T> GetSeries<T>(this IDataProvider provider, string fieldName, string symbol) where T : unmanaged;
}
```

---

## 四、异构事件流系统 (Event Stream System)

针对非对齐、稀疏或结构不规则数据（如复权因子、公告、龙虎榜席位），通过强类型事件流进行点查。

### 1. IEventProvider<T>
```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public interface IEventProvider<T> where T : class
{
    /// <summary>
    /// 依据交易日与股票代码进行 O(1) 索引查询。
    /// </summary>
    bool TryGet(DateTime date, string symbol, out T? value);

    /// <summary>
    /// 获取指定交易日全市场所有发生该事件的品种字典。
    /// </summary>
    bool TryGetDaily(DateTime date, out IReadOnlyDictionary<string, T> values);
}
```

### 2. EventProviderBuilder (自动反射映射工厂)
框架通过读取表元数据自动识别 schema，并利用反射将数据字段映射到 record 构造函数，无需手动编写文件解析器。

```csharp
namespace CarrotBacktesting.NET.Data;

public static class EventProviderBuilder
{
    public static IEventProvider<T> Build<T>(IStorageResolver resolver, string tableId) where T : class;
}
```

### 3. IEventRegistry
全局事件提供器容器中心。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public interface IEventRegistry
{
    IEventProvider<T> GetProvider<T>(string streamName) where T : class;
    bool HasStream(string streamName);
}
```

#### 调用范例
```csharp
// 1. 定义事件数据模型 (Record)
public record AdjustmentFactor(double BackAdjFactor);

// 2. 自动构建并注册事件流
var resolver = new StorageResolver("D:/QuantData");
var adjProvider = EventProviderBuilder.Build<AdjustmentFactor>(resolver, "ashare.adj_factor.baostock");

var registry = new EventRegistry();
registry.Register("adjustments", adjProvider);

// 3. 消费端查询 (O(1))
var provider = registry.GetProvider<AdjustmentFactor>("adjustments");
if (provider.TryGet(new DateTime(2025, 1, 15), "sh.600000", out var factor))
{
    Console.WriteLine($"复权因子: {factor!.BackAdjFactor}");
}
```

---

## 五、引擎与黑板上下文 (Engine & Execution Context)

### 1. IEngineContext (黑板上下文)
策略层运行时的核心宿主环境，隔离具体回测撮合引擎或实盘网关细节。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Engine;

public interface IEngineContext
{
    DateTime CurrentTime { get; }       // 当前时间步
    int CurrentIndex { get; }           // 当前交易日行索引
    IDataProvider Data { get; }         // 行情矩阵输入
    IEventRegistry Events { get; }       // 异构事件流
    IBroker Broker { get; }             // 账户持仓与报单
    IMarketState Market { get; }        // 宏观市场决策黑板
}
```

### 2. IMarketState 与 MarketBias (宏观状态传递)
支持宏观层向微观个股层传递状态，实现条件过滤与剪枝：
- **`MarketBias`**: `Up`（看多）, `Neutral`（中性）, `Down`（看空）。
- **`MarketContext.SkipAlpha`**: 若置为 `true`，引擎跳过当日个股信号运算。
- **`Market.GetState<T>()`**: 读取宏观策略写入的自定义强类型指标对象。

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
    
    event Action<ExecutionReport> OnOrderStatusChanged;
    event Action<string, OrderStatus> OnTrade;
}
```

#### 核心数据模型
- **`OrderRequest`**: `(string Symbol, OrderDirection Direction, OrderType Type, double Price, decimal Quantity)`
- **`ExecutionReport`**: `(string OrderId, string Symbol, double FillPrice, decimal FillQuantity, double Commission, DateTime Time)`
- **`PositionEvent`**: `(string Symbol, decimal NewQuantity, double AveragePrice, DateTime Time)`
- **`OrderStatus`**: `Pending`, `Submitted`, `PartialFilled`, `Filled`, `Canceled`, `Rejected`

---

## 六、策略与管线开发 (Strategy & Pipeline)

### 1. IStrategy 契约
所有策略的基础契约，定义策略在引擎推进过程中的生命周期流转：

```csharp
namespace CarrotBacktesting.NET.Abstraction.Strategy;

public interface IStrategy
{
    string Name { get; }
    IEnumerable<string> GetRequiredColumns() => Array.Empty<string>();
    
    void OnInitial(IEngineContext context) { }
    void OnStart(IEngineContext context) { }
    void OnUpdate(IEngineContext context);                              // 核心时步计算
    void OnStop(IEngineContext context) { }
    void OnOrderChanged(IEngineContext context, ExecutionReport e) { }  // 报单状态回报
    void OnPositionChanged(IEngineContext context, PositionEvent e) { } // 持仓状态变更
}
```

### 2. 典型策略编写范例
```csharp
public class MovingAverageCrossStrategy : IStrategy
{
    public string Name => "MA_Cross_Demo";

    public IEnumerable<string> GetRequiredColumns() => new[] { "close" };

    public void OnUpdate(IEngineContext context)
    {
        // 1. 检查宏观过滤标记
        var marketState = context.Market.GetState<MarketContext>();
        if (marketState?.SkipAlpha == true) return;

        // 2. 获取行情只读矩阵
        var closeMatrix = context.Data.GetMatrix<double>("close");
        int row = context.CurrentIndex;
        if (row < 20) return; // 历史数据不足

        // 3. 个股遍历与信号计算
        for (int col = 0; col < context.Data.Metadata.SymbolCount; col++)
        {
            string symbol = context.Data.Metadata.Symbols[col];
            double todayClose = closeMatrix[row, col];
            double ma20 = 0.0;
            for (int k = 0; k < 20; k++) ma20 += closeMatrix[row - k, col];
            ma20 /= 20.0;

            // 4. 交易执行
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

## 七、流水记录与分析层 (Analysis & Recorder)

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
