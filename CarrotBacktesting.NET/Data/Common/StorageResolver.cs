using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 数据区物理存储调度与元数据注册中心单一内核实现。
    /// 管理指定 StorageRoot 下的多表元数据解析缓存与 Hive 年月分区目录拓扑剪枝，天然并发隔离，随实例作用域释放。
    /// </summary>
    public class StorageResolver : IStorageResolver
    {
        private const string MetadataFileName = "metadata.json";
        private static readonly Regex YearRegex = new(@"^year=(\d{4})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MonthRegex = new(@"^(?:month=)?(\d{1,2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly string _storageRoot;
        private readonly ConcurrentDictionary<string, TableMetadata> _metadataCache;
        private readonly ConcurrentDictionary<string, TablePartitionTopology> _topologyCache;

        /// <inheritdoc />
        public string StorageRoot => _storageRoot;

        /// <summary>
        /// 构造函数，绑定指定数据存储区根目录。
        /// </summary>
        /// <param name="storageRoot">数据区存储根目录</param>
        public StorageResolver(string storageRoot)
        {
            if (string.IsNullOrWhiteSpace(storageRoot))
            {
                throw new ArgumentException("Storage root directory cannot be null or empty.", nameof(storageRoot));
            }
            _storageRoot = Path.GetFullPath(storageRoot);
            _metadataCache = new ConcurrentDictionary<string, TableMetadata>(StringComparer.OrdinalIgnoreCase);
            _topologyCache = new ConcurrentDictionary<string, TablePartitionTopology>(StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public bool HasTable(string tableId)
        {
            if (string.IsNullOrWhiteSpace(tableId)) return false;
            if (_metadataCache.ContainsKey(tableId)) return true;

            string tableDir = Path.Combine(_storageRoot, tableId);
            if (!Directory.Exists(tableDir)) return false;

            string metadataPath = Path.Combine(tableDir, MetadataFileName);
            if (File.Exists(metadataPath)) return true;

            return Directory.EnumerateFileSystemEntries(tableDir).Any();
        }

        /// <inheritdoc />
        public TableMetadata GetMetadata(string tableId)
        {
            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new ArgumentException("Table ID cannot be null or empty.", nameof(tableId));
            }
            return _metadataCache.GetOrAdd(tableId, LoadTableMetadata);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ResolvePhysicalFiles(string tableId, string? symbol = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            var metadata = GetMetadata(tableId);
            return ResolvePhysicalFiles(metadata, symbol, startDate, endDate);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ResolvePhysicalFiles(TableMetadata metadata, string? symbol = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            string ext = metadata.FileExtension;

            if (metadata.Layout == StorageLayout.Flat)
            {
                if (metadata.Partition == PartitionMode.Symbol && !string.IsNullOrEmpty(symbol))
                {
                    string filePath = Path.Combine(metadata.TableDir, $"{symbol}.{ext}");
                    return File.Exists(filePath) ? new[] { filePath } : Array.Empty<string>();
                }
                else
                {
                    string searchPattern = $"*.{ext}";
                    var files = Directory.EnumerateFiles(metadata.TableDir, searchPattern)
                        .OrderBy(file => file)
                        .ToList();
                    return files.Count > 0 ? files : Array.Empty<string>();
                }
            }
            else
            {
                // Hive 分区拓扑：利用缓存的年份与月份目录进行内存快速剪枝，规避反复枚举磁盘目录
                var topology = _topologyCache.GetOrAdd(metadata.TableId, _ => ScanHiveTopology(metadata.TableDir));
                var matchedFiles = new List<string>();
                string searchPattern = $"*.{ext}";
                bool isSymbolPartition = metadata.Partition == PartitionMode.Symbol && !string.IsNullOrEmpty(symbol);

                foreach (var yearInfo in topology.HivePartitions)
                {
                    int year = yearInfo.Year;
                    if (startDate.HasValue && year < startDate.Value.Year) continue;
                    if (endDate.HasValue && year > endDate.Value.Year) continue;

                    if (yearInfo.Months.Count > 0)
                    {
                        foreach (var monthInfo in yearInfo.Months)
                        {
                            int month = monthInfo.Month;
                            if (startDate.HasValue && year == startDate.Value.Year && month < startDate.Value.Month) continue;
                            if (endDate.HasValue && year == endDate.Value.Year && month > endDate.Value.Month) continue;

                            if (isSymbolPartition)
                            {
                                string filePath = Path.Combine(monthInfo.DirectoryPath, $"{symbol}.{ext}");
                                if (File.Exists(filePath))
                                {
                                    matchedFiles.Add(filePath);
                                }
                            }
                            else
                            {
                                matchedFiles.AddRange(Directory.EnumerateFiles(monthInfo.DirectoryPath, searchPattern));
                            }
                        }
                    }
                    else
                    {
                        if (isSymbolPartition)
                        {
                            string filePath = Path.Combine(yearInfo.DirectoryPath, $"{symbol}.{ext}");
                            if (File.Exists(filePath))
                            {
                                matchedFiles.Add(filePath);
                            }
                        }
                        else
                        {
                            matchedFiles.AddRange(Directory.EnumerateFiles(yearInfo.DirectoryPath, searchPattern));
                        }
                    }
                }

                if (matchedFiles.Count == 0) return Array.Empty<string>();
                return matchedFiles.OrderBy(f => f).ToList();
            }
        }

        /// <summary>
        /// 清除指定表或全表的本地元数据与目录拓扑缓存（主要用于动态环境重置或单元测试）。
        /// </summary>
        /// <param name="tableId">可选表 ID，为 null 时清空全量缓存</param>
        public void ClearCache(string? tableId = null)
        {
            if (string.IsNullOrWhiteSpace(tableId))
            {
                _metadataCache.Clear();
                _topologyCache.Clear();
            }
            else
            {
                _metadataCache.TryRemove(tableId, out _);
                _topologyCache.TryRemove(tableId, out _);
            }
        }

        private TableMetadata LoadTableMetadata(string tableId)
        {
            string tableDir = Path.GetFullPath(Path.Combine(_storageRoot, tableId));
            if (!Directory.Exists(tableDir))
            {
                throw new DirectoryNotFoundException($"Table directory not found: {tableDir}");
            }

            string metadataPath = Path.Combine(tableDir, MetadataFileName);
            MetadataModel? metadata = null;
            if (File.Exists(metadataPath))
            {
                try
                {
                    string metaJson = File.ReadAllText(metadataPath);
                    metadata = JsonSerializer.Deserialize<MetadataModel>(metaJson);
                }
                catch
                {
                    // 容错忽略反序列化错误，回退到推断
                }

                if (metadata != null && metadata.schema != null)
                {
                    StorageFormat format = ParseFormat(metadata.format);
                    StorageLayout layout = ParseLayout(metadata.layout);
                    TableCategory category = ParseCategory(metadata.category);
                    PartitionMode partition = ParsePartition(metadata.partition);

                    var fieldNames = metadata.schema.Keys.ToList();
                    var fieldTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in metadata.schema)
                    {
                        fieldTypes[kvp.Key] = MapSchemaType(kvp.Value);
                    }

                    var nullableFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (metadata.nullable_fields != null)
                    {
                        foreach (var f in metadata.nullable_fields)
                        {
                            if (!string.IsNullOrWhiteSpace(f)) nullableFields.Add(f.Trim());
                        }
                    }

                    return new TableMetadata(tableId, tableDir, metadataPath, format, layout, fieldNames, fieldTypes, category, partition, nullableFields);
                }
            }

            // 缺少 metadata.json 或未提供完整 schema 时委托给纯函数推断器
            StorageLayout? preLayout = metadata?.layout != null ? ParseLayout(metadata.layout) : null;
            StorageFormat? preFormat = metadata?.format != null ? ParseFormat(metadata.format) : null;
            PartitionMode? prePartition = metadata?.partition != null ? ParsePartition(metadata.partition) : null;
            TableCategory? preCategory = metadata?.category != null ? ParseCategory(metadata.category) : null;

            HashSet<string>? preNullable = null;
            if (metadata?.nullable_fields != null)
            {
                preNullable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in metadata.nullable_fields)
                {
                    if (!string.IsNullOrWhiteSpace(f)) preNullable.Add(f.Trim());
                }
            }

            return TableMetadataInferer.Infer(tableId, tableDir, metadataPath, preLayout, preFormat, prePartition, preCategory, preNullable);
        }

        private static StorageFormat ParseFormat(string? formatStr)
        {
            if (string.IsNullOrWhiteSpace(formatStr)) throw new ArgumentException("Storage format cannot be null or empty.");
            return formatStr.Trim().ToLowerInvariant() switch
            {
                "parquet" => StorageFormat.Parquet,
                "csv" => StorageFormat.Csv,
                _ => throw new NotSupportedException($"Unsupported storage format: '{formatStr}'. Supported formats are 'parquet' and 'csv'.")
            };
        }

        private static StorageLayout ParseLayout(string? layoutStr)
        {
            if (string.IsNullOrWhiteSpace(layoutStr)) return StorageLayout.Flat;
            return layoutStr.Trim().ToLowerInvariant() switch
            {
                "hive" => StorageLayout.Hive,
                "flat" => StorageLayout.Flat,
                _ => StorageLayout.Flat
            };
        }

        private static TableCategory ParseCategory(string? categoryStr)
        {
            if (string.IsNullOrWhiteSpace(categoryStr)) return TableCategory.Timeseries;
            return categoryStr.Trim().ToLowerInvariant() switch
            {
                "field" or "fields" => TableCategory.Field,
                "event" or "events" => TableCategory.Event,
                "timeseries" => TableCategory.Timeseries,
                _ => TableCategory.Timeseries
            };
        }

        private static PartitionMode ParsePartition(string? partitionStr)
        {
            if (string.IsNullOrWhiteSpace(partitionStr)) return PartitionMode.None;
            return partitionStr.Trim().ToLowerInvariant() switch
            {
                "symbol" => PartitionMode.Symbol,
                "none" => PartitionMode.None,
                _ => PartitionMode.None
            };
        }

        private static Type MapSchemaType(string schemaTypeStr)
        {
            return (schemaTypeStr ?? string.Empty).ToLowerInvariant() switch
            {
                "string" => typeof(string),
                "int64" or "long" => typeof(long),
                "int32" or "int" => typeof(int),
                "float64" or "double" => typeof(double),
                "float32" or "float" => typeof(float),
                "decimal" => typeof(decimal),
                "date" or "datetime" => typeof(DateTime),
                "boolean" or "bool" => typeof(bool),
                _ => typeof(string)
            };
        }

        private static TablePartitionTopology ScanHiveTopology(string tableDir)
        {
            var partitions = new List<HivePartitionInfo>();
            if (!Directory.Exists(tableDir))
            {
                return new TablePartitionTopology(partitions);
            }

            foreach (var yearDir in Directory.EnumerateDirectories(tableDir, "year=*"))
            {
                string dirName = Path.GetFileName(yearDir);
                var match = YearRegex.Match(dirName);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int year))
                {
                    var months = new List<HiveMonthInfo>();
                    foreach (var monthDir in Directory.EnumerateDirectories(yearDir))
                    {
                        string monthSub = Path.GetFileName(monthDir);
                        var mMatch = MonthRegex.Match(monthSub);
                        if (mMatch.Success && int.TryParse(mMatch.Groups[1].Value, out int month) && month >= 1 && month <= 12)
                        {
                            months.Add(new HiveMonthInfo(month, monthDir));
                        }
                    }
                    months.Sort((a, b) => a.Month.CompareTo(b.Month));
                    partitions.Add(new HivePartitionInfo(year, yearDir, months));
                }
            }

            partitions.Sort((a, b) => a.Year.CompareTo(b.Year));
            return new TablePartitionTopology(partitions);
        }

        private sealed class TablePartitionTopology
        {
            public IReadOnlyList<HivePartitionInfo> HivePartitions { get; }
            public TablePartitionTopology(IReadOnlyList<HivePartitionInfo> hivePartitions) => HivePartitions = hivePartitions;
        }

        private sealed class HivePartitionInfo
        {
            public int Year { get; }
            public string DirectoryPath { get; }
            public IReadOnlyList<HiveMonthInfo> Months { get; }
            public HivePartitionInfo(int year, string directoryPath, IReadOnlyList<HiveMonthInfo> months)
            {
                Year = year;
                DirectoryPath = directoryPath;
                Months = months;
            }
        }

        private sealed class HiveMonthInfo
        {
            public int Month { get; }
            public string DirectoryPath { get; }
            public HiveMonthInfo(int month, string directoryPath)
            {
                Month = month;
                DirectoryPath = directoryPath;
            }
        }

        private class MetadataModel
        {
            public string? format { get; set; }
            public string? layout { get; set; }
            public string? category { get; set; }
            public string? partition { get; set; }
            public List<string>? nullable_fields { get; set; }
            public Dictionary<string, string>? schema { get; set; }
        }
    }
}
