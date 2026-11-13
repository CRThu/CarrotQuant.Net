using System;
using System.Collections.Generic;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 静态标的维表注册中心装配工厂。
    /// 支持基于 YAML 配置清单（FieldTableConfig）批量加载物理维表并构建 IFieldRegistry 实例。
    /// </summary>
    public static class FieldRegistryFactory
    {
        /// <summary>
        /// 根据存储寻址解析器与配置清单批量加载维表并构建 IFieldRegistry。
        /// </summary>
        /// <param name="resolver">物理存储寻址解析器。</param>
        /// <param name="configs">维表配置列表（来自 SessionConfig.Data.Fields）。若为 null 或空，返回空注册中心。</param>
        /// <returns>已装配完成的 IFieldRegistry 实例。</returns>
        public static IFieldRegistry Create(
            IStorageResolver resolver,
            IEnumerable<FieldTableConfig>? configs = null)
        {
            ArgumentNullException.ThrowIfNull(resolver);

            var registry = new FieldRegistry();
            if (configs == null) return registry;

            foreach (var cfg in configs)
            {
                if (string.IsNullOrWhiteSpace(cfg.TableId))
                {
                    continue;
                }

                string tableName = !string.IsNullOrWhiteSpace(cfg.Name) ? cfg.Name : cfg.TableId;
                var table = FieldSourceFactory.Create(resolver, cfg.TableId, cfg.KeyColumn);
                registry.Register(tableName, table);
            }

            return registry;
        }

        /// <summary>
        /// 根据物理存储根目录快速构建 IFieldRegistry。
        /// </summary>
        public static IFieldRegistry Create(
            string storageRoot,
            IEnumerable<FieldTableConfig>? configs = null)
        {
            return Create(new StorageResolver(storageRoot), configs);
        }
    }
}
