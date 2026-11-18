using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Engine;

namespace CarrotBacktesting.NET.Abstraction.Indicators
{
    /// <summary>
    /// 全局指标注册中心统一契约。
    /// 统筹增量流式指标池的声明式注册、多策略共享与指标实例查询。
    /// 面向策略层与上下文暴露纯瘦契约，隐藏内部引擎时间步驱动与并发重置方法。
    /// </summary>
    public interface IIndicatorRegistry
    {
        /// <summary>
        /// 获取当前所有已注册指标的名称列表。
        /// </summary>
        IReadOnlyList<string> Names { get; }

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
        IndicatorPool<T> GetOrAdd<T>(
            string name,
            Func<T> factory,
            PriceMode mode = PriceMode.Post) where T : class, IStreamIndicator;

        /// <summary>
        /// 声明并注册一个全市场流式指标（无需持有 Pool 实例时的声明式重载）。
        /// </summary>
        void Add<T>(
            string name,
            Func<T> factory,
            PriceMode mode = PriceMode.Post) where T : class, IStreamIndicator;

        /// <summary>
        /// 根据注册名称获取已存在的指标池。
        /// </summary>
        /// <typeparam name="T">指标类型约束。</typeparam>
        /// <param name="name">全局唯一指标标识名。</param>
        /// <returns>标的池强类型实例；若不存在则返回 null。</returns>
        IndicatorPool<T>? GetPool<T>(string name) where T : class, IStreamIndicator;

        /// <summary>
        /// 获取指定名称和标的全局列索引的强类型流式指标实例（热路径 O(1) 索引，零哈希开销）。
        /// </summary>
        /// <typeparam name="T">指标类型约束。</typeparam>
        /// <param name="name">全局唯一指标标识名。</param>
        /// <param name="symbolIndex">标的全局列索引（0 &lt;= symbolIndex &lt; SymbolCount）。</param>
        /// <returns>标的专属的强类型流式指标实例。</returns>
        T Get<T>(string name, int symbolIndex) where T : class, IStreamIndicator;

        /// <summary>
        /// 获取指定名称和标的代码的强类型流式指标实例。
        /// </summary>
        /// <typeparam name="T">指标类型约束。</typeparam>
        /// <param name="name">全局唯一指标标识名。</param>
        /// <param name="symbol">标的代码（如 "sh.600000"）。</param>
        /// <returns>标的专属的强类型流式指标实例。</returns>
        T Get<T>(string name, string symbol) where T : class, IStreamIndicator;

        /// <summary>
        /// 检查指定名称的指标是否已被注册。
        /// </summary>
        /// <param name="name">指标标识名。</param>
        /// <returns>若已存在返回 true，否则返回 false。</returns>
        bool Has(string name);
    }
}
