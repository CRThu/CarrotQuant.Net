using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    /// <summary>
    /// 时序注册表装配工厂。
    /// 统一装配并初始化 <see cref="TimeseriesRegistry"/> 实例。
    /// </summary>
    public static class TimeseriesRegistryFactory
    {
        public static ITimeseriesRegistry Create(
            string storageRoot,
            IFieldRegistry registry, 
            ITimeseriesSource source,
            int pageSize = 4096,
            bool enableCache = false,
            string refreshMode = "lazy",
            string tableId = "default",
            string compression = "lz4",
            int commitBatchSize = 64,
            int maxPages = 4,
            bool prefetch = true,
            bool isLive = false)
        {
            return new TimeseriesRegistry(source, registry, storageRoot, pageSize, enableCache, refreshMode, tableId, compression, commitBatchSize, maxPages, prefetch, isLive);
        }
    }
}
