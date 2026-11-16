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
    /// 全局指标注册中心。
    /// 统筹增量流式指标池的声明式注册、多策略共享与引擎主循环单次驱动。
    /// 纯具象类直挂上下文，消除虚派发，对策略屏蔽内部步进调度方法。
    /// </summary>
    public sealed class IndicatorRegistry
    {
        private readonly IMarketMetadata _metadata;
        private readonly ConcurrentDictionary<string, IndicatorPool> _pools = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private volatile IndicatorPool[] _activePools = Array.Empty<IndicatorPool>();

        /// <summary>
        /// 获取当前所有已注册指标的名称列表。
        /// </summary>
        public IReadOnlyList<string> Names => _pools.Keys.ToList();

        /// <summary>
        /// 构建指标注册中心实例。
        /// </summary>
        /// <param name="metadata">市场维度元数据契约。</param>
        public IndicatorRegistry(IMarketMetadata metadata)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        /// <summary>
        /// 获取或声明注册一个全市场流式指标标的池（IndicatorPool）。
        /// 遵循约定优于配置：缺省复权模式为 PriceMode.Post。
        /// 若已存在同名指标则直接返回已有实例，保证多策略间全局单步只算一次。
        /// </summary>
        /// <typeparam name="T">指标类型约束。</typeparam>
        /// <param name="name">全局唯一指标标识名（如 "sma20", "macd"）。</param>
        /// <param name="factory">单个标的指标实例构建工厂委托。</param>
        /// <param name="mode">价格复权模式，默认为 PriceMode.Post。</param>
        /// <returns>标的池强类型具象实例。</returns>
        public IndicatorPool<T> GetOrAdd<T>(
            string name,
            Func<T> factory,
            PriceMode mode = PriceMode.Post) where T : class, IStreamIndicator
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(factory);

            // 显式双重检查锁定 (DCL)：彻底规避并发多次执行工厂委托导致的幽灵指标池泄漏
            if (!_pools.TryGetValue(name, out var pool))
            {
                lock (_lock)
                {
                    if (!_pools.TryGetValue(name, out pool))
                    {
                        var created = new IndicatorPool<T>(name, factory, _metadata, mode);
                        _pools[name] = created;
                        var list = new List<IndicatorPool>(_activePools) { created };
                        _activePools = list.ToArray();
                        return created;
                    }
                }
            }

            if (pool is IndicatorPool<T> typedPool)
            {
                if (typedPool.Mode != mode)
                {
                    throw new InvalidOperationException(
                        $"指标 '{name}' 已存在，但复权模式不匹配（现有模式: {typedPool.Mode}，请求模式: {mode}）。请使用不同名称或保持一致的复权模式。");
                }
                return typedPool;
            }

            throw new InvalidOperationException(
                $"指标 '{name}' 已存在，但类型不匹配（现有类型: {pool.GetType().FullName}，请求类型: {typeof(IndicatorPool<T>).FullName}）。");
        }

        /// <summary>
        /// 声明并注册一个全市场流式指标（无需持有 Pool 实例时的声明式重载）。
        /// </summary>
        public void Add<T>(
            string name,
            Func<T> factory,
            PriceMode mode = PriceMode.Post) where T : class, IStreamIndicator
        {
            GetOrAdd(name, factory, mode);
        }

        /// <summary>
        /// 根据注册名称获取已存在的指标池。
        /// </summary>
        /// <typeparam name="T">指标类型约束。</typeparam>
        /// <param name="name">全局唯一指标标识名。</param>
        /// <returns>标的池强类型实例；若不存在则返回 null。</returns>
        public IndicatorPool<T>? GetPool<T>(string name) where T : class, IStreamIndicator
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

        /// <summary>
        /// 获取指定名称和标的全局列索引的强类型流式指标实例（热路径 O(1) 索引，零哈希开销）。
        /// </summary>
        /// <typeparam name="T">指标类型约束。</typeparam>
        /// <param name="name">全局唯一指标标识名。</param>
        /// <param name="symbolIndex">标的全局列索引（0 &lt;= symbolIndex &lt; SymbolCount）。</param>
        /// <returns>标的专属的强类型流式指标实例。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get<T>(string name, int symbolIndex) where T : class, IStreamIndicator
        {
            var pool = GetPool<T>(name);
            if (pool != null)
            {
                return pool[symbolIndex];
            }
            throw new KeyNotFoundException($"未找到名称为 '{name}' 的已注册指标，请在策略 OnStart 中先调用 context.Indicators.GetOrAdd(\"{name}\", ...)。");
        }

        /// <summary>
        /// 获取指定名称和标的代码的强类型流式指标实例。
        /// </summary>
        /// <typeparam name="T">指标类型约束。</typeparam>
        /// <param name="name">全局唯一指标标识名。</param>
        /// <param name="symbol">标的代码（如 "sh.600000"）。</param>
        /// <returns>标的专属的强类型流式指标实例。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get<T>(string name, string symbol) where T : class, IStreamIndicator
        {
            var pool = GetPool<T>(name);
            if (pool != null)
            {
                return pool[symbol];
            }
            throw new KeyNotFoundException($"未找到名称为 '{name}' 的已注册指标，请在策略 OnStart 中先调用 context.Indicators.GetOrAdd(\"{name}\", ...)。");
        }

        /// <summary>
        /// 检查指定名称的指标是否已被注册。
        /// </summary>
        /// <param name="name">指标标识名。</param>
        /// <returns>若已存在返回 true，否则返回 false。</returns>
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
