using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 统一数据区多表路径调度解析器的具体实现。
    /// 内部维护线程安全的 ConcurrentDictionary 作为多表元数据惰性缓存，规避并发读取时的重复 I/O，并自适应探测物理分区结构。
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
        /// 校验表目录和元数据是否存在。
        /// </summary>
        public bool HasTable(string tableId)
        {
            if (string.IsNullOrWhiteSpace(tableId)) return false;
            if (_cache.ContainsKey(tableId)) return true;

            string tableDir = Path.Combine(_storageRoot, tableId);
            string metadataPath = Path.Combine(tableDir, MetadataFileName);
            return Directory.Exists(tableDir) && File.Exists(metadataPath);
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
        /// 终极调度 API：解析表数据物理文件，支持根据起止日期进行年份分区剪枝，并支持按 symbol 智能路由。
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
                // Hive 分区布局：动态枚举 year=* 目录并按时间过滤，不再依赖元数据中的起止时间戳
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

                        // 如果分区模式是 symbol 且指定了 symbol，精准定位
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
                            // 否则获取年份目录下所有文件
                            string searchPattern = $"*.{cache.Format}";
                            var files = Directory.EnumerateFiles(yearDir, searchPattern);
                            matchedFiles.AddRange(files);
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
        /// 惰性加载元数据并解析所有必须的字段，彻底去除物理目录自适应探测。
        /// </summary>
        private TableMetadataCache LoadTableMetadata(string tableId)
        {
            string tableDir = Path.GetFullPath(Path.Combine(_storageRoot, tableId));
            if (!Directory.Exists(tableDir))
            {
                throw new DirectoryNotFoundException($"Table directory not found: {tableDir}");
            }

            string metadataPath = Path.Combine(tableDir, MetadataFileName);
            if (!File.Exists(metadataPath))
            {
                throw new FileNotFoundException($"Metadata file not found at: {metadataPath}");
            }

            // 1. 反序列化 metadata.json
            string metaJson = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<MetadataModel>(metaJson);
            if (metadata == null || metadata.schema == null)
            {
                throw new InvalidDataException($"Invalid metadata format in: {metadataPath}");
            }

            string format = (metadata.format ?? string.Empty).ToLowerInvariant();
            var fieldNames = metadata.schema.Keys.ToList();

            // 2. 映射 schema 类型
            var fieldTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in metadata.schema)
            {
                fieldTypes[kvp.Key] = MapSchemaType(kvp.Value);
            }

            // 3. 布局模式判定
            StorageLayout layout;
            if (!string.IsNullOrWhiteSpace(metadata.layout))
            {
                layout = metadata.layout.Equals("hive", StringComparison.OrdinalIgnoreCase)
                    ? StorageLayout.Hive
                    : StorageLayout.Flat;
            }
            else
            {
                layout = StorageLayout.Flat;
            }

            // 4. 解析并规范化 category (timeseries / events)
            string category = (metadata.category ?? string.Empty).ToLowerInvariant();
            if (category == "ts" || category == "timeseries")
            {
                category = "timeseries";
            }
            else if (category == "events")
            {
                category = "events";
            }

            // 5. 解析并规范化 partition (symbol / date / none)
            string partition = (metadata.partition ?? string.Empty).ToLowerInvariant();
            if (partition != "symbol" && partition != "date" && partition != "none")
            {
                partition = "none";
            }

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

