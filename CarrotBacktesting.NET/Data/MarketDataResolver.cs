using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 市场数据物理路径与元数据解析器的具体实现。
    /// 一次性加载并缓存 schema 元数据，并提供年份及物理文件定位的统一管理，消除数据源类的冗余拼写逻辑。
    /// </summary>
    public class MarketDataResolver : IMarketDataResolver
    {
        private readonly string _storageRoot;
        private readonly string _tableId;
        private readonly string _tableDir;
        private readonly string _metadataPath;
        private readonly string _format;
        private readonly List<string> _fieldNames;
        private readonly Dictionary<string, Type> _fieldTypes;
        private readonly List<int> _availableYears;

        public string StorageRoot => _storageRoot;
        public string TableId => _tableId;
        public string TableDir => _tableDir;
        public string MetadataPath => _metadataPath;
        public string Format => _format;
        public IReadOnlyList<string> FieldNames => _fieldNames;

        /// <summary>
        /// 构造函数，初始化路径解析器并加载元数据。
        /// </summary>
        public MarketDataResolver(string storageRoot, string tableId)
        {
            _storageRoot = storageRoot;
            _tableId = tableId;
            _tableDir = Path.GetFullPath(Path.Combine(storageRoot, tableId));
            
            if (!Directory.Exists(_tableDir))
            {
                throw new DirectoryNotFoundException($"Table directory not found: {_tableDir}");
            }

            _metadataPath = Path.Combine(_tableDir, "metadata.json");
            if (!File.Exists(_metadataPath))
            {
                throw new FileNotFoundException($"Metadata file not found at: {_metadataPath}");
            }

            // 1. 反序列化 metadata.json
            string metaJson = File.ReadAllText(_metadataPath);
            var metadata = JsonSerializer.Deserialize<MetadataModel>(metaJson);
            if (metadata == null || metadata.schema == null)
            {
                throw new InvalidDataException($"Invalid metadata format in: {_metadataPath}");
            }

            _format = metadata.format.ToLowerInvariant();
            _fieldNames = metadata.schema.Keys.ToList();
            
            // 2. 映射 schema 类型
            _fieldTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in metadata.schema)
            {
                _fieldTypes[kvp.Key] = MapSchemaType(kvp.Value);
            }

            // 3. 扫描已存在的所有年份文件夹分区 (year=YYYY)
            var years = new List<int>();
            foreach (var dir in Directory.EnumerateDirectories(_tableDir, "year=*"))
            {
                string dirName = Path.GetFileName(dir);
                if (int.TryParse(dirName.Split('=')[1], out int year))
                {
                    years.Add(year);
                }
            }
            _availableYears = years.OrderBy(y => y).ToList();
        }

        public IReadOnlyList<int> GetAvailableYears() => _availableYears;

        public Type GetFieldType(string fieldName)
        {
            if (_fieldTypes.TryGetValue(fieldName, out var type))
            {
                return type;
            }
            return typeof(string);
        }

        public string GetCsvFilePath(int year, string symbol)
        {
            return Path.Combine(_tableDir, $"year={year}", $"{symbol}.csv");
        }

        public IReadOnlyList<string> EnumerateCsvFiles()
        {
            var files = new List<string>();
            foreach (var year in _availableYears)
            {
                string yearDir = Path.Combine(_tableDir, $"year={year}");
                if (Directory.Exists(yearDir))
                {
                    foreach (var file in Directory.EnumerateFiles(yearDir, "*.csv"))
                    {
                        // 排除类似 data.csv 等非个股数据
                        if (!Path.GetFileNameWithoutExtension(file).Equals("data", StringComparison.OrdinalIgnoreCase))
                        {
                            files.Add(file);
                        }
                    }
                }
            }
            return files;
        }

        public string GetParquetFilePath(int year, int month)
        {
            return Path.Combine(_tableDir, $"year={year}", $"{year}-{month:D2}.parquet");
        }

        public IReadOnlyList<string> EnumerateParquetFiles()
        {
            return Directory.EnumerateFiles(_tableDir, "*.parquet", SearchOption.AllDirectories).ToList();
        }

        private Type MapSchemaType(string schemaTypeStr)
        {
            return schemaTypeStr.ToLowerInvariant() switch
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

        private class MetadataModel
        {
            public string table_id { get; set; } = "";
            public string category { get; set; } = "";
            public string format { get; set; } = "";
            public Dictionary<string, string>? schema { get; set; }
        }
    }
}
