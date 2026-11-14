# 数据存储与格式规范 (Data Storage & Format Specification)

本文档面向数据工程与策略开发，详细说明 CarrotQuant.NET 的底层物理数据组织架构、Table ID 命名规范、数据文件字段契约、`metadata.json` 规范定义以及缺失元数据时的程序缺省回退推断规则。

---

## 目录
- [一、Table ID 命名规范](#一table-id-命名规范)
- [二、物理目录布局模式 (Storage Layout)](#二物理目录布局模式-storage-layout)
- [三、数据文件格式与字段契约](#三数据文件格式与字段契约)
- [四、元数据契约与缺省自描述回退](#四元数据契约与缺省自描述回退)
- [五、时序二进制分块缓存规范 (.cache)](#五时序二进制分块缓存规范-cache)

---

## 一、Table ID 命名规范

系统使用点分格式的 `TableId` 作为数据表的统一寻址标识：

```
{market}.{category}.[sub_category/freq/adj].{source}
```

### 字段说明
- **`market`**：市场代码。例如 `ashare`（A 股）、`hkex`（港股）、`us`（美股）。
- **`category`**：数据大类。例如 `kline`（行情 K 线）、`adj_factor`（复权因子）、`tick`（逐笔数据）、`dragon_tiger`（龙虎榜）。
- **`sub_category/freq/adj`**（可选段）：周期、口径或子分类。例如 `1d.raw`（日线原始不复权）、`1m.raw`（1分钟线原始）、`1d.adj`（日线后复权）。
- **`source`**：数据源或厂商。例如 `baostock`、`tdx`、`eastmoney`、`custom`。

### 常用命名对照表
| Table ID | 数据类型 | 周期/口径 | 数据源 |
| :--- | :--- | :--- | :--- |
| `ashare.kline.1d.raw.baostock` | A 股 K 线 | 日线 / 原始不复权 | BaoStock |
| `ashare.kline.1m.raw.custom` | A 股 K 线 | 1分钟 / 原始不复权 | 自定义数据源 |
| `ashare.kline.1d.adj.baostock` | A 股 K 线 | 日线 / 后复权 | BaoStock |
| `ashare.adj_factor.baostock` | 复权因子事件流 | 事件级 / 后复权因子 | BaoStock |
| `ashare.dragon_tiger.eastmoney`| 龙虎榜事件流 | 每日异动 | 东方财富 |
| `ashare.industry.eastmoney`    | 标的行业维表 | 平铺维表 (静态) | 东方财富 |
| `ashare.concept.eastmoney`     | 标的概念维表 | 平铺维表 (静态) | 东方财富 |

---

## 二、物理目录布局模式 (Storage Layout)

数据表存放在配置指定的 `root` 根目录下，目录名称与 `table_id` 一致。根据时间跨度与数据量级，系统支持两种目录布局：

### 模式 1：Hive 分区布局 (`layout: "hive"`)
通过年份（以及可选月份）拆分子目录，支持引擎在加载时依据回测起止时间执行**分区剪枝 (Partition Pruning)**，跳过非回测时间范围的目录扫描。

```
# 方式 A：单级年份分区（推荐：日线行情）
{root}/{table_id}/
├── metadata.json (可选)
├── year=2024/
│   └── data.parquet (或 {symbol}.csv)
└── year=2025/
    └── data.parquet (或 {symbol}.csv)

# 方式 B：年/月二级分区（推荐：分钟线与高频时序）
{root}/{table_id}/
├── metadata.json (可选)
└── year=2025/
    ├── month=01/
    │   └── data.parquet (或 {symbol}.csv)
    └── month=02/
        └── data.parquet (或 {symbol}.csv)
```

- **适用场景**：分钟线、Tick 数据或跨越多年的大体量时序行情。
- **子目录命名**：遵循 `year=YYYY` 与 `month=MM`（两位数字补零）。

### 模式 2：Flat 平铺布局 (`layout: "flat"`)
所有数据文件直接平铺在表根目录下，不分子目录。

```
{root}/{table_id}/
├── metadata.json (可选)
└── data.parquet (或 data.csv / {symbol}.csv)
```

- **适用场景**：静态平铺维表（如东财行业对应表、概念对应表）、低频事件数据（如复权因子、公告）、单文件宽表或小样本数据集。标的维表通常不包含时间戳，默认主键为 `symbol`。

### 分区维度模式 (`partition`)
- **`partition: "none"`**：整表存储为单个或按年划分的单文件（通常命名为 `data.parquet` 或 `data.csv`），文件内部包含全市场代码数据。
- **`partition: "symbol"`**：物理文件按股票代码拆分存储，文件名为 `{symbol}.csv`（例如 `sh.600000.csv`）。

---

## 三、数据文件格式与字段契约

### 1. 文件格式
- **CSV**：逗号分隔文本，UTF-8 编码，第一行为字段表头。
- **Parquet**：列式存储文件，支持标准 Snappy/ZSTD 压缩。

### 2. 双时间轴强制契约
无论时序宽表还是异构事件流，数据行必须包含以下基础对齐字段：

| 字段名 | 推荐类型 | 格式与说明 |
| :--- | :--- | :--- |
| `symbol` | `String` | 统一品种代码，带市场前缀。例如 `sh.600000`、`sz.000001`。 |
| `datetime` | `String` / `Datetime` | 可读时间字符串。标准格式：`YYYY-MM-DDTHH:mm:ss.fff+08:00` 或日线标准 `YYYY-MM-DD`。 |
| `timestamp`| `Int64` | 毫秒级 Unix 时间戳（自 1970-01-01 00:00:00 UTC 起算的毫秒数）。用于高速数值比对与索引对齐。 |

### 3. 复权口径规范
- **禁止使用前复权数据**：前复权随除权除息导致历史价格发生未来漂移，产生未来函数。
- **标准口径**：
  - 行情宽表统一存储原始不复权价格（`raw`）；
  - 需要复权调整时，通过挂载 `ashare.adj_factor.*` 事件流提供的后复权因子进行动态折算。

### 4. 停牌与缺失数据契约（前置 ffill 与零隐式伪造）
- **停牌向前沿用（ffill）必须由上游前置完成**：
  若策略或模型需要在股票停牌期间沿用上一交易日收盘价（Forward Fill），**必须由上游数据清洗管线（如离线 Python / Polars ETL 脚本）预先执行 `ffill()` 处理完毕后再落盘存入宽表**。
- **回测框架数据层零隐式处理（第一性原理）**：
  回测框架数据层（`Data Layer`）严格忠实呈现物理文件真相，**不对缺失数据执行任何跨日隐式复制或伪造**。物理文件中缺失的数据单元格（停牌、未上市、已退市、漏采），在加载至内存二维矩阵（`ChunkMatrix2D<T>`）时一律严格置为 `NaN`（或整型哨兵）。
- **撮合与执行层行为**：
  回测撮合引擎（`IMatchingEngine`）若读取到标的当前行情为 `NaN`，权威判定为不可交易状态（停牌拒绝下单/撮合），杜绝因底层静默前向填充导致的虚假流动性风险。

---

## 四、元数据契约与缺省自描述回退

`metadata.json` 用于显式定义表的存储格式与 Schema。该文件**并非强制必需**。存储调度器 (`StorageResolver`) 遵循**“优先 metadata.json，缺省自描述回退”**原则。

### 1. 显式声明模式 (`metadata.json`)

当表目录下存在 `metadata.json` 时，加载器直接读取该配置：

```json
{
  "table_id": "ashare.kline.1d.raw.baostock",
  "category": "timeseries",
  "format": "parquet",
  "layout": "hive",
  "partition": "none",
  "schema": {
    "symbol": "String",
    "datetime": "Datetime",
    "timestamp": "Int64",
    "open": "Float64",
    "high": "Float64",
    "low": "Float64",
    "close": "Float64",
    "volume": "Float64"
  },
  "nullable_fields": ["close", "volume"]
}
```

> [!IMPORTANT]
> **零空值强契约（Zero-Tolerance NULL Policy）**：
> - **默认行为**：系统默认严禁任何数据列包含 NULL 值。如果 Parquet 数据在解包时检测到任何未在 `nullable_fields` 白名单中的字段包含 NULL，系统将立即以**方案 C（统计损坏总数 + 打印前 5 个损坏样本行号）**抛出强类型 `InvalidDataException` 阻断加载，杜绝坏数据隐蔽穿透。
> - **显式放行**：只有在 `metadata.json` 的 `nullable_fields` 数组或会话配置（`SessionConfig`）中显式声明的字段，才被允许包含空值，并在加载时安全映射为标准缺失哨兵（浮点转 `NaN`，文本转空字符串 `""`，日期转 `DateTime.MinValue`，整型转 `0`）。

#### 支持的数据类型映射字典

| Schema 类型声明 | .NET 类型 | Polars 类型 | 说明 |
| :--- | :--- | :--- | :--- |
| `Int64` | `long` | `pl.Int64` | 整数、毫秒时间戳 |
| `Float64` | `double` | `pl.Float64` | 双精度浮点（价格、成交量、因子） |
| `String` | `string` | `pl.String` | 代码、文本 |
| `Boolean` | `bool` | `pl.Boolean` | 布尔状态位 |
| `Date` | `DateTime` / `DateOnly` | `pl.Date` | 日度日期 |
| `Datetime` | `DateTime` | `pl.Datetime`| 毫秒/微秒日期时间 |

---

### 2. 缺省自描述回退模式 (Fallback Inference)

当表目录下**未提供 `metadata.json`** 时，`StorageResolver` 自动委托内部无状态纯函数推断器（`TableMetadataInferer`）流式扫描目录拓扑并采样首个物理数据文件进行自描述推断，生成不可变 `TableMetadata` 实体：

1. **存储布局推断 (`StorageLayout`)**：
   - 若表目录下存在 `year=*` 子目录，自动推断为 `StorageLayout.Hive` 分区布局；
   - 否则推断为 `StorageLayout.Flat` 平铺布局。
2. **文件格式推断 (`StorageFormat`)**：
   - 自动获取目录下首个数据文件的扩展名，推断为 `StorageFormat.Parquet` 或 `StorageFormat.Csv`。
3. **代码分区推断 (`PartitionMode`)**：
   - 若物理文件名以 `data.` 开头（如 `data.parquet` 或 `data.csv`），推断为 `PartitionMode.None`（整表无分区）；
   - 若物理文件以股票代码命名（如 `{symbol}.csv`），推断为 `PartitionMode.Symbol` 代码拆分模式。
4. **字段与类型推断 (`schema`)**：
   - **Parquet**：自动流式读取首个 Parquet 文件的 Footer Schema，直接提取所有字段名及其对应的 CLR 物理类型（`Int64` -> `long`，`Double` -> `double` 等）；
   - **CSV**：自动读取首行表头获取字段名列表；字段类型按列名规则自动推断：
     - `symbol` 候选列 -> `typeof(string)`
     - `datetime` / `date` 候选列 -> `typeof(DateTime)`
     - `timestamp` 候选列 -> `typeof(long)`
     - 其余数值字段默认推断为 `typeof(double)`。
5. **数据类别推断 (`TableCategory`)**：
   - 默认推断为 `TableCategory.Timeseries`（时序宽表）。

---

## 五、时序二进制分块缓存规范 (.cache)

针对大规模回测中重复解析 CSV/Parquet 的 I/O 瓶颈，系统在数据层引入了基于 4096 行定长对齐的二进制分块缓存机制。

### 1. 缓存物理目录布局
缓存文件默认组织在数据存储区或表级目录下的 `.cache` 隐式目录中：

```
{root}/.cache/{table_id}/
└── {field_name}/                           # 按字段隔离存储（如 close, open, volume）
    ├── meta.json                           # 缓存元数据描述契约
    ├── chunk_0.bin                         # 0 ~ 4095 行二进制密集数据块
    ├── chunk_1.bin                         # 4096 ~ 8191 行二进制密集数据块
    └── chunk_2.bin                         # 增量或尾部行数据块
```

### 2. 元数据描述契约 (`meta.json`)
记录数据维度、分页规模、压缩编码与标的清单，保证加载时的强类型校验与增量比对：

```json
{
  "table_id": "ashare.kline.1d.raw.baostock",
  "field_name": "close",
  "data_type": "Double",
  "compression": "lz4",
  "page_size": 4096,
  "width": 5000,
  "row_count": 5200,
  "start_date": "2021-01-04T00:00:00",
  "end_date": "2024-12-31T00:00:00",
  "symbols": ["sh.600000", "sz.000001", "..."],
  "trade_dates": ["2021-01-04", "..."]
}
```

### 3. 分块与存储编码 (`compression`)
- **存储排布**：每个 `chunk_{i}.bin` 内部按 Row-Major 行优先连续存储。
- **LZ4 块压缩（默认推荐，`compression: "lz4"`）**：
  - **物理二进制布局**：`[4 字节原始未压缩字节长度 (Little-Endian)] + [LZ4 压缩 Payload]`。
  - **吞吐与压缩比**：解压缩速度达 3~5 GB/s（消除纯裸 I/O 吞吐瓶颈），量价时序矩阵实测压缩率超过 50%（体积减少 60%~70%）。
- **裸二进制模式 (`compression: "none"`)**：
  - 直接写入未经压缩的非托管连续内存，单块尺寸严格为 `rowsInChunk * width * sizeof(T)` 字节。
- **单次零拷贝批量 I/O**：
  - 写入与读取均基于块级纯物理连续切片（`GetPageSpan` / `GetReadOnlyPageSpan`），通过单次系统调用（`FileStream.Write` / `FileStream.ReadExactly`）配合非托管裸内存批量灌入，消除逐行循环开销。

### 4. 非满数据持久化与稀疏数值精度
- **尾部分块精确截断**：
  - 数据总行数通常非分页大小整倍数（例如 10,000 行在 4096 页大小下分为两满页与一尾页 1,808 行）；
  - 尾页仅按实际有效行数 `rowsInChunk * width * sizeof(T)` 压缩落盘，末尾不填充脏零，避免污染实际行索引。
- **极端与稀疏数值保留**：
  - 股票晚挂牌上市前（前期未交易）、退市摘牌后（后期无交易）均以标准 IEEE 754 `double.NaN` 填充；
  - 停牌交易日（成交量为 `0.0` 或价格沿用）如实记录；
  - LZ4 块压缩能 100% 无损保持 IEEE 754 浮点数位精度（包含各类 NaN 标志位），杜绝精度损失。

### 5. 全局标的元数据单一事实来源 (SSOT) 与列维度演进契约
- **“只多不少”（只增不减）原则**：
  - 在整个量化回测与实盘生命周期中，标的代码（Symbols）由全局元数据统一定义权威列索引（`SymbolIndex`）；
  - **退市标的绝不剔除**：退市股票永久保留其在元数据中的固定列索引，退市后的交易日填充 `NaN`。严禁从代码列表中剔除退市股票，防止引入严重的**幸存者偏差（Survivorship Bias）**或引起历史列索引错位；
  - 标的集合在时间轴上具备单调递增性，**只多不少**。
- **唯一 Index 与分块自包含宽度契约（Scheme B）**：
  - 每只股票在元数据中拥有全局唯一的固定 `index`。新增上市公司（IPO）均分配单调递增的新索引并在尾部追加，已有股票的索引位置绝对守恒；
  - **分块独立自描述宽度 (Variable Width per Bin)**：每个 `chunk_{i}.bin` 在磁盘上仅按其所属历史阶段真实存在的股票数量紧凑存储，`meta.json` 记录 `chunk_widths` 数组；
  - **单分块精准重刷 (RewriteChunk)**：当新股上市或股票池维度扩增时，**物理上仅重写当前活跃分块（单个 bin）**，历史所有分块磁盘零触碰、零 I/O 放大；杜绝为了新股在历史分块中漫天填充无意义 `0` 或 `NaN` 导致的存储膨胀；
  - **载入内存自动拓展 (Auto-expand on Load)**：引擎在内存中分配全局统一列宽的 `ChunkMatrix2D`，历史窄分块载入内存时以行切片批量拷贝并自动对未上市股票填充 `NaN`，实现“历史只读物理免刷、内存自动无缝对齐”。

### 6. 缓存生命周期刷新策略 (`refresh`)
- **`lazy`**（默认）：若缓存文件与 `meta.json` 存在且兼容则直接秒级加载；缺失时构建；
- **`auto`**：比对原始数据最新时间戳与代码范围。若检测到新交易日或增量代码，增量调用 `AppendRows` / 追加列，无需全量重刷历史数据；
- **`force`**：显式清空现有缓存分块，重新从原始数据源全量重建。
