using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using CarrotBacktesting.NET.Abstraction.Data;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Sylvan.Data.Csv;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 基于内存字典索引的事件提供器实现。
    /// 支持 O(1) 的 (date, symbol) 点查和全市场日快照。
    /// </summary>
    public class EventProvider<T> : IEventProvider<T> where T : class
    {
        private readonly Dictionary<DateTime, Dictionary<string, T>> _data;

        public EventProvider(Dictionary<DateTime, Dictionary<string, T>> data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public bool TryGet(DateTime date, string symbol, out T? value)
        {
            if (_data.TryGetValue(date.Date, out var dayDict))
            {
                return dayDict.TryGetValue(symbol, out value);
            }
            value = null;
            return false;
        }

        public bool TryGetDaily(DateTime date, out IReadOnlyDictionary<string, T> values)
        {
            if (_data.TryGetValue(date.Date, out var dayDict))
            {
                values = dayDict;
                return true;
            }
            values = null!;
            return false;
        }
    }

    /// <summary>
    /// 事件提供器构建工厂。
    /// 通过反射自动将 CSV/Parquet 物理行映射为用户定义的 T 类型。
    /// T 必须具备与 schema value 列名匹配的构造函数参数。
    /// </summary>
    public static class EventProviderBuilder
    {
        private static readonly HashSet<string> KeyColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "symbol", "datetime", "timestamp"
        };

        /// <summary>
        /// 根据 metadata.json 自动检测格式并构建事件提供器。
        /// </summary>
        public static EventProvider<T> Build<T>(IStorageResolver resolver, string tableId) where T : class
        {
            string format = resolver.GetFormat(tableId);
            return format switch
            {
                "csv" => BuildFromCsv<T>(resolver, tableId),
                "parquet" => BuildFromParquet<T>(resolver, tableId),
                _ => throw new NotSupportedException($"Unsupported format: {format}")
            };
        }

        private static EventProvider<T> BuildFromCsv<T>(IStorageResolver resolver, string tableId) where T : class
        {
            var fieldNames = resolver.GetFieldNames(tableId);
            var valueColumns = fieldNames.Where(f => !KeyColumns.Contains(f)).ToList();
            var ctor = ResolveConstructor<T>(valueColumns);
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

                    T record = InvokeConstructor<T>(ctor, valueColumns, values);
                    if (!data.TryGetValue(date, out var dayDict))
                    {
                        dayDict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
                        data[date] = dayDict;
                    }
                    dayDict[symbol] = record;
                }
            }

            return new EventProvider<T>(data);
        }

        private static EventProvider<T> BuildFromParquet<T>(IStorageResolver resolver, string tableId) where T : class
        {
            var fieldNames = resolver.GetFieldNames(tableId);
            var valueColumns = fieldNames.Where(f => !KeyColumns.Contains(f)).ToList();
            var ctor = ResolveConstructor<T>(valueColumns);

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

                            T record = InvokeConstructor<T>(ctor, valueColumns, values);
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

            return new EventProvider<T>(data);
        }

        #region Reflection Helpers

        private static ConstructorInfo ResolveConstructor<T>(List<string> valueColumns) where T : class
        {
            var type = typeof(T);
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0)
                throw new InvalidOperationException($"Type {type.Name} has no public constructor.");

            foreach (var ctor in ctors)
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length == valueColumns.Count)
                {
                    bool match = true;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (!NormalizedEquals(parameters[i].Name!, valueColumns[i]))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return ctor;
                }
            }

            throw new InvalidOperationException(
                $"No constructor of {type.Name} matches value columns [{string.Join(", ", valueColumns)}]. " +
                $"Available constructors: {string.Join("; ", ctors.Select(c => $"({string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})"))}");
        }

        private static T InvokeConstructor<T>(ConstructorInfo ctor, List<string> valueColumns, string[] values) where T : class
        {
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                string valStr = i < values.Length ? values[i] : "";
                args[i] = ConvertValue(valStr, parameters[i].ParameterType);
            }

            return (T)ctor.Invoke(args);
        }

        private static object? ConvertValue(string valStr, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(valStr))
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }

            if (targetType == typeof(string)) return valStr;
            if (targetType == typeof(double)) return double.Parse(valStr, CultureInfo.InvariantCulture);
            if (targetType == typeof(float)) return float.Parse(valStr, CultureInfo.InvariantCulture);
            if (targetType == typeof(long)) return long.Parse(valStr, CultureInfo.InvariantCulture);
            if (targetType == typeof(int)) return int.Parse(valStr, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool)) return bool.Parse(valStr);
            if (targetType == typeof(DateTime)) return DateTime.Parse(valStr, CultureInfo.InvariantCulture);

            return Convert.ChangeType(valStr, targetType, CultureInfo.InvariantCulture);
        }

        private static bool NormalizedEquals(string a, string b)
        {
            return string.Equals(
                a.Replace("_", "").ToLowerInvariant(),
                b.Replace("_", "").ToLowerInvariant(),
                StringComparison.Ordinal);
        }

        #endregion

        #region Physical Parsing Helpers

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

        private static string[] ReadStringColumn(ParquetRowGroupReader groupReader, DataField field, int rowCount)
        {
            var clrType = field.ClrType;
            if (clrType == typeof(string))
            {
                var arr = new string[rowCount];
                groupReader.ReadAsync(field, arr).GetAwaiter().GetResult();
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
            else if (fieldType == typeof(string))
            {
                var arr = new string[rowCount];
                groupReader.ReadAsync(field, arr).GetAwaiter().GetResult();
                for (int i = 0; i < rowCount; i++)
                {
                    if (!string.IsNullOrEmpty(arr[i]) && DateTime.TryParse(arr[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
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

        #endregion
    }
}
