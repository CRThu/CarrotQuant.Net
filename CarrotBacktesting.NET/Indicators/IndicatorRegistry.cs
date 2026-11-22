using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Abstraction.Engine;
using CarrotBacktesting.NET.Abstraction.Indicators;

namespace CarrotBacktesting.NET.Indicators
{
    /// <summary>
    /// 全局指标注册中心具象实现。
    /// 统筹增量流式指标池的声明式注册、多策略共享与引擎主循环单次驱动。
    /// 遵循单一事实来源（SSOT），实现统一契约 <see cref="IIndicatorRegistry"/>。
    /// </summary>
    public sealed class IndicatorRegistry : IIndicatorRegistry
    {
        private readonly IMarketMetadata _metadata;
        private readonly ConcurrentDictionary<string, IndicatorPool> _pools = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private volatile IndicatorPool[] _activePools = Array.Empty<IndicatorPool>();

        /// <inheritdoc />
        public IReadOnlyList<string> Names => _pools.Keys.ToList();

        /// <summary>
        /// 构建指标注册中心实例。
        /// </summary>
        /// <param name="metadata">市场维度元数据契约。</param>
        public IndicatorRegistry(IMarketMetadata metadata)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        /// <inheritdoc />
        public IndicatorPool<T> GetOrAdd<T>(
            string name,
            Func<T> factory,
            string field = "close",
            PriceMode mode = PriceMode.Post) where T : class, IIndicator
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentException.ThrowIfNullOrWhiteSpace(field);

            // 显式双重检查锁定 (DCL)：彻底规避并发多次执行工厂委托导致的幽灵指标池泄漏
            if (!_pools.TryGetValue(name, out var pool))
            {
                lock (_lock)
                {
                    if (!_pools.TryGetValue(name, out pool))
                    {
                        var created = new IndicatorPool<T>(name, factory, _metadata, field, mode);
                        _pools[name] = created;
                        var list = new List<IndicatorPool>(_activePools) { created };
                        _activePools = list.ToArray();
                        return created;
                    }
                }
            }

            if (pool is IndicatorPool<T> typedPool)
            {
                if (typedPool.Mode != mode || !string.Equals(typedPool.Field, field, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"指标 '{name}' 已存在，但参数不匹配（现有字段: '{typedPool.Field}'，模式: {typedPool.Mode}；请求字段: '{field}'，模式: {mode}）。请使用不同名称或保持一致参数。");
                }
                return typedPool;
            }

            throw new InvalidOperationException(
                $"指标 '{name}' 已存在，但类型不匹配（现有类型: {pool.GetType().FullName}，请求类型: {typeof(IndicatorPool<T>).FullName}）。");
        }

        /// <inheritdoc />
        public IndicatorPool<T> GetOrAdd<T>(
            string name,
            Func<T> factory,
            IReadOnlyList<string> fields,
            PriceMode mode = PriceMode.Post) where T : class, IIndicator
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(fields);

            if (!_pools.TryGetValue(name, out var pool))
            {
                lock (_lock)
                {
                    if (!_pools.TryGetValue(name, out pool))
                    {
                        var created = new IndicatorPool<T>(name, factory, _metadata, fields, mode);
                        _pools[name] = created;
                        var list = new List<IndicatorPool>(_activePools) { created };
                        _activePools = list.ToArray();
                        return created;
                    }
                }
            }

            if (pool is IndicatorPool<T> typedPool)
            {
                bool fieldsMatch = typedPool.Fields != null && typedPool.Fields.SequenceEqual(fields, StringComparer.OrdinalIgnoreCase);
                if (typedPool.Mode != mode || !fieldsMatch)
                {
                    throw new InvalidOperationException(
                        $"指标 '{name}' 已存在，但参数不匹配（现有字段: [{string.Join(", ", typedPool.Fields ?? Array.Empty<string>())}]，模式: {typedPool.Mode}；请求字段: [{string.Join(", ", fields)}]，模式: {mode}）。请使用不同名称或保持一致参数。");
                }
                return typedPool;
            }

            throw new InvalidOperationException(
                $"指标 '{name}' 已存在，但类型不匹配（现有类型: {pool.GetType().FullName}，请求类型: {typeof(IndicatorPool<T>).FullName}）。");
        }

        /// <inheritdoc />
        public void Add<T>(
            string name,
            Func<T> factory,
            string field = "close",
            PriceMode mode = PriceMode.Post) where T : class, IIndicator
        {
            GetOrAdd(name, factory, field, mode);
        }

        /// <inheritdoc />
        public void Add<T>(
            string name,
            Func<T> factory,
            IReadOnlyList<string> fields,
            PriceMode mode = PriceMode.Post) where T : class, IIndicator
        {
            GetOrAdd(name, factory, fields, mode);
        }

        /// <inheritdoc />
        public IndicatorPool<T>? GetPool<T>(string name) where T : class, IIndicator
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (_pools.TryGetValue(name, out var pool))
            {
                if (pool is IndicatorPool<T> typedPool)
                {
                    return typedPool;
                }
                throw new InvalidOperationException(
                    $"指标 '{name}' 类型不匹配（现有类型: {pool.GetType().FullName}，请求类型: {typeof(IndicatorPool<T>).FullName}）。");
            }
            return null;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get<T>(string name, int symbolIndex) where T : class, IIndicator
        {
            var pool = GetPool<T>(name);
            if (pool != null)
            {
                return pool[symbolIndex];
            }
            throw new KeyNotFoundException($"未找到名称为 '{name}' 的已注册指标，请在策略 OnStart 中先调用 context.Indicators.GetOrAdd(\"{name}\", ...)。");
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get<T>(string name, string symbol) where T : class, IIndicator
        {
            var pool = GetPool<T>(name);
            if (pool != null)
            {
                return pool[symbol];
            }
            throw new KeyNotFoundException($"未找到名称为 '{name}' 的已注册指标，请在策略 OnStart 中先调用 context.Indicators.GetOrAdd(\"{name}\", ...)。");
        }

        /// <inheritdoc />
        public bool Has(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _pools.ContainsKey(name);
        }

        /// <summary>
        /// 供引擎主循环在当前时间步统一驱动所有已注册的指标池完成批量泵送更新（内部驱动，对策略隐藏）。
        /// </summary>
        /// <param name="context">当前引擎运行上下文。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Step(IEngineContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var pools = _activePools;
            for (int i = 0; i < pools.Length; i++)
            {
                pools[i].Step(context);
            }
        }

        /// <summary>
        /// 重置所有已注册指标的状态（内部驱动，对策略隐藏）。
        /// </summary>
        internal void Reset()
        {
            var pools = _activePools;
            for (int i = 0; i < pools.Length; i++)
            {
                pools[i].Reset();
            }
        }
    }
}
