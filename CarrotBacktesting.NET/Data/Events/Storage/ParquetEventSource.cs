using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// Parquet 格式的异构事件市场数据源驱动（基于 Parquet.Net 读取与实体反射映射）。
    /// </summary>
    public class ParquetEventSource : IEventSource
    {
        public IEventTable<T> LoadTable<T>(IStorageResolver resolver, string tableId) where T : class
        {
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableId);

            var fieldNames = resolver.GetFieldNames(tableId);
            var valueColumns = fieldNames.Where(f => !EventEntityMapper.KeyColumns.Contains(f)).ToList();
            var ctor = EventEntityMapper.ResolveConstructor<T>(valueColumns);

            var files = resolver.ResolvePhysicalFiles(tableId);
            var data = new Dictionary<DateTime, Dictionary<string, T>>();

            foreach (var filePath in files)
            {
                if (!File.Exists(filePath)) continue;

                using var fs = File.OpenRead(filePath);
                var reader = ParquetReader.CreateAsync(fs).GetAwaiter().GetResult();
                try
                {
                    var dataFields = reader.Schema.GetDataFields();
                    var symField = dataFields.FirstOrDefault(f => f.Name.Equals("symbol", StringComparison.OrdinalIgnoreCase));
                    var dtField = dataFields.FirstOrDefault(f => f.Name.Equals("datetime", StringComparison.OrdinalIgnoreCase))
                               ?? dataFields.FirstOrDefault(f => f.Name.Equals("timestamp", StringComparison.OrdinalIgnoreCase));
                    var valueFields = valueColumns.Select(name =>
                        dataFields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ).Where(f => f != null).ToArray();

                    for (int g = 0; g < reader.RowGroupCount; g++)
                    {
                        using var groupReader = reader.OpenRowGroupReader(g);
                        int rowCount = (int)groupReader.RowCount;
                        string[] symbols = symField != null ? ReadStringColumn(groupReader, symField, rowCount) : new string[rowCount];
                        DateTime[] dates = dtField != null ? ReadDateColumn(groupReader, dtField, rowCount) : new DateTime[rowCount];
                        string[][] valueArrays = new string[valueFields.Length][];
                        for (int v = 0; v < valueFields.Length; v++)
                            valueArrays[v] = ReadStringColumn(groupReader, valueFields[v]!, rowCount);

                        for (int r = 0; r < rowCount; r++)
                        {
                            string symbol = symbols[r] ?? "";
                            DateTime date = dates[r];
                            if (date == DateTime.MinValue || string.IsNullOrEmpty(symbol)) continue;

                            var values = new string[valueColumns.Count];
                            for (int v = 0; v < valueColumns.Count; v++)
                                values[v] = valueArrays[v]?[r] ?? "";

                            T record = EventEntityMapper.InvokeConstructor<T>(ctor, valueColumns, values);
                            if (!data.TryGetValue(date, out var dayDict))
                            {
                                dayDict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
                                data[date] = dayDict;
                            }
                            dayDict[symbol] = record;
                        }
                    }
                }
                finally
                {
                    reader.DisposeAsync().GetAwaiter().GetResult();
                }
            }

            return new EventTable<T>(data);
        }

        private static string[] ReadStringColumn(ParquetRowGroupReader groupReader, DataField field, int rowCount)
        {
            var clrType = field.ClrType;
            if (clrType == typeof(ReadOnlyMemory<char>))
            {
                var memArr = new ReadOnlyMemory<char>[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawAsync<ReadOnlyMemory<char>>(field, memArr, def, null, default).GetAwaiter().GetResult();
                var arr = new string[rowCount];
                for (int i = 0; i < rowCount; i++)
                {
                    arr[i] = def[i] > 0 ? memArr[i].ToString() : "";
                }
                return arr;
            }

            if (clrType == typeof(double))
            {
                var arr = new double[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawAsync<double>(field, arr, def, null, default).GetAwaiter().GetResult();
                return arr.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();
            }
            if (clrType == typeof(float))
            {
                var arr = new float[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawAsync<float>(field, arr, def, null, default).GetAwaiter().GetResult();
                return arr.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();
            }
            if (clrType == typeof(long))
            {
                var arr = new long[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawAsync<long>(field, arr, def, null, default).GetAwaiter().GetResult();
                return arr.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();
            }
            if (clrType == typeof(int))
            {
                var arr = new int[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawAsync<int>(field, arr, def, null, default).GetAwaiter().GetResult();
                return arr.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray();
            }
            if (clrType == typeof(bool))
            {
                var arr = new bool[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawAsync<bool>(field, arr, def, null, default).GetAwaiter().GetResult();
                return arr.Select(v => v.ToString()).ToArray();
            }
            if (clrType == typeof(DateTimeOffset))
            {
                var arr = new DateTimeOffset[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawAsync<DateTimeOffset>(field, arr, def, null, default).GetAwaiter().GetResult();
                return arr.Select(v => v.ToString("o", CultureInfo.InvariantCulture)).ToArray();
            }

            throw new NotSupportedException($"Parquet column type {clrType} is not supported for event string conversion.");
        }

        private static DateTime[] ReadDateColumn(ParquetRowGroupReader groupReader, DataField field, int rowCount)
        {
            var dates = new DateTime[rowCount];
            var fieldType = field.ClrType;

            if (fieldType == typeof(DateTimeOffset))
            {
                var arr = new DateTimeOffset[rowCount];
                groupReader.ReadRawAsync<DateTimeOffset>(field, arr, new int[rowCount], null, default).GetAwaiter().GetResult();
                for (int i = 0; i < rowCount; i++) dates[i] = arr[i].DateTime.Date;
            }
            else if (fieldType == typeof(long))
            {
                var arr = new long[rowCount];
                var defLevels = new int[rowCount];
                groupReader.ReadRawAsync<long>(field, arr, defLevels, null, default).GetAwaiter().GetResult();
                for (int i = 0; i < rowCount; i++) dates[i] = DateTimeOffset.FromUnixTimeMilliseconds(arr[i]).DateTime.Date;
            }
            else if (fieldType == typeof(ReadOnlyMemory<char>))
            {
                var arr = new ReadOnlyMemory<char>[rowCount];
                var def = new int[rowCount];
                groupReader.ReadRawAsync<ReadOnlyMemory<char>>(field, arr, def, null, default).GetAwaiter().GetResult();
                for (int i = 0; i < rowCount; i++)
                {
                    if (def[i] > 0 && !arr[i].IsEmpty && DateTime.TryParse(arr[i].Span, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        dates[i] = dt.Date;
                }
            }
            else if (fieldType == typeof(DateTime))
            {
                var arr = new DateTime[rowCount];
                groupReader.ReadRawAsync<DateTime>(field, arr, new int[rowCount], null, default).GetAwaiter().GetResult();
                for (int i = 0; i < rowCount; i++) dates[i] = arr[i].Date;
            }

            return dates;
        }
    }
}
