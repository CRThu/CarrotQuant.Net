using System;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 事件流注册中心实现。管理已加载的事件流表实例。
    /// </summary>
    public class EventRegistry : IEventRegistry
    {
        private readonly object _syncLock = new();
        private readonly Dictionary<string, IEventTable> _tables = new(StringComparer.OrdinalIgnoreCase);
        private volatile IReadOnlyList<string> _cachedNames = Array.Empty<string>();
        private volatile IEventTable[] _cachedTables = Array.Empty<IEventTable>();

        public IEventTable this[string name] => GetTable(name);

        public IEventTable GetTable(string name)
        {
            lock (_syncLock)
            {
                if (_tables.TryGetValue(name, out var table))
                {
                    return table;
                }
            }
            throw new KeyNotFoundException($"Event table '{name}' is not registered.");
        }

        public bool HasTable(string name)
        {
            lock (_syncLock)
            {
                return _tables.ContainsKey(name);
            }
        }

        public void Register(string name, IEventTable table)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(table);

            lock (_syncLock)
            {
                _tables[name] = table;
                _cachedNames = _tables.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                _cachedTables = _tables.Values.ToArray();
            }
        }

        public IReadOnlyList<string> GetTableNames()
        {
            return _cachedNames;
        }

        /// <inheritdoc />
        public bool HasNewEvents(DateTime time, string? tableName = null)
        {
            DateTime date = time.Date;
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                lock (_syncLock)
                {
                    return _tables.TryGetValue(tableName, out var table) && table.ContainsDate(date);
                }
            }

            // 热路径：直接通过原子引用的不可变数组遍历，纯无锁并发安全且零 GC
            var tables = _cachedTables;
            for (int i = 0; i < tables.Length; i++)
            {
                if (tables[i].ContainsDate(date))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
