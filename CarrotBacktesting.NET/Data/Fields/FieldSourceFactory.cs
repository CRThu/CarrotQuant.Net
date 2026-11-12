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
        /// 加载并构建静态维表。
        /// </summary>
        public static IFieldTable Create(IStorageResolver resolver, string tableId, string? keyColumn = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

            string format = resolver.GetFormat(tableId);
            var files = resolver.ResolvePhysicalFiles(tableId);
            var fieldNames = resolver.GetFieldNames(tableId);
            string effectiveKeyCol = ResolveKeyColumn(fieldNames, keyColumn);

            var data = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
            var ordinals = fieldNames.Select((f, idx) => (f, idx))
                .ToDictionary(x => x.f, x => x.idx, StringComparer.OrdinalIgnoreCase);

            if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                LoadCsv(files, effectiveKeyCol, fieldNames, ordinals, data);
            }
            else if (format.Equals("parquet", StringComparison.OrdinalIgnoreCase))
            {
                LoadParquet(files, effectiveKeyCol, fieldNames, ordinals, data);
            }
            else
            {
                throw new NotSupportedException($"Unsupported field table storage format: '{format}' for table '{tableId}'.");
            }

            return new FieldTable(tableId, fieldNames, data);
        }

        /// <summary>
        /// 基于存储根目录快速构建静态维表。
        /// </summary>
        public static IFieldTable Create(string storageRoot, string tableId, string? keyColumn = null)
        {
            return Create(new StorageResolver(storageRoot), tableId, keyColumn);
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
            Dictionary<string, List<DataRow>> data)
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
                        string[] keys = groupReader.ReadStringColumn(keyField, rowCount);

                        string?[]?[] valueArrays = new string?[fieldNames.Count][];
                        for (int v = 0; v < fieldNames.Count; v++)
                        {
                            var vf = valueFields[v];
                            valueArrays[v] = vf != null ? groupReader.ReadStringColumn(vf, rowCount) : null;
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
