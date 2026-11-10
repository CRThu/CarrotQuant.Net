# 数据存储与格式规范 (Data Storage & Format Specification)

本文档面向数据工程与策略开发，详细说明 CarrotQuant.NET 的底层物理数据组织架构、Table ID 命名规范、数据文件字段契约、`metadata.json` 规范定义以及缺失元数据时的程序缺省回退推断规则。

---

## 目录
- [一、Table ID 命名规范](#一table-id-命名规范)
- [二、物理目录布局模式 (Storage Layout)](#二物理目录布局模式-storage-layout)
- [三、数据文件格式与字段契约](#三数据文件格式与字段契约)
- [四、元数据契约与缺省自描述回退](#四元数据契约与缺省自描述回退)

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

- **适用场景**：低频事件数据（如复权因子、公告）、单文件宽表或小样本数据集。

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
  }
}
```

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

当表目录下**未提供 `metadata.json`** 时，`StorageResolver` 自动扫描目录拓扑并采样首个物理数据文件进行自描述推断：

1. **存储布局推断 (`layout`)**：
   - 若表目录下存在 `year=*` 子目录，自动推断为 `Hive` 分区布局；
   - 否则推断为 `Flat` 平铺布局。
2. **文件格式推断 (`format`)**：
   - 自动获取目录下首个数据文件的扩展名，推断为 `parquet` 或 `csv`。
3. **代码分区推断 (`partition`)**：
   - 若物理文件名以 `data.` 开头（如 `data.parquet` 或 `data.csv`），推断为 `none`（整表无分区）；
   - 若物理文件以股票代码命名（如 `{symbol}.csv`），推断为 `symbol` 代码拆分模式。
4. **字段与类型推断 (`schema`)**：
   - **Parquet**：自动读取首个 Parquet 文件的 Footer Schema，直接提取所有字段名及其对应的 CLR 物理类型（`Int64` -> `long`，`Double` -> `double` 等）；
   - **CSV**：自动读取首行表头获取字段名列表；字段类型按列名规则自动推断：
     - `symbol` -> `typeof(string)`
     - `datetime` / `date` -> `typeof(DateTime)`
     - `timestamp` -> `typeof(long)`
     - 其余数值字段默认推断为 `typeof(double)`。
5. **数据类别推断 (`category`)**：
   - 默认推断为 `timeseries`（时序宽表）。
