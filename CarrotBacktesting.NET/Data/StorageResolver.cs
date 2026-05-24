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

        /// <summary>
        /// 终极调度 API：解析表数据物理文件，支持根据起止日期进行年份分区剪枝，并支持按 symbol 智能路由。
        /// </summary>
        public IReadOnlyList<string> ResolvePhysicalFiles(string tableId, string? symbol = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            var cache = GetOrCreateCache(tableId);

            if (cache.Layout == StorageLayout.NonHive)
            {
                // NonHive 平铺布局：根据 symbol 是否为空选择全匹配还是精确查找
                if (string.IsNullOrEmpty(symbol))
                {
                    string searchPattern = $"*.{cache.Format}";
                    return Directory.EnumerateFiles(cache.TableDir, searchPattern)
                        .OrderBy(file => file)
                        .ToList();
                }
                else
                {
                    string filePath = Path.Combine(cache.TableDir, $"{symbol}.{cache.Format}");
                    return File.Exists(filePath) ? new List<string> { filePath } : new List<string>();
                }
            }
            else
            {
                // Hive 分区布局：根据传入起止日期确定年份边界，实现分区剪枝
                int startYear = startDate?.Year ?? 0;
                int endYear = endDate?.Year ?? int.MaxValue;

                var matchedFiles = new List<string>();
                // 仅扫描落在 [startYear, endYear] 范围内的有效年份目录
                foreach (var year in cache.AvailableYears)
                {
                    if (year >= startYear && year <= endYear)
                    {
                        string yearDir = Path.Combine(cache.TableDir, $"year={year}");
                        if (Directory.Exists(yearDir))
                        {
                            if (string.IsNullOrEmpty(symbol))
                            {
                                string searchPattern = $"*.{cache.Format}";
                                var files = Directory.EnumerateFiles(yearDir, searchPattern);
                                matchedFiles.AddRange(files);
                            }
                            else
                            {
                                string filePath = Path.Combine(yearDir, $"{symbol}.{cache.Format}");
                                if (File.Exists(filePath))
                                {
                                    matchedFiles.Add(filePath);
                                }
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
        /// 惰性加载元数据并探测布局模式。
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

            // 3. 布局模式判定：若 JSON 显式指定则依其定义，否则通过 'year=*' 子目录存在性自适应探测
            StorageLayout layout;
            if (!string.IsNullOrWhiteSpace(metadata.layout))
            {
                layout = metadata.layout.Equals("hive", StringComparison.OrdinalIgnoreCase)
                    ? StorageLayout.Hive
                    : StorageLayout.NonHive;
            }
            else
            {
                // 自适应探测：若表目录下存在 "year=*" 的年份文件夹，则判定为 Hive 分区
                layout = Directory.EnumerateDirectories(tableDir, "year=*").Any()
                    ? StorageLayout.Hive
                    : StorageLayout.NonHive;
            }

            // 4. 加载所有可用年份分区 (主要供 Hive 分区剪枝时校验过滤)
            var years = new List<int>();
            if (layout == StorageLayout.Hive)
            {
                foreach (var dir in Directory.EnumerateDirectories(tableDir, "year=*"))
                {
                    string dirName = Path.GetFileName(dir);
                    var parts = dirName.Split('=');
                    if (parts.Length > 1 && int.TryParse(parts[1], out int year))
                    {
                        years.Add(year);
                    }
                }
            }
            var availableYears = years.OrderBy(y => y).ToList();

            return new TableMetadataCache(tableId, tableDir, metadataPath, format, layout, fieldNames, fieldTypes, availableYears);
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
            public IReadOnlyList<int> AvailableYears { get; }

            public TableMetadataCache(string tableId, string tableDir, string metadataPath, string format, StorageLayout layout,
                                      IReadOnlyList<string> fieldNames, Dictionary<string, Type> fieldTypes, IReadOnlyList<int> availableYears)
            {
                TableId = tableId;
                TableDir = tableDir;
                MetadataPath = metadataPath;
                Format = format;
                Layout = layout;
                FieldNames = fieldNames;
                FieldTypes = fieldTypes;
                AvailableYears = availableYears;
            }
        }

        private class MetadataModel
        {
            public string table_id { get; set; } = "";
            public string category { get; set; } = "";
            public string format { get; set; } = "";
            public string? layout { get; set; } = "";
            public Dictionary<string, string>? schema { get; set; }
        }
    }
}

