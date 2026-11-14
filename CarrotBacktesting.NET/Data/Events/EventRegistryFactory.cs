using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时态事件表注册中心装配工厂。
    /// 支持基于 YAML 配置清单（EventStreamConfig）批量加载物理事件表并构建 IEventRegistry 实例。
    /// </summary>
    public static class EventRegistryFactory
    {
        /// <summary>
        /// 根据存储寻址解析器与配置清单批量加载事件表并构建 IEventRegistry。
        /// </summary>
        /// <param name="resolver">物理存储寻址解析器。</param>
        /// <param name="configs">事件流配置列表（来自 SessionConfig.Data.Events）。若为 null 或空，返回空注册中心。</param>
        /// <param name="startDate">可选回测起始日期（用于物理年份分区剪枝）。</param>
        /// <param name="endDate">可选回测结束日期（用于物理年份分区剪枝）。</param>
        /// <returns>已装配完成的 IEventRegistry 实例。</returns>
        public static IEventRegistry Create(
            IStorageResolver resolver,
            IEnumerable<EventStreamConfig>? configs = null,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);

            var registry = new EventRegistry();
            if (configs == null) return registry;

            foreach (var cfg in configs)
            {
                if (string.IsNullOrWhiteSpace(cfg.TableId))
                {
                    continue;
                }

                string tableName = !string.IsNullOrWhiteSpace(cfg.Name) ? cfg.Name : cfg.TableId;
                var nullableFields = cfg.NullableFields != null
                    ? new HashSet<string>(cfg.NullableFields, StringComparer.OrdinalIgnoreCase)
                    : null;
                var table = EventSourceFactory.Create(resolver, cfg.TableId, startDate, endDate, nullableFields);
                registry.Register(tableName, table);
            }

            return registry;
        }

        /// <summary>
        /// 根据物理存储根目录快速构建 IEventRegistry。
        /// </summary>
        public static IEventRegistry Create(
            string storageRoot,
            IEnumerable<EventStreamConfig>? configs = null,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            return Create(new StorageResolver(storageRoot), configs, startDate, endDate);
        }
    }
}
