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
            string compression = "lz4")
        {
            return new CachedDataProvider(source, registry, storageRoot, pageSize, enableCache, refreshMode, tableId, compression);
        }
    }
}
