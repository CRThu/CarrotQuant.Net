using System;
using System.Collections.Generic;
using System.Linq;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 静态标的维表注册表实现。管理已加载的维表实例。
    /// </summary>
    public class FieldRegistry : IFieldRegistry
    {
        private readonly Dictionary<string, IFieldTable> _tables = new(StringComparer.OrdinalIgnoreCase);

        public IFieldTable this[string name] => GetTable(name);

        public IFieldTable GetTable(string name)
        {
            if (_tables.TryGetValue(name, out var table))
            {
                return table;
            }
            throw new KeyNotFoundException($"Field table '{name}' is not registered.");
        }

        public bool HasTable(string name)
        {
            return _tables.ContainsKey(name);
        }

        public void Register(string name, IFieldTable table)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(table);

            _tables[name] = table;
        }

        public IReadOnlyList<string> GetTableNames()
        {
            return _tables.Keys.OrderBy(n => n).ToList();
        }
    }
}
