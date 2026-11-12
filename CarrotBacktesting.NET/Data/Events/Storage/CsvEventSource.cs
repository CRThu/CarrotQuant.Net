using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using Sylvan.Data.Csv;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// CSV 格式事件数据源驱动。
    /// 基于 Sylvan.Data.Csv 流式读取数据并构建 IEventTable 实例。
    /// </summary>
    public class CsvEventSource : IEventSource
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

                using var reader = CsvDataReader.Create(filePath);
                int symIdx = reader.FindColumn(DefaultDataColumns.SymbolCandidates);
                int dtIdx = reader.FindColumn(DefaultDataColumns.DateTime);
                int tsIdx = reader.FindColumn(DefaultDataColumns.Timestamp);
                var valueColIndices = valueColumns.Select(v => reader.FindColumn(v)).ToArray();

                while (reader.Read())
                {
                    string symbol = symIdx >= 0 ? reader.GetString(symIdx) : "";
                    DateTime date = reader.ParseDate(dtIdx, tsIdx);

                    var values = new string[valueColumns.Count];
                    for (int i = 0; i < valueColumns.Count; i++)
                    {
                        int colIdx = valueColIndices[i];
                        values[i] = colIdx >= 0 && !reader.IsDBNull(colIdx) ? reader.GetString(colIdx) : "";
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

            return new EventTable(tableId, valueColumns, data, globalEvents.Count > 0 ? globalEvents : null);
        }
    }
}
