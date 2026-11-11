using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using Sylvan.Data.Csv;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// CSV 格式的异构事件市场数据源驱动（基于 Sylvan.Data.Csv 流式读取与实体反射映射）。
    /// </summary>
    public class CsvEventSource : IEventSource
    {
        public IEventTable<T> LoadTable<T>(IStorageResolver resolver, string tableId) where T : class
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

            var fieldNames = resolver.GetFieldNames(tableId);
            var valueColumns = fieldNames.Where(f => !EventEntityMapper.KeyColumns.Contains(f)).ToList();
            var ctor = EventEntityMapper.ResolveConstructor<T>(valueColumns);
            var valueColIndices = valueColumns.ToDictionary(
                v => v,
                v => Array.IndexOf(fieldNames.ToArray(), v),
                StringComparer.OrdinalIgnoreCase);

            var files = resolver.ResolvePhysicalFiles(tableId);
            var data = new Dictionary<DateTime, Dictionary<string, T>>();

            foreach (var filePath in files)
            {
                if (!File.Exists(filePath)) continue;

                using var reader = CsvDataReader.Create(filePath);
                int symIdx = FindColumn(reader, "symbol");
                int dtIdx = FindColumn(reader, "datetime");
                int tsIdx = FindColumn(reader, "timestamp");

                while (reader.Read())
                {
                    string symbol = symIdx >= 0 ? reader.GetString(symIdx) : "";
                    DateTime date = ParseDate(reader, dtIdx, tsIdx);
                    if (date == DateTime.MinValue) continue;

                    var values = new string[valueColumns.Count];
                    for (int i = 0; i < valueColumns.Count; i++)
                    {
                        int colIdx = valueColIndices[valueColumns[i]];
                        values[i] = colIdx >= 0 && !reader.IsDBNull(colIdx)
                            ? reader.GetString(colIdx)
                            : "";
                    }

                    T record = EventEntityMapper.InvokeConstructor<T>(ctor, valueColumns, values);
                    if (!data.TryGetValue(date, out var dayDict))
                    {
                        dayDict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
                        data[date] = dayDict;
                    }
                    dayDict[symbol] = record;
                }
            }

            return new EventTable<T>(data);
        }

        private static int FindColumn(CsvDataReader reader, string name)
        {
            try { return reader.GetOrdinal(name); }
            catch { return -1; }
        }

        private static DateTime ParseDate(CsvDataReader reader, int dtIdx, int tsIdx)
        {
            if (dtIdx >= 0)
            {
                if (reader.GetFieldType(dtIdx) == typeof(DateTime))
                    return reader.GetDateTime(dtIdx).Date;

                string dtStr = reader.GetString(dtIdx);
                if (DateTime.TryParse(dtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return dt.Date;
            }

            if (tsIdx >= 0 && !reader.IsDBNull(tsIdx))
            {
                string tsStr = reader.GetString(tsIdx);
                if (long.TryParse(tsStr, out long ts))
                    return DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime.Date;
            }

            return DateTime.MinValue;
        }
    }
}
