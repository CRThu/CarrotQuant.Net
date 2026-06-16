using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 事件注册表实现。
    /// 管理所有已加载的事件流，支持按 streamName 检索。
    /// </summary>
    public class EventRegistry : IEventRegistry
    {
        private readonly Dictionary<string, object> _providers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 注册一个事件提供器。
        /// </summary>
        /// <typeparam name="T">事件数据类型。</typeparam>
        /// <param name="streamName">数据流名称（如 "adjustments"）。</param>
        /// <param name="provider">事件提供器实例。</param>
        public void Register<T>(string streamName, IEventProvider<T> provider) where T : class
        {
            if (string.IsNullOrWhiteSpace(streamName))
                throw new ArgumentException("Stream name cannot be null or empty.", nameof(streamName));
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            _providers[streamName] = provider;
        }

        public IEventProvider<T> GetProvider<T>(string streamName) where T : class
        {
            if (_providers.TryGetValue(streamName, out var provider))
            {
                if (provider is IEventProvider<T> typed)
                    return typed;

                throw new InvalidCastException(
                    $"Event stream '{streamName}' is registered as {provider.GetType().Name}, " +
                    $"not IEventProvider<{typeof(T).Name}>.");
            }

            throw new KeyNotFoundException($"Event stream '{streamName}' not found. Available: [{string.Join(", ", _providers.Keys)}]");
        }

        public bool HasStream(string streamName)
        {
            return _providers.ContainsKey(streamName);
        }
    }
}
