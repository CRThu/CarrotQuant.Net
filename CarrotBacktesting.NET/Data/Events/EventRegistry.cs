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
        private readonly Dictionary<string, IEventTable> _tables = new(StringComparer.OrdinalIgnoreCase);

        public IEventTable this[string name] => GetTable(name);

        public IEventTable GetTable(string name)
        {
            if (_tables.TryGetValue(name, out var table))
            {
                return table;
            }
            throw new KeyNotFoundException($"Event table '{name}' is not registered.");
        }

        public bool HasTable(string name)
        {
            return _tables.ContainsKey(name);
        }

        public void Register(string name, IEventTable table)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(table);

            _tables[name] = table;
        }

        public IReadOnlyList<string> GetTableNames()
        {
            return _tables.Keys.OrderBy(n => n).ToList();
        }

        /// <inheritdoc />
        public bool HasNewEvents(DateTime time, string? tableName = null)
        {
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                return _tables.TryGetValue(tableName, out var table) && table.ContainsDate(time.Date);
            }

            foreach (var table in _tables.Values)
            {
                if (table.ContainsDate(time.Date))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
