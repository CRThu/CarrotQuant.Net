# CarrotQuant .NET API 参考手册 (API Reference)

本文档面向策略研发工程师与框架二次开发者，作为底层接口字典收录 CarrotQuant.NET v4 的核心契约签名与数据模型。

> 💡 **业务原理与使用指南**：若需了解端到端回测流程与配置范例，请参阅 [标准研发工作流 (workflow.md)](workflow.md) 及 [配置体系指南 (configuration.md)](configuration.md)。

---

## 目录
- [一、配置层 (CarrotBacktesting.NET.Config)](#一配置层-carrotbacktestingnetconfig)
- [二、存储调度与数据物理层 (Abstraction.Data & Data)](#二存储调度与数据物理层-abstractiondata--data)
- [三、内存数据供给层 (IDataProvider & Carrot.Memory)](#三内存数据供给层-idataprovider--carrotmemory)
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

## 三、内存数据供给层 (IDataProvider & Carrot.Memory)

### 1. IDataProvider
数据层向引擎与策略提供只读数据访问。返回非托管内存块的只读视图。

```csharp
namespace CarrotBacktesting.NET.Abstraction.Data;

public interface IDataProvider
{
    IMarketMetadata Metadata { get; }
    bool HasField(string fieldName);
    
    /// <summary>
    /// 获取指定字段的全局二维只读 Buffer（Row=交易日，Col=股票代码）。
    /// </summary>
    IReadOnlyBuffer2D<T> GetBuffer<T>(string fieldName) where T : unmanaged;
}
```

### 2. 核心访问模式
`IDataProvider.GetBuffer<T>` 返回来自 `Carrot.Memory` 的 `IReadOnlyBuffer2D<T>`，支持无 GC 压力的批量截面切片与序列点查：

```csharp
// 1. 获取收盘价与成交量矩阵
IReadOnlyBuffer2D<double> closeBuffer = provider.GetBuffer<double>("close");

// 2. 根据元数据索引定位物理坐标
int dateIdx = provider.Metadata.GetDateIndex(new DateTime(2025, 1, 15));
int symbolIdx = provider.Metadata.GetSymbolIndex("sh.600000");

// 3. 常数时间点查 (O(1))
double closePrice = closeBuffer[dateIdx, symbolIdx];

// 4. 行切片（当日全市场截面，零拷贝）
ReadOnlyRowView<double> marketSnapshot = closeBuffer.GetRow(dateIdx);

// 5. 列切片（单只个股全时序序列，零拷贝）
ReadOnlyColumnView<double> stockHistory = closeBuffer.GetColumn(symbolIdx);
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

        // 2. 获取行情只读 Buffer
        var closeBuffer = context.Data.GetBuffer<double>("close");
        int row = context.CurrentIndex;
        if (row < 20) return; // 历史数据不足

        // 3. 个股遍历与信号计算
        for (int col = 0; col < context.Data.Metadata.SymbolCount; col++)
        {
            string symbol = context.Data.Metadata.Symbols[col];
            double todayClose = closeBuffer[row, col];
            double ma20 = 0.0;
            for (int k = 0; k < 20; k++) ma20 += closeBuffer[row - k, col];
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
