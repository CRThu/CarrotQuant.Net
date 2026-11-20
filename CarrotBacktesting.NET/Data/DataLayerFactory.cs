using System;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 数据层装配工厂。
    /// 基于数据配置模型（<see cref="DataConfig"/>）构建时序、事件与维表注册中心。
    /// </summary>
    public static class DataLayerFactory
    {
        /// <summary>
        /// 根据 DataConfig 配置自动构建完整的数据层组件套件。
        /// </summary>
        /// <param name="config">数据层配置清单。</param>
        /// <param name="startDate">可选回测起始日期（用于物理年份分区剪枝）。</param>
        /// <param name="endDate">可选回测结束日期（用于物理年份分区剪枝）。</param>
        /// <param name="isLive">是否为实盘环境。</param>
        /// <returns>已装配完成的时序、事件与维表注册中心元组。</returns>
        public static (ITimeseriesRegistry Timeseries, IEventRegistry Events, IFieldRegistry Fields) Create(
            DataConfig config,
            MarketTime? startDate = null,
            MarketTime? endDate = null,
            bool isLive = false)
        {
            ArgumentNullException.ThrowIfNull(config);
            var resolver = new StorageResolver(config.Root);

            // 1. 装配稠密时序矩阵注册表
            if (config.Timeseries == null || config.Timeseries.Count == 0)
            {
                throw new InvalidOperationException("DataConfig 中未配置任何 timeseries 表项，无法构建 ITimeseriesRegistry。回测引擎依赖主时序行情表作为推进时间轴。");
            }
            if (config.Timeseries.Count > 1)
            {
                throw new NotSupportedException($"DataConfig 目前仅支持单张主时序行情表，检测到配置了 {config.Timeseries.Count} 个 timeseries 表项。多时序行情表自动对齐与跨周期时钟推进机制尚未开放，请在配置中保留单张主时序行情表。");
            }
            var ts = TimeseriesRegistryFactory.Create(resolver, config.Timeseries[0], startDate, endDate, isLive);

            // 2. 装配异构事件注册中心
            var ev = EventRegistryFactory.Create(resolver, config.Events, startDate, endDate);

            // 3. 装配静态维表注册中心
            var fl = FieldRegistryFactory.Create(resolver, config.Fields);

            return (ts, ev, fl);
        }
    }
}
