using System;
using System.Collections.Generic;
using System.Globalization;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 动态数据行实现。基于字符串数组与列序号映射，提供字段访问与类型转换方法。
    /// </summary>
    public class DataRow : IDataRow
    {
        private readonly string[] _values;
        private readonly Dictionary<string, int> _ordinals;

        /// <inheritdoc />
        public string Key { get; }

        /// <inheritdoc />
        public DateTime? Timestamp { get; }

        /// <summary>
        /// 底层原始字符串数组（供 EntityMapper 预编译委托高速投影）。
        /// </summary>
        public string[] RawValues => _values;

        /// <inheritdoc />
        public IReadOnlyCollection<string> Columns => _ordinals.Keys;

        /// <inheritdoc />
        public int Count => _values.Length;

        public DataRow(string key, DateTime? timestamp, string[] values, Dictionary<string, int> ordinals)
        {
            Key = key ?? string.Empty;
            Timestamp = timestamp;
            _values = values ?? throw new ArgumentNullException(nameof(values));
            _ordinals = ordinals ?? throw new ArgumentNullException(nameof(ordinals));
        }

        public DataRow(string key, string[] values, Dictionary<string, int> ordinals)
            : this(key, null, values, ordinals)
        {
        }

        public DataRow(string[] values, Dictionary<string, int> ordinals)
            : this(string.Empty, null, values, ordinals)
        {
        }

        /// <inheritdoc />
        public string this[string columnName]
        {
            get
            {
                if (_ordinals.TryGetValue(columnName, out int ord) && ord >= 0 && ord < _values.Length)
                {
                    return _values[ord];
                }
                throw new KeyNotFoundException($"Column '{columnName}' not found in DataRow. Available: [{string.Join(", ", _ordinals.Keys)}]");
            }
        }

        /// <inheritdoc />
        public string this[int index] => _values[index];

        /// <inheritdoc />
        public string GetString(string columnName, string defaultValue = "")
        {
            if (_ordinals.TryGetValue(columnName, out int ord) && ord >= 0 && ord < _values.Length)
            {
                return _values[ord] ?? defaultValue;
            }
            return defaultValue;
        }

        /// <inheritdoc />
        public double GetDouble(string columnName, double defaultValue = 0.0)
        {
            string val = GetString(columnName);
            if (string.IsNullOrWhiteSpace(val)) return defaultValue;
            return double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double result) ? result : defaultValue;
        }

        /// <inheritdoc />
        public float GetFloat(string columnName, float defaultValue = 0.0f)
        {
            string val = GetString(columnName);
            if (string.IsNullOrWhiteSpace(val)) return defaultValue;
            return float.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out float result) ? result : defaultValue;
        }

        /// <inheritdoc />
        public decimal GetDecimal(string columnName, decimal defaultValue = 0m)
        {
            string val = GetString(columnName);
            if (string.IsNullOrWhiteSpace(val)) return defaultValue;
            return decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result) ? result : defaultValue;
        }

        /// <inheritdoc />
        public int GetInt(string columnName, int defaultValue = 0)
        {
            string val = GetString(columnName);
            if (string.IsNullOrWhiteSpace(val)) return defaultValue;
            return int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out int result) ? result : defaultValue;
        }

        /// <inheritdoc />
        public long GetLong(string columnName, long defaultValue = 0)
        {
            string val = GetString(columnName);
            if (string.IsNullOrWhiteSpace(val)) return defaultValue;
            return long.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out long result) ? result : defaultValue;
        }

        /// <inheritdoc />
        public bool GetBoolean(string columnName, bool defaultValue = false)
        {
            string val = GetString(columnName);
            if (string.IsNullOrWhiteSpace(val)) return defaultValue;
            return bool.TryParse(val, out bool result) ? result : defaultValue;
        }

        /// <inheritdoc />
        public DateTime GetDateTime(string columnName, DateTime defaultValue = default)
        {
            string val = GetString(columnName);
            if (string.IsNullOrWhiteSpace(val)) return defaultValue;

            if (long.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out long ts))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ts).DateTime;
            }

            if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
            {
                return result;
            }

            return defaultValue;
        }

        /// <inheritdoc />
        public bool TryGetValue(string columnName, out string? value)
        {
            if (_ordinals.TryGetValue(columnName, out int ord) && ord >= 0 && ord < _values.Length)
            {
                value = _values[ord];
                return true;
            }
            value = null;
            return false;
        }

        /// <inheritdoc />
        public bool ContainsColumn(string columnName) => _ordinals.ContainsKey(columnName);
    }
}
