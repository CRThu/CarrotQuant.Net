using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using Parquet.Schema;
using Sylvan.Data.Csv;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 静态维表数据源工厂。
    /// 根据存储格式（CSV/Parquet）加载物理数据并构建 IFieldTable 实例。
    /// </summary>
    public static class FieldSourceFactory
    {
        /// <summary>
        /// 基于不可变元数据与统一调度器构建静态维表。
        /// </summary>
        public static IFieldTable Create(
            TableMetadata metadata,
            IStorageResolver resolver,
            string? keyColumn = null,
            IReadOnlySet<string>? nullableFields = null)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(resolver);

            var files = resolver.ResolvePhysicalFiles(metadata);
            var fieldNames = metadata.FieldNames;
            string effectiveKeyCol = ResolveKeyColumn(fieldNames, keyColumn);

            var resolverFields = metadata.NullableFields;
            IReadOnlySet<string> effectiveNullableFields;
            if (nullableFields == null || nullableFields.Count == 0)
            {
                effectiveNullableFields = resolverFields;
            }
            else if (resolverFields == null || resolverFields.Count == 0)
            {
                effectiveNullableFields = nullableFields;
            }
            else
            {
                var merged = new HashSet<string>(resolverFields, StringComparer.OrdinalIgnoreCase);
                foreach (var f in nullableFields) merged.Add(f);
                effectiveNullableFields = merged;
            }

            var data = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
            var ordinals = fieldNames.Select((f, idx) => (f, idx))
                .ToDictionary(x => x.f, x => x.idx, StringComparer.OrdinalIgnoreCase);

            if (metadata.Format == StorageFormat.Csv)
            {
                LoadCsv(files, effectiveKeyCol, fieldNames, ordinals, data);
            }
            else if (metadata.Format == StorageFormat.Parquet)
            {
                LoadParquet(files, effectiveKeyCol, fieldNames, ordinals, data, effectiveNullableFields);
            }
            else
            {
                throw new NotSupportedException($"Unsupported field table storage format: '{metadata.Format}' for table '{metadata.TableId}'.");
            }

            return new FieldTable(metadata.TableId, fieldNames, data);
        }

        /// <summary>
        /// 基于存储调度门面加载并构建静态维表（工厂层自动解构依赖）。
        /// </summary>
        public static IFieldTable Create(
            IStorageResolver resolver,
            string tableId,
            string? keyColumn = null,
            IReadOnlySet<string>? nullableFields = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

            var metadata = resolver.GetMetadata(tableId);
            return Create(metadata, resolver, keyColumn, nullableFields);
        }

        /// <summary>
        /// 基于存储根目录快速构建静态维表。
        /// </summary>
        public static IFieldTable Create(
            string storageRoot,
            string tableId,
            string? keyColumn = null,
            IReadOnlySet<string>? nullableFields = null)
        {
            return Create(new StorageResolver(storageRoot), tableId, keyColumn, nullableFields);
        }

        private static string ResolveKeyColumn(IReadOnlyList<string> fields, string? specified)
        {
            if (!string.IsNullOrWhiteSpace(specified))
            {
                var match = fields.FirstOrDefault(f => f.Equals(specified, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
                throw new KeyNotFoundException($"Specified key column '{specified}' not found in table schema.");
            }

            var symbolCol = fields.FirstOrDefault(f => DefaultDataColumns.SymbolCandidates.Any(c => c.Equals(f, StringComparison.OrdinalIgnoreCase)));
            return symbolCol ?? (fields.Count > 0 ? fields[0] : "key");
        }

        private static void LoadCsv(
            IReadOnlyList<string> files,
            string keyCol,
            IReadOnlyList<string> fieldNames,
            Dictionary<string, int> ordinals,
            Dictionary<string, List<DataRow>> data)
        {
            foreach (var filePath in files)
            {
                if (!File.Exists(filePath)) continue;

                using var reader = CsvDataReader.Create(filePath);
                var colMap = reader.GetOrdinalMap();
                if (!colMap.TryGetValue(keyCol, out int keyIdx))
                {
                    throw new InvalidOperationException(
                        $"Key column '{keyCol}' not found in CSV file '{filePath}'. Available columns: [{string.Join(", ", colMap.Keys)}]");
                }

                var colIndices = fieldNames.Select(f => colMap.TryGetValue(f, out int idx) ? idx : -1).ToArray();

                while (reader.Read())
                {
                    string key = !reader.IsDBNull(keyIdx) ? reader.GetString(keyIdx) : "";
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    var values = new string[fieldNames.Count];
                    for (int i = 0; i < fieldNames.Count; i++)
                    {
                        int cIdx = colIndices[i];
                        values[i] = cIdx >= 0 && !reader.IsDBNull(cIdx) ? reader.GetString(cIdx) : "";
                    }

                    var row = new DataRow(key, values, ordinals);

                    if (!data.TryGetValue(key, out var list))
                    {
                        list = new List<DataRow>();
                        data[key] = list;
                    }
                    list.Add(row);
                }
            }
        }

        private static void LoadParquet(
            IReadOnlyList<string> files,
            string keyCol,
            IReadOnlyList<string> fieldNames,
            Dictionary<string, int> ordinals,
            Dictionary<string, List<DataRow>> data,
            IReadOnlySet<string>? nullableFields = null)
        {
            foreach (var filePath in files)
            {
                if (!File.Exists(filePath)) continue;

                using var fs = File.OpenRead(filePath);
                var reader = ParquetReaderExtensions.OpenParquetReaderSync(fs);
                try
                {
                    var dataFields = reader.Schema.GetDataFields();
                    var keyField = reader.Schema.FindDataFieldIgnoreCase(keyCol);
                    if (keyField == null)
                    {
                        throw new InvalidOperationException(
                            $"Key column '{keyCol}' not found in Parquet schema of '{filePath}'.");
                    }

                    var valueFields = fieldNames.Select(name =>
                        dataFields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ).ToArray();

                    for (int g = 0; g < reader.RowGroupCount; g++)
                    {
                        using var groupReader = reader.OpenRowGroupReader(g);
                        int rowCount = (int)groupReader.RowCount;
                        string[] keys = groupReader.ReadStringColumn(keyField, rowCount, nullableFields);

                        string?[]?[] valueArrays = new string?[fieldNames.Count][];
                        for (int v = 0; v < fieldNames.Count; v++)
                        {
                            var vf = valueFields[v];
                            valueArrays[v] = vf != null ? groupReader.ReadStringColumn(vf, rowCount, nullableFields) : null;
                        }

                        for (int r = 0; r < rowCount; r++)
                        {
                            string key = keys[r] ?? "";
                            if (string.IsNullOrWhiteSpace(key)) continue;

                            var values = new string[fieldNames.Count];
                            for (int v = 0; v < fieldNames.Count; v++)
                            {
                                var colArr = valueArrays[v];
                                values[v] = (colArr != null && r < colArr.Length) ? (colArr[r] ?? "") : "";
                            }

                            var row = new DataRow(key, values, ordinals);

                            if (!data.TryGetValue(key, out var list))
                            {
                                list = new List<DataRow>();
                                data[key] = list;
                            }
                            list.Add(row);
                        }
                    }
                }
                finally
                {
                    reader.DisposeSync();
                }
            }
        }
    }
}
