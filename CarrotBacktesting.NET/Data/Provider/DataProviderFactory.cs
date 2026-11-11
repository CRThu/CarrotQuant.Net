using System.IO;
using CarrotBacktesting.NET.Abstraction.Data;
using CarrotBacktesting.NET.Config;

namespace CarrotBacktesting.NET.Data
{
    public static class DataProviderFactory
    {
        public static IDataProvider Create(
            string storageRoot,
            IFieldRegistry registry, 
            IMarketSeriesSource source,
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
            return new CachedDataProvider(source, registry, storageRoot, pageSize, enableCache, refreshMode, tableId, compression, commitBatchSize, maxPages, prefetch, isLive);
        }
    }
}
