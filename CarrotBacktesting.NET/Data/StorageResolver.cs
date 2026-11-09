using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CarrotBacktesting.NET.Abstraction.Data;
using Parquet;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 统一数据区多表路径调度解析器的具体实现。
    /// 内部维护线程安全的 ConcurrentDictionary 作为多表元数据惰性缓存，规避并发读取时的重复 I/O。
    /// 遵循“优先 metadata.json，缺省自描述回退”原则：缺失 metadata.json 时自动从物理文件与目录拓扑推断元数据。
    /// </summary>
    public class StorageResolver : IStorageResolver
    {
        private const string MetadataFileName = "metadata.json";
        private readonly string _storageRoot;
        
        // 线程安全的多表元数据与扫描状态缓存
        private readonly ConcurrentDictionary<string, TableMetadataCache> _cache;

        public string StorageRoot => _storageRoot;

        /// <summary>
        /// 构造函数，初始化统一数据区的存储根目录。
        /// </summary>
        /// <param name="storageRoot">数据区存储根目录</param>
        public StorageResolver(string storageRoot)
        {
            if (string.IsNullOrWhiteSpace(storageRoot))
            {
                throw new ArgumentException("Storage root directory cannot be null or empty.", nameof(storageRoot));
            }
            _storageRoot = Path.GetFullPath(storageRoot);
            _cache = new ConcurrentDictionary<string, TableMetadataCache>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 校验表目录和数据是否存在。优先校验 metadata.json，不存在时检查目录下是否存在数据项。
        /// </summary>
        public bool HasTable(string tableId)
        {
            if (string.IsNullOrWhiteSpace(tableId)) return false;
            if (_cache.ContainsKey(tableId)) return true;

            string tableDir = Path.Combine(_storageRoot, tableId);
            if (!Directory.Exists(tableDir)) return false;

            string metadataPath = Path.Combine(tableDir, MetadataFileName);
            if (File.Exists(metadataPath)) return true;

            return Directory.EnumerateFileSystemEntries(tableDir).Any();
        }

        /// <summary>
        /// 获取元数据 JSON 路径。
        /// </summary>
        public string GetMetadataPath(string tableId)
        {
            return Path.Combine(_storageRoot, tableId, MetadataFileName);
        }

        public string GetFormat(string tableId) => GetOrCreateCache(tableId).Format;

        public StorageLayout GetLayout(string tableId) => GetOrCreateCache(tableId).Layout;

        public IReadOnlyList<string> GetFieldNames(string tableId) => GetOrCreateCache(tableId).FieldNames;

        public Type GetFieldType(string tableId, string fieldName)
        {
            var cache = GetOrCreateCache(tableId);
            if (cache.FieldTypes.TryGetValue(fieldName, out var type))
            {
                return type;
            }
            return typeof(string); // 默认降级为 string 类型
        }

        public string GetCategory(string tableId) => GetOrCreateCache(tableId).Category;

        public string GetPartition(string tableId) => GetOrCreateCache(tableId).Partition;

        /// <summary>
        /// 解析表数据的物理文件路径，支持根据起止日期进行时间分区剪枝与按代码路由。
        /// </summary>
        public IReadOnlyList<string> ResolvePhysicalFiles(string tableId, string? symbol = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            var cache = GetOrCreateCache(tableId);

            if (cache.Layout == StorageLayout.Flat)
            {
                if (cache.Partition.Equals("symbol", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(symbol))
                {
                    string filePath = Path.Combine(cache.TableDir, $"{symbol}.{cache.Format}");
                    return File.Exists(filePath) ? new List<string> { filePath } : new List<string>();
                }
                else
                {
                    string searchPattern = $"*.{cache.Format}";
                    return Directory.EnumerateFiles(cache.TableDir, searchPattern)
                        .OrderBy(file => file)
                        .ToList();
                }
            }
            else
            {
                // Hive 分区布局：动态枚举 year=* 目录并按时间过滤，支持二级 month=* 分区及按月剪枝
                var matchedFiles = new List<string>();
                var yearDirs = Directory.EnumerateDirectories(cache.TableDir, "year=*");

                foreach (var yearDir in yearDirs)
                {
                    string dirName = Path.GetFileName(yearDir);
                    if (int.TryParse(dirName.Substring(5), out int year))
                    {
                        // 年份过滤
                        if (startDate.HasValue && year < startDate.Value.Year) continue;
                        if (endDate.HasValue && year > endDate.Value.Year) continue;

                        string searchPattern = $"*.{cache.Format}";
                        var monthDirs = Directory.EnumerateDirectories(yearDir, "month=*").ToList();

                        if (monthDirs.Count > 0)
                        {
                            foreach (var monthDir in monthDirs)
                            {
                                string monthDirName = Path.GetFileName(monthDir);
                                if (int.TryParse(monthDirName.Substring(6), out int month))
                                {
                                    // 月份过滤
                                    if (startDate.HasValue && year == startDate.Value.Year && month < startDate.Value.Month) continue;
                                    if (endDate.HasValue && year == endDate.Value.Year && month > endDate.Value.Month) continue;

                                    if (cache.Partition.Equals("symbol", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(symbol))
                                    {
                                        string filePath = Path.Combine(monthDir, $"{symbol}.{cache.Format}");
                                        if (File.Exists(filePath))
                                        {
                                            matchedFiles.Add(filePath);
                                        }
                                    }
                                    else
                                    {
                                        matchedFiles.AddRange(Directory.EnumerateFiles(monthDir, searchPattern));
                                    }
                                }
                            }
                        }
                        else
                        {
                            // 单级 year=* 布局
                            if (cache.Partition.Equals("symbol", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(symbol))
                            {
                                string filePath = Path.Combine(yearDir, $"{symbol}.{cache.Format}");
                                if (File.Exists(filePath))
                                {
                                    matchedFiles.Add(filePath);
                                }
                            }
                            else
                            {
                                matchedFiles.AddRange(Directory.EnumerateFiles(yearDir, searchPattern));
                            }
                        }
                    }
                }

                return matchedFiles.OrderBy(f => f).ToList();
            }
        }

        /// <summary>
        /// 惰性加载或获取缓存表的元数据。
        /// </summary>
        private TableMetadataCache GetOrCreateCache(string tableId)
        {
            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new ArgumentException("Table ID cannot be null or empty.", nameof(tableId));
            }
            return _cache.GetOrAdd(tableId, LoadTableMetadata);
        }

        /// <summary>
        /// 惰性加载元数据并解析所有必须的字段。遵循“优先 metadata.json，缺省自描述回退”原则。
        /// </summary>
        private TableMetadataCache LoadTableMetadata(string tableId)
        {
            string tableDir = Path.GetFullPath(Path.Combine(_storageRoot, tableId));
            if (!Directory.Exists(tableDir))
            {
                throw new DirectoryNotFoundException($"Table directory not found: {tableDir}");
            }

            string metadataPath = Path.Combine(tableDir, MetadataFileName);
            if (File.Exists(metadataPath))
            {
                // 1. 优先反序列化已有 metadata.json
                string metaJson = File.ReadAllText(metadataPath);
                var metadata = JsonSerializer.Deserialize<MetadataModel>(metaJson);
                if (metadata != null && metadata.schema != null)
                {
                    string format = (metadata.format ?? string.Empty).ToLowerInvariant();
                    var fieldNames = metadata.schema.Keys.ToList();

                    // 2. 映射 schema 类型
                    var fieldTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in metadata.schema)
                    {
                        fieldTypes[kvp.Key] = MapSchemaType(kvp.Value);
                    }

                    // 3. 布局模式判定
                    StorageLayout layout = !string.IsNullOrWhiteSpace(metadata.layout) && metadata.layout.Equals("hive", StringComparison.OrdinalIgnoreCase)
                        ? StorageLayout.Hive
                        : StorageLayout.Flat;

                    // 4. 解析并规范化 category (timeseries / event)
                    string category = (metadata.category ?? string.Empty).ToLowerInvariant();
                    category = (category == "event") ? "event" : "timeseries";

                    // 5. 解析并规范化 partition (symbol / none)
                    string partition = (metadata.partition ?? string.Empty).ToLowerInvariant();
                    if (partition != "symbol" && partition != "none")
                    {
                        partition = "none";
                    }

                    return new TableMetadataCache(tableId, tableDir, metadataPath, format, layout, fieldNames, fieldTypes, category, partition);
                }
            }

            // 缺省自描述探测：当缺少 metadata.json 时，自动从物理数据文件和目录拓扑推断
            return InferTableMetadata(tableId, tableDir, metadataPath);
        }

        /// <summary>
        /// 缺省自描述回退：通过扫描首个样本物理文件（如 Parquet Footer 或 CSV 表头）与目录拓扑推断元数据。
        /// </summary>
        private TableMetadataCache InferTableMetadata(string tableId, string tableDir, string metadataPath)
        {
            // 1. 布局模式判断：存在 year=* 目录即为 Hive 分区
            bool isHive = Directory.EnumerateDirectories(tableDir, "year=*").Any();
            StorageLayout layout = isHive ? StorageLayout.Hive : StorageLayout.Flat;

            // 2. 寻找首个样本物理文件（支持 Parquet 与 CSV）
            string? sampleFile = isHive
                ? Directory.EnumerateFiles(tableDir, "*.*", SearchOption.AllDirectories)
                    .FirstOrDefault(f => f.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                : Directory.EnumerateFiles(tableDir, "*.*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => f.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(sampleFile))
            {
                throw new FileNotFoundException($"No metadata.json or supported data files (*.parquet, *.csv) found in: {tableDir}");
            }

            string format = Path.GetExtension(sampleFile).TrimStart('.').ToLowerInvariant();
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sampleFile);

            // 3. 分区模式推断：如果物理文件名为 data 则为整表无分区，若为股票代码命名则为 symbol 分区
            string partition = fileNameWithoutExt.Equals("data", StringComparison.OrdinalIgnoreCase) ? "none" : "symbol";

            // 4. 解析 Schema
            var fieldNames = new List<string>();
            var fieldTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            if (format == "parquet")
            {
                using var fs = File.OpenRead(sampleFile);
                var reader = ParquetReader.CreateAsync(fs).GetAwaiter().GetResult();
                try
                {
                    foreach (var field in reader.Schema.GetDataFields())
                    {
                        fieldNames.Add(field.Name);
                        Type rawType = field.ClrType;
                        Type effectiveType = Nullable.GetUnderlyingType(rawType) ?? rawType;
                        fieldTypes[field.Name] = effectiveType;
                    }
                }
                finally
                {
                    reader.DisposeAsync().GetAwaiter().GetResult();
                }
            }
            else if (format == "csv")
            {
                using var sr = new StreamReader(sampleFile);
                string? headerLine = sr.ReadLine();
                if (!string.IsNullOrWhiteSpace(headerLine))
                {
                    var cols = headerLine.Split(',').Select(c => c.Trim().Trim('"')).Where(c => !string.IsNullOrEmpty(c));
                    foreach (var col in cols)
                    {
                        fieldNames.Add(col);
                        string lower = col.ToLowerInvariant();
                        if (lower == "symbol") fieldTypes[col] = typeof(string);
                        else if (lower == "datetime" || lower == "date") fieldTypes[col] = typeof(DateTime);
                        else if (lower == "timestamp") fieldTypes[col] = typeof(long);
                        else fieldTypes[col] = typeof(double);
                    }
                }
            }

            // 5. 默认归属类别为 timeseries
            string category = "timeseries";

            return new TableMetadataCache(tableId, tableDir, metadataPath, format, layout, fieldNames, fieldTypes, category, partition);
        }

        private Type MapSchemaType(string schemaTypeStr)
        {
            return (schemaTypeStr ?? string.Empty).ToLowerInvariant() switch
            {
                "string" => typeof(string),
                "int64" => typeof(long),
                "float64" => typeof(double),
                "date" => typeof(DateTime),
                "datetime" => typeof(DateTime),
                "boolean" => typeof(bool),
                _ => typeof(string)
            };
        }

        /// <summary>
        /// 存储表元数据及物理状态的缓存对象。
        /// </summary>
        private class TableMetadataCache
        {
            public string TableId { get; }
            public string TableDir { get; }
            public string MetadataPath { get; }
            public string Format { get; }
            public StorageLayout Layout { get; }
            public IReadOnlyList<string> FieldNames { get; }
            public Dictionary<string, Type> FieldTypes { get; }
            public string Category { get; }
            public string Partition { get; }

            public TableMetadataCache(string tableId, string tableDir, string metadataPath, string format, StorageLayout layout,
                                      IReadOnlyList<string> fieldNames, Dictionary<string, Type> fieldTypes,
                                      string category, string partition)
            {
                TableId = tableId;
                TableDir = tableDir;
                MetadataPath = metadataPath;
                Format = format;
                Layout = layout;
                FieldNames = fieldNames;
                FieldTypes = fieldTypes;
                Category = category;
                Partition = partition;
            }
        }

        private class MetadataModel
        {
            public string table_id { get; set; } = "";
            public string category { get; set; } = "";
            public string format { get; set; } = "";
            public string? layout { get; set; } = "";
            public string? partition { get; set; } = "";
            public Dictionary<string, string>? schema { get; set; }
        }
    }
}

