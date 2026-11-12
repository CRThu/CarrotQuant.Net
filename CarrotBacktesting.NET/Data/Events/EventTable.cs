using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时态事件表实现。支持日期截面检索、二维主键点查与实体映射。
    /// </summary>
    public class EventTable : IEventTable
    {
        private readonly Dictionary<DateTime, IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>> _dayMaps;
        private readonly Dictionary<DateTime, IReadOnlyList<IDataRow>> _globalEvents;
        private readonly DateTime[] _sortedDates;
        private readonly IReadOnlyList<string> _valueColumns;
        private readonly ConcurrentDictionary<Type, object> _factoryCache = new();

        public string Name { get; }
        public int Count => _dayMaps.Count;
        public IEnumerable<DateTime> Dates => _sortedDates;

        public EventTable(
            string name,
            IReadOnlyList<string> valueColumns,
            Dictionary<DateTime, Dictionary<string, List<DataRow>>> data,
            Dictionary<DateTime, List<DataRow>>? globalEvents = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _valueColumns = valueColumns ?? throw new ArgumentNullException(nameof(valueColumns));
            ArgumentNullException.ThrowIfNull(data);

            _dayMaps = new Dictionary<DateTime, IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>>(data.Count);
            foreach (var (date, dayDict) in data)
            {
                var frozenDay = new Dictionary<string, IReadOnlyList<IDataRow>>(dayDict.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var (sym, list) in dayDict)
                {
                    if (list != null && list.Count > 0)
                    {
                        frozenDay[sym] = list.ToArray();
                    }
                }
                _dayMaps[date.Date] = frozenDay;
            }

            _globalEvents = new Dictionary<DateTime, IReadOnlyList<IDataRow>>();
            if (globalEvents != null)
            {
                foreach (var (date, list) in globalEvents)
                {
                    if (list != null && list.Count > 0)
                    {
                        _globalEvents[date.Date] = list.ToArray();
                    }
                }
            }

            _sortedDates = _dayMaps.Keys.OrderBy(d => d).ToArray();
        }

        public IReadOnlyDictionary<string, IReadOnlyList<IDataRow>> this[DateTime date]
        {
            get
            {
                if (_dayMaps.TryGetValue(date.Date, out var dayMap))
                {
                    return dayMap;
                }
                throw new KeyNotFoundException($"No events recorded on {date:yyyy-MM-dd} in stream '{Name}'.");
            }
        }

        public IDataRow this[DateTime date, string key]
        {
            get
            {
                if (TryGetFirst(date, key, out var row) && row != null)
                {
                    return row;
                }
                throw new KeyNotFoundException($"Key '{key}' not found on {date:yyyy-MM-dd} in stream '{Name}'.");
            }
        }

        public bool TryGetDay(DateTime date, out IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>? dayMap)
        {
            return _dayMaps.TryGetValue(date.Date, out dayMap);
        }

        public bool TryGet(DateTime timestamp, string key, out IReadOnlyList<IDataRow> rows)
        {
            if (_dayMaps.TryGetValue(timestamp.Date, out var dayMap) && dayMap.TryGetValue(key, out var r))
            {
                rows = r;
                return true;
            }
            rows = Array.Empty<IDataRow>();
            return false;
        }

        public IReadOnlyList<IDataRow> GetRows(DateTime timestamp, string key)
        {
            if (TryGet(timestamp, key, out var rows))
            {
                return rows;
            }
            return Array.Empty<IDataRow>();
        }

        public bool TryGetFirst(DateTime timestamp, string key, out IDataRow? row)
        {
            if (TryGet(timestamp, key, out var rows) && rows.Count > 0)
            {
                row = rows[0];
                return true;
            }
            row = null;
            return false;
        }

        public bool TryGetGlobal(DateTime timestamp, out IReadOnlyList<IDataRow> globalRows)
        {
            if (_globalEvents.TryGetValue(timestamp.Date, out var g) && g.Count > 0)
            {
                globalRows = g;
                return true;
            }
            globalRows = Array.Empty<IDataRow>();
            return false;
        }

        public bool ContainsDate(DateTime date) => _dayMaps.ContainsKey(date.Date);

        #region 强类型即时点查与流式 LINQ

        public T Get<T>(DateTime timestamp, string key) where T : class
        {
            if (TryGet<T>(timestamp, key, out var value) && value != null)
            {
                return value;
            }
            throw new KeyNotFoundException($"Key '{key}' not found on {timestamp:yyyy-MM-dd} in event stream '{Name}'.");
        }

        public bool TryGet<T>(DateTime timestamp, string key, out T? value) where T : class
        {
            if (TryGetFirst(timestamp, key, out var row) && row is DataRow dataRow)
            {
                var factory = GetProjectionFactory<T>();
                value = factory(dataRow.RawValues);
                return true;
            }
            value = null;
            return false;
        }

        public IEnumerable<T> As<T>(DateTime timestamp) where T : class
        {
            if (_dayMaps.TryGetValue(timestamp.Date, out var dayMap))
            {
                var factory = GetProjectionFactory<T>();
                foreach (var rows in dayMap.Values)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (rows[i] is DataRow dataRow)
                        {
                            yield return factory(dataRow.RawValues);
                        }
                    }
                }
            }
        }

        public IEnumerable<T> As<T>(string key) where T : class
        {
            var factory = GetProjectionFactory<T>();
            foreach (var date in _sortedDates)
            {
                if (_dayMaps[date].TryGetValue(key, out var rows))
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (rows[i] is DataRow dataRow)
                        {
                            yield return factory(dataRow.RawValues);
                        }
                    }
                }
            }
        }

        public IEnumerable<T> As<T>() where T : class
        {
            var factory = GetProjectionFactory<T>();
            foreach (var dayMap in _dayMaps.Values)
            {
                foreach (var rows in dayMap.Values)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (rows[i] is DataRow dataRow)
                        {
                            yield return factory(dataRow.RawValues);
                        }
                    }
                }
            }
        }

        private Func<string[], T> GetProjectionFactory<T>() where T : class
        {
            return (Func<string[], T>)_factoryCache.GetOrAdd(typeof(T), _ =>
            {
                var ctor = EntityMapper.ResolveConstructor<T>(_valueColumns);
                return EntityMapper.GetOrCreateCompiledFactory<T>(ctor, _valueColumns);
            });
        }

        #endregion

        public IEnumerator<KeyValuePair<DateTime, IReadOnlyDictionary<string, IReadOnlyList<IDataRow>>>> GetEnumerator()
        {
            return _dayMaps.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
