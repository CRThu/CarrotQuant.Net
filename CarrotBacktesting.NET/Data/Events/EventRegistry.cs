using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 事件注册表实现。
    /// 管理所有已加载的事件表，支持按 streamName 检索。
    /// </summary>
    public class EventRegistry : IEventRegistry
    {
        private readonly Dictionary<string, object> _tables = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 注册一个强类型事件表。
        /// </summary>
        /// <typeparam name="T">事件数据实体类型。</typeparam>
        /// <param name="streamName">数据流名称（如 "adjustments"）。</param>
        /// <param name="table">事件表实例。</param>
        public void Register<T>(string streamName, IEventTable<T> table) where T : class
        {
            if (string.IsNullOrWhiteSpace(streamName))
                throw new ArgumentException("Stream name cannot be null or empty.", nameof(streamName));
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            _tables[streamName] = table;
        }

        /// <inheritdoc />
        public IEventTable<T> GetTable<T>(string streamName) where T : class
        {
            if (_tables.TryGetValue(streamName, out var table))
            {
                if (table is IEventTable<T> typed)
                    return typed;

                throw new InvalidCastException(
                    $"Event stream '{streamName}' is registered as {table.GetType().Name}, " +
                    $"not IEventTable<{typeof(T).Name}>.");
            }

            throw new KeyNotFoundException($"Event stream '{streamName}' not found. Available: [{string.Join(", ", _tables.Keys)}]");
        }

        /// <inheritdoc />
        public bool HasStream(string streamName)
        {
            return _tables.ContainsKey(streamName);
        }
    }
}
