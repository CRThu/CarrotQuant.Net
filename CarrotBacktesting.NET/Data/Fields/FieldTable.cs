using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 静态标的维表实现。提供主键查询、倒排索引及实体映射。
    /// </summary>
    public class FieldTable : IFieldTable
    {
        private readonly Dictionary<string, IReadOnlyList<IDataRow>> _data;
        private readonly Dictionary<string, Dictionary<string, IReadOnlyList<string>>> _invertedIndex;
        private readonly Dictionary<string, IReadOnlyList<string>> _globalInvertedIndex;
        private readonly ConcurrentDictionary<Type, object> _factoryCache = new();

        public string Name { get; }
        public IReadOnlyCollection<string> Columns { get; }
        public int Count => _data.Count;
        public IEnumerable<string> Keys => _data.Keys;

        public FieldTable(
            string name,
            IReadOnlyCollection<string> columns,
            Dictionary<string, List<DataRow>> data)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Columns = columns ?? throw new ArgumentNullException(nameof(columns));
            ArgumentNullException.ThrowIfNull(data);

            _data = new Dictionary<string, IReadOnlyList<IDataRow>>(data.Count, StringComparer.OrdinalIgnoreCase);
            var rawInverted = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
            var rawGlobal = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, rows) in data)
            {
                if (rows == null || rows.Count == 0) continue;

                _data[key] = rows.ToArray();
                foreach (var row in rows)
                {
                    foreach (var col in row.Columns)
                    {
                        string val = row.GetString(col);
                        if (string.IsNullOrEmpty(val)) continue;

                        // 1. 列级别倒排索引
                        if (!rawInverted.TryGetValue(col, out var valDict))
                        {
                            valDict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                            rawInverted[col] = valDict;
                        }

                        if (!valDict.TryGetValue(val, out var keyList))
                        {
                            keyList = new List<string>();
                            valDict[val] = keyList;
                        }

                        if (!keyList.Contains(key, StringComparer.OrdinalIgnoreCase))
                        {
                            keyList.Add(key);
                        }

                        // 2. 全局（全列）倒排索引
                        if (!rawGlobal.TryGetValue(val, out var globalKeyList))
                        {
                            globalKeyList = new List<string>();
                            rawGlobal[val] = globalKeyList;
                        }

                        if (!globalKeyList.Contains(key, StringComparer.OrdinalIgnoreCase))
                        {
                            globalKeyList.Add(key);
                        }
                    }
                }
            }

            // 冻结为只读集合，物理隔离
            _invertedIndex = new Dictionary<string, Dictionary<string, IReadOnlyList<string>>>(rawInverted.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (col, dict) in rawInverted)
            {
                var frozenDict = new Dictionary<string, IReadOnlyList<string>>(dict.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var (val, list) in dict)
                {
                    frozenDict[val] = list.ToArray();
                }
                _invertedIndex[col] = frozenDict;
            }

            _globalInvertedIndex = new Dictionary<string, IReadOnlyList<string>>(rawGlobal.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (val, list) in rawGlobal)
            {
                _globalInvertedIndex[val] = list.ToArray();
            }
        }

        public IDataRow this[string key]
        {
            get
            {
                if (_data.TryGetValue(key, out var rows) && rows.Count > 0)
                {
                    return rows[0];
                }
                throw new KeyNotFoundException($"Key '{key}' not found in field table '{Name}'.");
            }
        }

        public bool TryGetRow(string key, out IDataRow? row)
        {
            if (_data.TryGetValue(key, out var rows) && rows.Count > 0)
            {
                row = rows[0];
                return true;
            }
            row = null;
            return false;
        }

        public bool TryGetRows(string key, out IReadOnlyList<IDataRow> rows)
        {
            if (_data.TryGetValue(key, out var r))
            {
                rows = r;
                return true;
            }
            rows = Array.Empty<IDataRow>();
            return false;
        }

        public IReadOnlyList<IDataRow> GetRows(string key)
        {
            if (_data.TryGetValue(key, out var rows))
            {
                return rows;
            }
            return Array.Empty<IDataRow>();
        }

        public IReadOnlyList<string> GetValues(string key, string column)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(column))
            {
                return Array.Empty<string>();
            }

            if (_data.TryGetValue(key, out var rows) && rows.Count > 0)
            {
                var list = new List<string>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].TryGetValue(column, out var val) && !string.IsNullOrEmpty(val))
                    {
                        list.Add(val);
                    }
                }
                return list;
            }
            return Array.Empty<string>();
        }

        public bool ContainsKey(string key) => _data.ContainsKey(key);

        public IReadOnlyList<string> GetKeys(string value, string? column = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Array.Empty<string>();
            }

            if (string.IsNullOrEmpty(column))
            {
                if (_globalInvertedIndex.TryGetValue(value, out var globalKeys) && globalKeys.Count > 0)
                {
                    return globalKeys;
                }
                return Array.Empty<string>();
            }

            if (_invertedIndex.TryGetValue(column, out var valDict) &&
                valDict.TryGetValue(value, out var keys) && keys.Count > 0)
            {
                return keys;
            }
            return Array.Empty<string>();
        }

        public IReadOnlyDictionary<string, IReadOnlyList<IDataRow>> GetAll() => _data;

        #region 强类型即时投影与流式 LINQ

        public T Get<T>(string key) where T : class
        {
            if (TryGet<T>(key, out var item) && item != null)
            {
                return item;
            }
            throw new KeyNotFoundException($"Key '{key}' not found in field table '{Name}'.");
        }

        public bool TryGet<T>(string key, out T? value) where T : class
        {
            if (_data.TryGetValue(key, out var rows) && rows.Count > 0 && rows[0] is DataRow row)
            {
                var factory = GetProjectionFactory<T>();
                value = factory(row.RawValues);
                return true;
            }
            value = null;
            return false;
        }

        public IEnumerable<T> As<T>() where T : class
        {
            var factory = GetProjectionFactory<T>();
            foreach (var rows in _data.Values)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] is DataRow row)
                    {
                        yield return factory(row.RawValues);
                    }
                }
            }
        }

        public IEnumerable<KeyValuePair<string, T>> AsPairs<T>() where T : class
        {
            var factory = GetProjectionFactory<T>();
            foreach (var (key, rows) in _data)
            {
                if (rows.Count > 0 && rows[0] is DataRow row)
                {
                    yield return new KeyValuePair<string, T>(key, factory(row.RawValues));
                }
            }
        }

        public IReadOnlyList<string> GetKeys<T>(Func<T, bool> predicate) where T : class
        {
            ArgumentNullException.ThrowIfNull(predicate);

            var factory = GetProjectionFactory<T>();
            var result = new List<string>();

            foreach (var (key, rows) in _data)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] is DataRow row && predicate(factory(row.RawValues)))
                    {
                        result.Add(key);
                        break;
                    }
                }
            }
            return result;
        }

        private Func<string[], T> GetProjectionFactory<T>() where T : class
        {
            return (Func<string[], T>)_factoryCache.GetOrAdd(typeof(T), _ =>
            {
                var colList = Columns.ToList();
                var ctor = EntityMapper.ResolveConstructor<T>(colList);
                return EntityMapper.GetOrCreateCompiledFactory<T>(ctor, colList);
            });
        }

        #endregion

        public IEnumerator<KeyValuePair<string, IReadOnlyList<IDataRow>>> GetEnumerator() => _data.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
