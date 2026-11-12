using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// Parquet 格式事件数据源驱动。
    /// 基于 Parquet.Net 批量读取列数据并构建 IEventTable 实例。
    /// </summary>
    public class ParquetEventSource : IEventSource
    {
        public IEventTable LoadTable(
            IStorageResolver resolver,
            string tableId,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

            var fieldNames = resolver.GetFieldNames(tableId);
            var valueColumns = fieldNames.Where(f => !EntityMapper.ReservedEventKeys.Contains(f)).ToList();
            var ordinals = valueColumns.Select((f, idx) => (f, idx))
                .ToDictionary(x => x.f, x => x.idx, StringComparer.OrdinalIgnoreCase);

            var files = resolver.ResolvePhysicalFiles(tableId, null, startDate, endDate);
            var data = new Dictionary<DateTime, Dictionary<string, List<DataRow>>>();
            var globalEvents = new Dictionary<DateTime, List<DataRow>>();

            foreach (var filePath in files)
            {
                if (!File.Exists(filePath)) continue;

                using var fs = File.OpenRead(filePath);
                var reader = ParquetReaderExtensions.OpenParquetReaderSync(fs);
                try
                {
                    var symField = reader.Schema.FindDataFieldIgnoreCase(DefaultDataColumns.SymbolCandidates);
                    var dtField = reader.Schema.FindDataFieldIgnoreCase(DefaultDataColumns.TimeCandidates);
                    var dataFields = reader.Schema.GetDataFields();
                    var valueFields = valueColumns.Select(name =>
                        dataFields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ).ToArray();

                    for (int g = 0; g < reader.RowGroupCount; g++)
                    {
                        using var groupReader = reader.OpenRowGroupReader(g);
                        int rowCount = (int)groupReader.RowCount;
                        string[] symbols = symField != null ? groupReader.ReadStringColumn(symField, rowCount) : new string[rowCount];
                        DateTime[] dates = dtField != null ? groupReader.ReadDateColumn(dtField, rowCount) : new DateTime[rowCount];
                        string?[]?[] valueArrays = new string?[valueColumns.Count][];
                        for (int v = 0; v < valueColumns.Count; v++)
                        {
                            var vf = valueFields[v];
                            valueArrays[v] = vf != null ? groupReader.ReadStringColumn(vf, rowCount) : null;
                        }

                        for (int r = 0; r < rowCount; r++)
                        {
                            string symbol = symbols[r] ?? "";
                            DateTime date = dates[r];

                            var values = new string[valueColumns.Count];
                            for (int v = 0; v < valueColumns.Count; v++)
                            {
                                var colArr = valueArrays[v];
                                values[v] = (colArr != null && r < colArr.Length) ? (colArr[r] ?? "") : "";
                            }

                            var row = new DataRow(symbol, date, values, ordinals);

                            if (string.IsNullOrEmpty(symbol))
                            {
                                if (!globalEvents.TryGetValue(date, out var gList))
                                {
                                    gList = new List<DataRow>();
                                    globalEvents[date] = gList;
                                }
                                gList.Add(row);
                            }
                            else
                            {
                                if (!data.TryGetValue(date, out var dayDict))
                                {
                                    dayDict = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
                                    data[date] = dayDict;
                                }

                                if (!dayDict.TryGetValue(symbol, out var symList))
                                {
                                    symList = new List<DataRow>();
                                    dayDict[symbol] = symList;
                                }
                                symList.Add(row);
                            }
                        }
                    }
                }
                finally
                {
                    reader.DisposeSync();
                }
            }

            return new EventTable(tableId, valueColumns, data, globalEvents.Count > 0 ? globalEvents : null);
        }
    }
}
